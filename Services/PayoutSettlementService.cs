using System.Text.Json;
using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
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
        private readonly Alerts.IOpsAlertService _alerts;
        private readonly ILogger<PayoutSettlementService> _logger;

        public PayoutSettlementService(
            AppDbContext context, INotificationService notif,
            Alerts.IOpsAlertService alerts, ILogger<PayoutSettlementService> logger)
        {
            _context = context;
            _notif = notif;
            _alerts = alerts;
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

        /// <summary>
        /// Prévient le BÉNÉFICIAIRE du transfert (SMS, + push s'il a un compte)
        /// que l'argent est arrivé sur son Mobile Money : « Vous avez recu un
        /// transfert de X FCFA par {opérateur} de la part de {daara}. »
        /// Best-effort, POST-commit, ne lève jamais. Appelée uniquement sur les
        /// transitions réelles vers Completed (la garde de statut sous verrou de
        /// SettleCompletedAsync rend un rejeu webhook+poll NoOp → jamais de
        /// double SMS). Jamais pour un retrait plateforme (chemin isolé en amont).
        ///
        /// Langue : si le numéro correspond à un compte Idara (même règle de
        /// rattachement que l'espace bénéficiaire : User.PhoneNumber E.164 ==
        /// Normalize(RecipientPhone)), la règle d'or §160 s'applique dans
        /// NotificationService (langue FRAÎCHE du récepteur, bilingue si jamais
        /// connecté). Sans compte → BILINGUE forcé : la langue d'un numéro
        /// inconnu ne se devine pas.
        ///
        /// PUBLIQUE EXPRÈS (motif §133) : le chemin complet SettleCompletedAsync
        /// passe par un verrou FOR UPDATE intraduisible en EF InMemory (§143) —
        /// cette méthode, sans verrou, reste vérifiable sur un banc.
        /// </summary>
        public async Task NotifyBeneficiaryAsync(Withdrawal withdrawal, CancellationToken ct = default)
        {
            try
            {
                var phone = SenegalPhone.Normalize(withdrawal.RecipientPhone);
                if (phone == null || withdrawal.SchoolId == null) return;

                var school = await _context.Schools.AsNoTracking()
                    .Where(s => s.Id == withdrawal.SchoolId.Value)
                    .Select(s => new { s.Name, s.NameAr })
                    .FirstOrDefaultAsync(ct);

                var user = await _context.Users.AsNoTracking()
                    .Where(u => !u.IsDeleted && u.PhoneNumber == phone)
                    .OrderBy(u => u.Id) // numéro = identité : ordre déterministe (§95)
                    .Select(u => new { u.Id, u.PreferredLanguage })
                    .FirstOrDefaultAsync(ct);

                var platform = await _context.GetPlatformSettingsAsync(ct);
                await _notif.SendSmsAsync(new NotificationSmsRequest(
                    UserId: user?.Id,
                    RawPhone: phone,
                    PreferredLanguage: user?.PreferredLanguage ?? "fr",
                    Message: NotificationTemplates.TransferReceived(
                        withdrawal.AmountFcfa, withdrawal.Operator, school?.Name, school?.NameAr),
                    Bilingual: user == null || platform.SmsBilingual,
                    TemplateCode: "TRANSFER_RECEIVED",
                    RelatedEntityId: withdrawal.Id,
                    PushRoute: "/my-transfers",
                    SchoolId: withdrawal.SchoolId,
                    TriggerSource: "settle:payout"), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[payout-settle] Échec notif bénéficiaire withdrawal {Id} — pas bloquant", withdrawal.Id);
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
            withdrawal.ProviderDisbursementId ??= disbursementId;

            switch (withdrawal.Status)
            {
                case WithdrawalStatus.Completed:
                    await tx.RollbackAsync(ct);
                    return PayoutSettlementOutcome.NoOp;

                case WithdrawalStatus.Initiated:
                case WithdrawalStatus.UnderVerification:
                {
                    // Clôture normale : la réservation (Available -= débit,
                    // faite à l'init) devient définitive. On vide le Pending.
                    //
                    // 🔑 C'EST LE FRAIS RÉEL DE WAVE QUI FAIT FOI, jamais notre
                    // estimation. À la réservation on a posé `ceil(reçu × taux)`
                    // pour pouvoir afficher un chiffre et bloquer les fonds ;
                    // Wave renvoie le frais effectif au règlement. L'écart — des
                    // francs d'arrondi — se solde par une écriture
                    // d'AJUSTEMENT, jamais par une réécriture de la réservation
                    // (§55 : une correction est une nouvelle écriture).
                    withdrawal.Status = WithdrawalStatus.Completed;
                    withdrawal.CompletedAt = completedAt;
                    // ⚠️ `feesFcfa = 0` veut dire « la source n'a rien dit », pas
                    // « c'est gratuit » : un décaissement Wave coûte 1 %, jamais
                    // zéro. On garde alors l'estimation plutôt que de restituer
                    // à l'école un frais qu'elle a bel et bien payé.
                    if (feesFcfa > 0) withdrawal.FeesFcfa = feesFcfa;
                    withdrawal.NetReceivedFcfa = netReceivedFcfa;
                    withdrawal.LastCheckedAt = DateTime.UtcNow;

                    var reserved = ReservedDebit(withdrawal);
                    var actualDebit = withdrawal.AmountFcfa + withdrawal.FeesFcfa;

                    wallet.PendingBalance -= reserved;
                    // « Total retiré » compte ce qui est ARRIVÉ au bénéficiaire :
                    // c'est le chiffre qu'un directeur rapproche de ses reçus.
                    wallet.TotalWithdrawnLifetime += withdrawal.AmountFcfa;

                    var delta = reserved - actualDebit;
                    if (delta != 0)
                    {
                        if (wallet.AvailableBalance + delta < 0)
                        {
                            // Frais réels au-dessus de l'estimation ET solde à
                            // sec. On ne creuse pas sous zéro (contrainte
                            // CK_SchoolWallets_NonNegative, qui casserait la
                            // transaction et laisserait le retrait en suspens) :
                            // l'écart, quelques francs, reste à la charge de la
                            // plateforme et se lit dans le journal.
                            _logger.LogWarning(
                                "[payout-settle] Withdrawal {Id} : complément de frais de {Delta} FCFA "
                                + "NON prélevé, solde insuffisant ({Available} FCFA) — écart à la charge de la plateforme",
                                withdrawal.Id, -delta, wallet.AvailableBalance);
                        }
                        else
                        {
                            wallet.AvailableBalance += delta;
                            withdrawal.WalletDebitedFcfa = actualDebit;
                            // ⚠️ La poche « Don » garde la part réservée : la
                            // rectifier de quelques francs n'a pas d'effet sur
                            // l'Available, seul montant qui engage l'argent.
                            _context.WalletTransactions.Add(new WalletTransaction
                            {
                                SchoolId = withdrawal.SchoolId!.Value,
                                Type = WalletTransactionType.Adjustment,
                                Source = WalletSource.Adjustment,
                                AmountFcfa = delta,
                                BalanceAfter = wallet.AvailableBalance,
                                RelatedEntity = WalletRelatedEntity.Withdrawal,
                                RelatedId = withdrawal.Id,
                                Note = delta > 0
                                    ? $"Ajustement retrait #{withdrawal.Id} : frais réels {withdrawal.FeesFcfa} FCFA, {delta} FCFA restitués"
                                    : $"Ajustement retrait #{withdrawal.Id} : frais réels {withdrawal.FeesFcfa} FCFA, {-delta} FCFA complémentaires",
                                OccurredAt = DateTime.UtcNow
                            });
                        }
                    }
                    wallet.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    _logger.LogInformation(
                        "[payout-settle] Withdrawal {Id} → Completed (source={Source}, {Amount} FCFA)",
                        withdrawal.Id, source, withdrawal.AmountFcfa);
                    await NotifyAdminsAsync(withdrawal.SchoolId!.Value,
                        NotificationTemplates.WithdrawalDone(withdrawal.AmountFcfa),
                        "WITHDRAWAL_DONE", withdrawal.Id);
                    await NotifyBeneficiaryAsync(withdrawal, ct);
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
                    // 🔑 Le re-débit porte sur le DÉBIT (reçu + frais), pas sur
                    // le seul montant reçu : c'est bien cette somme-là qui est
                    // sortie de la réserve.
                    if (feesFcfa > 0) withdrawal.FeesFcfa = feesFcfa;
                    var redebit = withdrawal.AmountFcfa + withdrawal.FeesFcfa;
                    if (wallet.AvailableBalance < redebit)
                    {
                        await tx.RollbackAsync(ct);
                        await RaiseAlertAsync(
                            PayoutAlertType.CorrectionImpossible,
                            withdrawal.SchoolId, withdrawal.Id,
                            $"Décaissement #{withdrawal.Id} confirmé `completed` après restitution, " +
                            $"mais solde Available ({wallet.AvailableBalance}) insuffisant pour re-débiter " +
                            $"{redebit} FCFA. Perte/dette à régulariser manuellement.",
                            new { withdrawal.SchoolId, withdrawal.Id, withdrawal.AmountFcfa,
                                  redebit, wallet.AvailableBalance, disbursementId, source },
                            ct);
                        return PayoutSettlementOutcome.CorrectionImpossible;
                    }

                    withdrawal.Status = WithdrawalStatus.Completed;
                    withdrawal.ReversedAt = DateTime.UtcNow;
                    withdrawal.CompletedAt = completedAt;
                    withdrawal.FailedAt = null;
                    withdrawal.FailureReason = null;
                    withdrawal.NetReceivedFcfa = netReceivedFcfa;
                    withdrawal.WalletDebitedFcfa = redebit;
                    withdrawal.LastCheckedAt = DateTime.UtcNow;

                    // Re-débit définitif de l'Available + transaction Adjustment
                    // signée négative (préserve Σ tx == Available).
                    wallet.AvailableBalance -= redebit;
                    wallet.TotalWithdrawnLifetime += withdrawal.AmountFcfa;
                    // Re-applique la déduction de la poche « Don » qu'avait annulée
                    // la restitution. Clamp à 0 par prudence : entre la restitution
                    // et ce correcteur (rare), d'autres débits ont pu réduire la
                    // poche don ; l'Available (money-critical) reste exact, seul le
                    // détail de la poche est approché dans ce cas extrême.
                    wallet.DonationBalanceFcfa = Math.Max(
                        0, wallet.DonationBalanceFcfa - withdrawal.DonationAmountFcfa);
                    wallet.UpdatedAt = DateTime.UtcNow;

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        SchoolId = withdrawal.SchoolId!.Value,
                        Type = WalletTransactionType.Adjustment,
                        Source = WalletSource.Adjustment,
                        AmountFcfa = -redebit,
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
                        $"reçu après un Failed. Re-débit de {redebit} FCFA. Double dépense évitée.",
                        new { withdrawal.SchoolId, withdrawal.Id, withdrawal.AmountFcfa, disbursementId, source });

                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);

                    _logger.LogCritical(
                        "[ALERT][payout] DOUBLE-SPEND CORRECTED Withdrawal {Id} (School {SchoolId}, {Amount} FCFA) — restitution annulée par re-débit (source={Source})",
                        withdrawal.Id, withdrawal.SchoolId, withdrawal.AmountFcfa, source);
                    await NotifyAdminsAsync(withdrawal.SchoolId!.Value,
                        NotificationTemplates.WithdrawalDone(withdrawal.AmountFcfa),
                        "WITHDRAWAL_DONE", withdrawal.Id);
                    // L'argent EST sorti (completed authentique) : le bénéficiaire
                    // l'a reçu, il est prévenu comme sur le chemin nominal.
                    await NotifyBeneficiaryAsync(withdrawal, ct);
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
            withdrawal.ProviderDisbursementId ??= disbursementId;

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

                    // On restitue ce qui a été RÉSERVÉ — frais estimés
                    // compris. Aucun franc n'est sorti : l'école doit retrouver
                    // son solde au centime près.
                    var released = ReservedDebit(withdrawal);
                    wallet.PendingBalance -= released;
                    wallet.AvailableBalance += released;
                    // Restitution symétrique de la poche « Don » : on remet
                    // exactement la part qui avait été prélevée dessus à la réservation.
                    wallet.DonationBalanceFcfa += withdrawal.DonationAmountFcfa;
                    wallet.UpdatedAt = DateTime.UtcNow;

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        SchoolId = withdrawal.SchoolId!.Value,
                        Type = WalletTransactionType.Release,
                        Source = WalletSource.Withdrawal,
                        AmountFcfa = released, // release = signé positif, frais compris
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
                    // L'ECOLE etait prevenue, PAS TOI. Un retrait qui echoue
                    // parce que la reserve du prestataire est a sec fait echouer
                    // TOUS les retraits de TOUTES les ecoles, et jusqu'ici on ne
                    // l'apprenait que par l'appel d'un directeur (§111).
                    await AlertWithdrawalFailedAsync(withdrawal, reason, source, ct);
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
            withdrawal.ProviderDisbursementId ??= disbursementId;
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

        /// <summary>
        /// Ce qui a été RÉSERVÉ sur le portefeuille à l'initiation du retrait :
        /// le montant reçu par le bénéficiaire <b>plus</b> les frais estimés.
        /// </summary>
        /// <remarks>
        /// Le repli sur <see cref="Withdrawal.AmountFcfa"/> est une ceinture :
        /// la migration a reposé <c>WalletDebitedFcfa = AmountFcfa</c> sur tous
        /// les retraits antérieurs au 2026-09-17, où le débit valait bien le
        /// montant reçu (la sortie était provisionnée dès l'encaissement). Aucun
        /// retrait ne devrait donc porter zéro ici — mais restituer zéro à une
        /// école serait une perte sèche, alors qu'un repli ne coûte rien.
        /// </remarks>
        private static long ReservedDebit(Withdrawal w) =>
            w.WalletDebitedFcfa > 0 ? w.WalletDebitedFcfa : w.AmountFcfa;

        private async Task<PayoutSettlementOutcome> SettlePlatformCompletedAsync(
            IDbContextTransaction tx, Withdrawal withdrawal, string? disbursementId,
            long feesFcfa, long netReceivedFcfa, DateTime? completedAtUtc, string source, CancellationToken ct)
        {
            await _context.LockPlatformAsync(ct);
            await _context.Entry(withdrawal).ReloadAsync(ct);

            var completedAt = completedAtUtc?.ToUtcSafe() ?? DateTime.UtcNow;
            withdrawal.ProviderDisbursementId ??= disbursementId;

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
            withdrawal.ProviderDisbursementId ??= disbursementId;

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
                    await AlertWithdrawalFailedAsync(withdrawal, reason, source, ct);
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
            withdrawal.ProviderDisbursementId ??= disbursementId;
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

            // Ces alertes s'ecrivaient en base et dans le journal depuis des
            // MOIS sans que personne ne les lise : ni ecran, ni e-mail. Une
            // double depense corrigee ou une reconciliation rompue merite mieux
            // qu'une ligne de journalctl que rien ne fait remonter.
            var schoolName = schoolId == null ? null : await _context.Schools.AsNoTracking()
                .Where(x => x.Id == schoolId.Value).Select(x => x.Name)
                .FirstOrDefaultAsync(ct);

            _alerts.Queue(new Alerts.OpsAlertRequest(
                type == PayoutAlertType.StuckUnderVerification
                    ? Enums.OpsAlertKind.WithdrawalStuck
                    : Enums.OpsAlertKind.PayoutAnomaly,
                GroupingKey: $"payout-{type}-{withdrawalId?.ToString() ?? "global"}",
                Subject: $"Decaissement — {type}",
                Facts: new[]
                {
                    new Alerts.AlertFact("Ecole", schoolName ?? (schoolId == null
                        ? "plateforme" : $"#{schoolId}")),
                    new Alerts.AlertFact("Retrait", withdrawalId == null
                        ? "-" : IdaraReference.Withdrawal(withdrawalId.Value)),
                    new Alerts.AlertFact("Nature", type.ToString()),
                    new Alerts.AlertFact("Detail", Truncate(message, 900)),
                },
                Advice: type switch
                {
                    PayoutAlertType.StuckUnderVerification =>
                        "Interroger le prestataire sur l'etat reel de ce decaissement. Les fonds "
                        + "restent reserves cote ecole tant que ce n'est pas tranche : ne rien "
                        + "restituer a la main sans preuve.",
                    PayoutAlertType.CorrectionImpossible =>
                        "Le solde de l'ecole ne permet pas de re-debiter : il y a une dette a "
                        + "regulariser manuellement. A traiter en priorite, l'ecart grandit a "
                        + "chaque mouvement.",
                    PayoutAlertType.ReconciliationMismatch =>
                        "L'identite R = D + P est rompue. Comparer la reserve du prestataire aux "
                        + "soldes des ecoles avant tout nouveau retrait de gains.",
                    _ => "Verification manuelle requise : l'argent et les ecritures ne disent pas "
                       + "la meme chose.",
                },
                SchoolId: schoolId,
                RelatedId: withdrawalId));
        }

        /// <summary>
        /// Prévient par e-mail qu'un retrait a échoué, avec la cause CLASSÉE.
        ///
        /// <para>Le classement décide de tout : une réserve de décaissement à sec
        /// est urgente et n'appelle aucune action de l'école, un numéro invalide
        /// se règle par un appel. Le regroupement se fait par CAUSE et non par
        /// retrait : quand la réserve est vide, dix écoles échouent en quelques
        /// minutes et dix e-mails identiques feraient perdre le onzième.</para>
        /// </summary>
        private async Task AlertWithdrawalFailedAsync(
            Withdrawal withdrawal, string reason, string source, CancellationToken ct)
        {
            try
            {
                var cause = PayoutFailureClassifier.Classify(reason);
                var schoolName = withdrawal.SchoolId == null
                    ? "Plateforme (gains)"
                    : await _context.Schools.AsNoTracking()
                        .Where(x => x.Id == withdrawal.SchoolId.Value).Select(x => x.Name)
                        .FirstOrDefaultAsync(ct) ?? $"#{withdrawal.SchoolId}";

                _alerts.Queue(new Alerts.OpsAlertRequest(
                    cause == PayoutFailureCause.ProviderOutage
                        ? Enums.OpsAlertKind.WithdrawalProviderOutage
                        : Enums.OpsAlertKind.WithdrawalFailed,
                    // Regroupe par CAUSE quand le prestataire est en cause (un
                    // seul e-mail pour toutes les ecoles touchees), par RETRAIT
                    // sinon (chaque cas est particulier et se traite seul).
                    GroupingKey: cause == PayoutFailureCause.ProviderOutage
                        ? "withdrawal-provider-outage"
                        : $"withdrawal-failed-{withdrawal.Id}",
                    Subject: cause == PayoutFailureCause.ProviderOutage
                        ? "Retrait impossible — le prestataire ne peut pas decaisser"
                        : $"Retrait echoue — {schoolName}",
                    Facts: new[]
                    {
                        new Alerts.AlertFact("Ecole", schoolName ?? "-"),
                        new Alerts.AlertFact("Montant",
                            withdrawal.AmountFcfa.ToString("N0",
                                System.Globalization.CultureInfo.InvariantCulture)
                                .Replace(",", " ") + " FCFA"),
                        new Alerts.AlertFact("Beneficiaire",
                            withdrawal.RecipientName + " · "
                            + SenegalPhone.ToDisplay(withdrawal.RecipientPhone, "-")),
                        new Alerts.AlertFact("Operateur", withdrawal.Operator.ToString()),
                        new Alerts.AlertFact("Reference", IdaraReference.Withdrawal(withdrawal.Id)),
                        new Alerts.AlertFact("Cause", PayoutFailureClassifier.Label(cause)),
                        new Alerts.AlertFact("Motif brut", Truncate(reason, 400)),
                        new Alerts.AlertFact("Detecte par", source),
                    },
                    Advice: PayoutFailureClassifier.Advice(cause),
                    SchoolId: withdrawal.SchoolId,
                    RelatedId: withdrawal.Id));
            }
            catch (Exception ex)
            {
                // Une alerte ratee ne doit jamais aggraver un retrait deja en
                // echec : le retrait est regle, sa restitution est committee.
                _logger.LogError(ex,
                    "[payout-settle] Alerte e-mail impossible pour le retrait {Id} — pas bloquant",
                    withdrawal.Id);
            }
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
