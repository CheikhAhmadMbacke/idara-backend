using System.Text.Json;
using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Idara.API.Services
{
    /// <inheritdoc cref="IPayoutSettlementService"/>
    public class PayoutSettlementService : IPayoutSettlementService
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notif;
        private readonly ILogger<PayoutSettlementService> _logger;

        public PayoutSettlementService(
            AppDbContext context, INotificationService notif, ILogger<PayoutSettlementService> logger)
        {
            _context = context;
            _notif = notif;
            _logger = logger;
        }

        /// <summary>Notifie (push uniquement) les SchoolAdmin de l'école après une
        /// transition de retrait. Best-effort, post-commit, ne lève jamais. Clic →
        /// page solde/wallet.</summary>
        private async Task NotifyAdminsAsync(
            int schoolId, BilingualMessage msg, string templateCode, int withdrawalId)
        {
            try
            {
                var admins = await _context.Users
                    .Where(u => u.SchoolId == schoolId && !u.IsDeleted && u.Role == UserRoles.SchoolAdmin)
                    .Select(u => new { u.Id, u.PreferredLanguage })
                    .ToListAsync();
                foreach (var a in admins)
                {
                    await _notif.SendPushOnlyAsync(new PushOnlyRequest(
                        UserId: a.Id,
                        PreferredLanguage: a.PreferredLanguage ?? "fr",
                        Message: msg,
                        TemplateCode: templateCode,
                        RelatedEntityId: withdrawalId,
                        PushRoute: "/payments/overview"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[payout-settle] Échec notif admins withdrawal {Id} — pas bloquant", withdrawalId);
            }
        }

        public async Task<PayoutSettlementOutcome> SettleCompletedAsync(
            int withdrawalId, string? disbursementId, long feesFcfa, long netReceivedFcfa,
            DateTime? completedAtUtc, string source, CancellationToken ct = default)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            // On charge d'abord le retrait (pour son SchoolId), PUIS on verrouille
            // le bon wallet (SELECT ... FOR UPDATE).
            var withdrawal = await _context.Withdrawals.FirstOrDefaultAsync(w => w.Id == withdrawalId, ct)
                ?? throw new InvalidOperationException($"Withdrawal.Id={withdrawalId} introuvable (SettleCompleted)");

            // Retrait de GAINS PLATEFORME : chemin isolé (aucun wallet école touché).
            if (withdrawal.IsPlatform)
                return await SettlePlatformCompletedAsync(
                    tx, withdrawal, disbursementId, feesFcfa, netReceivedFcfa, completedAtUtc, source, ct);

            var wallet = await _context.LockWalletAsync(withdrawal.SchoolId!.Value, ct)
                ?? throw new InvalidOperationException($"SchoolWallet manquant pour SchoolId={withdrawal.SchoolId}");

            // SOUS le verrou wallet : recharger le statut du retrait pour voir
            // l'effet d'un settler concurrent (webhook + poll job) déjà committé.
            // Le verrou sérialise, mais le retrait a été lu AVANT de l'acquérir →
            // sans ce reload on rejouerait un débit déjà appliqué (lost update).
            await _context.Entry(withdrawal).ReloadAsync(ct);

            var completedAt = completedAtUtc?.ToUtcSafe() ?? DateTime.UtcNow;
            withdrawal.SenePayDisbursementId ??= disbursementId;

            switch (withdrawal.Status)
            {
                case WithdrawalStatus.Completed:
                    await tx.RollbackAsync(ct);
                    return PayoutSettlementOutcome.NoOp;

                case WithdrawalStatus.Initiated:
                case WithdrawalStatus.UnderVerification:
                {
                    // Clôture normale : la réservation (Available -= X faite à
                    // l'init) devient définitive. On vide le Pending. PAS de
                    // WalletTransaction (Available ne bouge pas — la Reservation
                    // -X portait déjà le débit, cf. gotcha §55).
                    withdrawal.Status = WithdrawalStatus.Completed;
                    withdrawal.CompletedAt = completedAt;
                    withdrawal.FeesFcfa = feesFcfa;
                    withdrawal.NetReceivedFcfa = netReceivedFcfa;
                    withdrawal.LastCheckedAt = DateTime.UtcNow;

                    wallet.PendingBalance -= withdrawal.AmountFcfa;
                    wallet.TotalWithdrawnLifetime += withdrawal.AmountFcfa;
                    wallet.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    _logger.LogInformation(
                        "[payout-settle] Withdrawal {Id} → Completed (source={Source}, {Amount} FCFA)",
                        withdrawal.Id, source, withdrawal.AmountFcfa);
                    await NotifyAdminsAsync(withdrawal.SchoolId!.Value,
                        NotificationTemplates.WithdrawalDone(withdrawal.AmountFcfa),
                        "WITHDRAWAL_DONE", withdrawal.Id);
                    return PayoutSettlementOutcome.SettledCompleted;
                }

                case WithdrawalStatus.Failed:
                {
                    // CORRECTEUR (scénario S3/D) : on avait restitué + marqué
                    // Failed, mais un `completed` authentique arrive → l'argent
                    // EST sorti. Il faut annuler la restitution par un re-débit
                    // de l'Available.
                    if (withdrawal.ReversedAt != null)
                    {
                        await tx.RollbackAsync(ct);
                        return PayoutSettlementOutcome.NoOp; // déjà corrigé
                    }

                    // On vérifie SOUS le verrou que l'Available suffit pour
                    // re-débiter, pour ne pas heurter la contrainte CHECK
                    // (CK_SchoolWallets_NonNegative) et corrompre la tx. Le
                    // verrou garantit que le solde ne bouge pas sous nos pieds.
                    if (wallet.AvailableBalance < withdrawal.AmountFcfa)
                    {
                        await tx.RollbackAsync(ct);
                        await RaiseAlertAsync(
                            PayoutAlertType.CorrectionImpossible,
                            withdrawal.SchoolId, withdrawal.Id,
                            $"Décaissement #{withdrawal.Id} confirmé `completed` après restitution, " +
                            $"mais solde Available ({wallet.AvailableBalance}) insuffisant pour re-débiter " +
                            $"{withdrawal.AmountFcfa} FCFA. Perte/dette à régulariser manuellement.",
                            new { withdrawal.SchoolId, withdrawal.Id, withdrawal.AmountFcfa,
                                  wallet.AvailableBalance, disbursementId, source },
                            ct);
                        return PayoutSettlementOutcome.CorrectionImpossible;
                    }

                    withdrawal.Status = WithdrawalStatus.Completed;
                    withdrawal.ReversedAt = DateTime.UtcNow;
                    withdrawal.CompletedAt = completedAt;
                    withdrawal.FailedAt = null;
                    withdrawal.FailureReason = null;
                    withdrawal.FeesFcfa = feesFcfa;
                    withdrawal.NetReceivedFcfa = netReceivedFcfa;
                    withdrawal.LastCheckedAt = DateTime.UtcNow;

                    // Re-débit définitif de l'Available + transaction Adjustment
                    // signée négative (préserve Σ tx == Available).
                    wallet.AvailableBalance -= withdrawal.AmountFcfa;
                    wallet.TotalWithdrawnLifetime += withdrawal.AmountFcfa;
                    wallet.UpdatedAt = DateTime.UtcNow;

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        SchoolId = withdrawal.SchoolId!.Value,
                        Type = WalletTransactionType.Adjustment,
                        AmountFcfa = -withdrawal.AmountFcfa,
                        BalanceAfter = wallet.AvailableBalance,
                        RelatedEntity = WalletRelatedEntity.Withdrawal,
                        RelatedId = withdrawal.Id,
                        Note = $"Re-débit correcteur retrait #{withdrawal.Id} (completed après restitution)",
                        OccurredAt = DateTime.UtcNow
                    });

                    // Alerte ajoutée DANS la même tx (atomique avec la correction).
                    AddAlertEntity(
                        PayoutAlertType.DoubleSpendCorrected,
                        withdrawal.SchoolId, withdrawal.Id,
                        $"Restitution du décaissement #{withdrawal.Id} ANNULÉE : `completed` authentique " +
                        $"reçu après un Failed. Re-débit de {withdrawal.AmountFcfa} FCFA. Double dépense évitée.",
                        new { withdrawal.SchoolId, withdrawal.Id, withdrawal.AmountFcfa, disbursementId, source });

                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);

                    _logger.LogCritical(
                        "[ALERT][payout] DOUBLE-SPEND CORRECTED Withdrawal {Id} (School {SchoolId}, {Amount} FCFA) — restitution annulée par re-débit (source={Source})",
                        withdrawal.Id, withdrawal.SchoolId, withdrawal.AmountFcfa, source);
                    await NotifyAdminsAsync(withdrawal.SchoolId!.Value,
                        NotificationTemplates.WithdrawalDone(withdrawal.AmountFcfa),
                        "WITHDRAWAL_DONE", withdrawal.Id);
                    return PayoutSettlementOutcome.Corrected;
                }

                default: // Cancelled
                    await tx.RollbackAsync(ct);
                    return PayoutSettlementOutcome.NoOp;
            }
        }

        public async Task<PayoutSettlementOutcome> SettleFailedAsync(
            int withdrawalId, string reason, string? disbursementId,
            DateTime? failedAtUtc, string source, CancellationToken ct = default)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var withdrawal = await _context.Withdrawals.FirstOrDefaultAsync(w => w.Id == withdrawalId, ct)
                ?? throw new InvalidOperationException($"Withdrawal.Id={withdrawalId} introuvable (SettleFailed)");

            if (withdrawal.IsPlatform)
                return await SettlePlatformFailedAsync(
                    tx, withdrawal, reason, disbursementId, failedAtUtc, source, ct);

            var wallet = await _context.LockWalletAsync(withdrawal.SchoolId!.Value, ct)
                ?? throw new InvalidOperationException($"SchoolWallet manquant pour SchoolId={withdrawal.SchoolId}");

            // SOUS le verrou : recharger le statut (cf. SettleCompletedAsync).
            await _context.Entry(withdrawal).ReloadAsync(ct);
            withdrawal.SenePayDisbursementId ??= disbursementId;

            switch (withdrawal.Status)
            {
                case WithdrawalStatus.Initiated:
                case WithdrawalStatus.UnderVerification:
                {
                    // Restitution : Pending → Available + transaction Release.
                    withdrawal.Status = WithdrawalStatus.Failed;
                    withdrawal.FailedAt = failedAtUtc?.ToUtcSafe() ?? DateTime.UtcNow;
                    withdrawal.FailureReason = Truncate(reason, 480);
                    withdrawal.LastCheckedAt = DateTime.UtcNow;

                    wallet.PendingBalance -= withdrawal.AmountFcfa;
                    wallet.AvailableBalance += withdrawal.AmountFcfa;
                    wallet.UpdatedAt = DateTime.UtcNow;

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        SchoolId = withdrawal.SchoolId!.Value,
                        Type = WalletTransactionType.Release,
                        AmountFcfa = withdrawal.AmountFcfa, // release = signé positif
                        BalanceAfter = wallet.AvailableBalance,
                        RelatedEntity = WalletRelatedEntity.Withdrawal,
                        RelatedId = withdrawal.Id,
                        Note = $"Restitution retrait #{withdrawal.Id} ({Truncate(reason, 80)})",
                        OccurredAt = DateTime.UtcNow
                    });

                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    _logger.LogInformation(
                        "[payout-settle] Withdrawal {Id} → Failed + restitué (source={Source}, raison={Reason})",
                        withdrawal.Id, source, Truncate(reason, 120));
                    await NotifyAdminsAsync(withdrawal.SchoolId!.Value,
                        NotificationTemplates.WithdrawalFailed(withdrawal.AmountFcfa),
                        "WITHDRAWAL_FAILED", withdrawal.Id);
                    return PayoutSettlementOutcome.Restituted;
                }

                case WithdrawalStatus.Completed:
                {
                    // `failed` arrive APRÈS un completed : l'argent est déjà parti.
                    // On NE restitue PAS (sinon on rendrait un solde déjà sorti).
                    await tx.RollbackAsync(ct);
                    await RaiseAlertAsync(
                        PayoutAlertType.FailedAfterCompleted,
                        withdrawal.SchoolId, withdrawal.Id,
                        $"Statut `failed` reçu sur le décaissement #{withdrawal.Id} déjà Completed. " +
                        "Aucune restitution effectuée (fonds déjà sortis). Vérification manuelle requise.",
                        new { withdrawal.SchoolId, withdrawal.Id, reason, disbursementId, source },
                        ct);
                    return PayoutSettlementOutcome.FailedAfterCompleted;
                }

                default: // Failed (idempotent) / Cancelled
                    await tx.RollbackAsync(ct);
                    return PayoutSettlementOutcome.NoOp;
            }
        }

        public async Task<PayoutSettlementOutcome> MarkUnderVerificationAsync(
            int withdrawalId, string? disbursementId, string reason,
            string source, CancellationToken ct = default)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var withdrawal = await _context.Withdrawals.FirstOrDefaultAsync(w => w.Id == withdrawalId, ct)
                ?? throw new InvalidOperationException($"Withdrawal.Id={withdrawalId} introuvable (MarkUnderVerification)");

            if (withdrawal.IsPlatform)
                return await MarkPlatformUnderVerificationAsync(tx, withdrawal, disbursementId, reason, source, ct);

            // Verrou pour sérialiser vs un webhook qui clôturerait au même moment.
            await _context.LockWalletAsync(withdrawal.SchoolId!.Value, ct);

            // SOUS le verrou : recharger le statut (un webhook a pu trancher).
            await _context.Entry(withdrawal).ReloadAsync(ct);

            if (withdrawal.Status != WithdrawalStatus.Initiated)
            {
                // Déjà tranché (webhook plus rapide) ou déjà en vérification.
                await tx.RollbackAsync(ct);
                return PayoutSettlementOutcome.NoOp;
            }

            withdrawal.Status = WithdrawalStatus.UnderVerification;
            withdrawal.SenePayDisbursementId ??= disbursementId;
            withdrawal.VerificationStartedAt = DateTime.UtcNow;
            withdrawal.NextVerificationAt = DateTime.UtcNow.AddSeconds(30);
            withdrawal.VerificationAttempts = 0;

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            _logger.LogWarning(
                "[payout-settle] Withdrawal {Id} → UnderVerification (source={Source}, raison={Reason}) — fonds maintenus réservés",
                withdrawal.Id, source, Truncate(reason, 160));
            return PayoutSettlementOutcome.MarkedUnderVerification;
        }

        // ====================================================================
        // ===== Retraits de GAINS PLATEFORME (SchoolId == null) =====
        // Isolé du chemin école : AUCUN wallet, AUCUNE WalletTransaction. Le solde
        // plateforme P étant RECALCULÉ (dérivé), un retrait plateforme non-Failed
        // réduit P de lui-même ; le passage à Failed le restaure. On ne fait donc
        // que des transitions de statut, sérialisées par le verrou plateforme.
        // ====================================================================

        private async Task<PayoutSettlementOutcome> SettlePlatformCompletedAsync(
            IDbContextTransaction tx, Withdrawal withdrawal, string? disbursementId,
            long feesFcfa, long netReceivedFcfa, DateTime? completedAtUtc, string source, CancellationToken ct)
        {
            await _context.LockPlatformAsync(ct);
            await _context.Entry(withdrawal).ReloadAsync(ct);

            var completedAt = completedAtUtc?.ToUtcSafe() ?? DateTime.UtcNow;
            withdrawal.SenePayDisbursementId ??= disbursementId;

            switch (withdrawal.Status)
            {
                case WithdrawalStatus.Completed:
                    await tx.RollbackAsync(ct);
                    return PayoutSettlementOutcome.NoOp;

                case WithdrawalStatus.Initiated:
                case WithdrawalStatus.UnderVerification:
                    withdrawal.Status = WithdrawalStatus.Completed;
                    withdrawal.CompletedAt = completedAt;
                    withdrawal.FeesFcfa = feesFcfa;
                    withdrawal.NetReceivedFcfa = netReceivedFcfa;
                    withdrawal.LastCheckedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    _logger.LogInformation(
                        "[payout-settle] Retrait PLATEFORME {Id} → Completed (source={Source}, {Amount} FCFA)",
                        withdrawal.Id, source, withdrawal.AmountFcfa);
                    return PayoutSettlementOutcome.SettledCompleted;

                case WithdrawalStatus.Failed:
                    // Correcteur : `completed` authentique après un Failed. P étant
                    // dérivé, il suffit de repasser Completed (P re-soustrait ce
                    // retrait). AUCUN re-débit à faire (pas de wallet).
                    if (withdrawal.ReversedAt != null)
                    {
                        await tx.RollbackAsync(ct);
                        return PayoutSettlementOutcome.NoOp;
                    }
                    withdrawal.Status = WithdrawalStatus.Completed;
                    withdrawal.ReversedAt = DateTime.UtcNow;
                    withdrawal.CompletedAt = completedAt;
                    withdrawal.FailedAt = null;
                    withdrawal.FailureReason = null;
                    withdrawal.FeesFcfa = feesFcfa;
                    withdrawal.NetReceivedFcfa = netReceivedFcfa;
                    withdrawal.LastCheckedAt = DateTime.UtcNow;
                    AddAlertEntity(
                        PayoutAlertType.DoubleSpendCorrected, null, withdrawal.Id,
                        $"Retrait plateforme #{withdrawal.Id} : `completed` reçu après Failed → repassé Completed "
                        + $"(P re-soustrait {withdrawal.AmountFcfa} FCFA). Double compte évité.",
                        new { withdrawal.Id, withdrawal.AmountFcfa, disbursementId, source });
                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    _logger.LogCritical(
                        "[ALERT][payout] PLATEFORME double-spend corrected Withdrawal {Id} ({Amount} FCFA, source={Source})",
                        withdrawal.Id, withdrawal.AmountFcfa, source);
                    return PayoutSettlementOutcome.Corrected;

                default: // Cancelled
                    await tx.RollbackAsync(ct);
                    return PayoutSettlementOutcome.NoOp;
            }
        }

        private async Task<PayoutSettlementOutcome> SettlePlatformFailedAsync(
            IDbContextTransaction tx, Withdrawal withdrawal, string reason,
            string? disbursementId, DateTime? failedAtUtc, string source, CancellationToken ct)
        {
            await _context.LockPlatformAsync(ct);
            await _context.Entry(withdrawal).ReloadAsync(ct);
            withdrawal.SenePayDisbursementId ??= disbursementId;

            switch (withdrawal.Status)
            {
                case WithdrawalStatus.Initiated:
                case WithdrawalStatus.UnderVerification:
                    // Échec : P se restaure de lui-même (retrait Failed exclu du calcul).
                    withdrawal.Status = WithdrawalStatus.Failed;
                    withdrawal.FailedAt = failedAtUtc?.ToUtcSafe() ?? DateTime.UtcNow;
                    withdrawal.FailureReason = Truncate(reason, 480);
                    withdrawal.LastCheckedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    _logger.LogInformation(
                        "[payout-settle] Retrait PLATEFORME {Id} → Failed (source={Source}, raison={Reason})",
                        withdrawal.Id, source, Truncate(reason, 120));
                    return PayoutSettlementOutcome.Restituted;

                case WithdrawalStatus.Completed:
                    await tx.RollbackAsync(ct);
                    await RaiseAlertAsync(
                        PayoutAlertType.FailedAfterCompleted, null, withdrawal.Id,
                        $"Retrait plateforme #{withdrawal.Id} : `failed` reçu alors que déjà Completed. "
                        + "Aucune action (fonds déjà sortis). Vérification manuelle requise.",
                        new { withdrawal.Id, reason, disbursementId, source }, ct);
                    return PayoutSettlementOutcome.FailedAfterCompleted;

                default: // Failed (idempotent) / Cancelled
                    await tx.RollbackAsync(ct);
                    return PayoutSettlementOutcome.NoOp;
            }
        }

        private async Task<PayoutSettlementOutcome> MarkPlatformUnderVerificationAsync(
            IDbContextTransaction tx, Withdrawal withdrawal, string? disbursementId,
            string reason, string source, CancellationToken ct)
        {
            await _context.LockPlatformAsync(ct);
            await _context.Entry(withdrawal).ReloadAsync(ct);

            if (withdrawal.Status != WithdrawalStatus.Initiated)
            {
                await tx.RollbackAsync(ct);
                return PayoutSettlementOutcome.NoOp;
            }

            withdrawal.Status = WithdrawalStatus.UnderVerification;
            withdrawal.SenePayDisbursementId ??= disbursementId;
            withdrawal.VerificationStartedAt = DateTime.UtcNow;
            withdrawal.NextVerificationAt = DateTime.UtcNow.AddSeconds(30);
            withdrawal.VerificationAttempts = 0;
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            _logger.LogWarning(
                "[payout-settle] Retrait PLATEFORME {Id} → UnderVerification (source={Source}, raison={Reason})",
                withdrawal.Id, source, Truncate(reason, 160));
            return PayoutSettlementOutcome.MarkedUnderVerification;
        }

        public async Task RaiseAlertAsync(
            PayoutAlertType type, int? schoolId, int? withdrawalId,
            string message, object? details, CancellationToken ct = default)
        {
            AddAlertEntity(type, schoolId, withdrawalId, message, details);
            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // Une alerte non persistée ne doit jamais faire échouer l'appelant.
                _logger.LogError(ex, "[payout-settle] Échec persistance PayoutAlert type={Type}", type);
            }

            _logger.LogCritical(
                "[ALERT][payout] {Type} school={School} withdrawal={Withdrawal} — {Message}",
                type, schoolId, withdrawalId, message);
        }

        // --- Helpers ---

        /// <summary>Ajoute une PayoutAlert au contexte SANS SaveChanges (pour inclusion dans une tx en cours).</summary>
        private void AddAlertEntity(
            PayoutAlertType type, int? schoolId, int? withdrawalId, string message, object? details)
        {
            _context.PayoutAlerts.Add(new PayoutAlert
            {
                Type = type,
                SchoolId = schoolId,
                WithdrawalId = withdrawalId,
                Message = Truncate(message, 1000),
                Details = details != null ? JsonSerializer.Serialize(details) : null,
                Resolved = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s[..max];
        }
    }
}
