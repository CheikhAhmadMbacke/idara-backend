using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <inheritdoc cref="IPayinSettlementService"/>
    public class PayinSettlementService : IPayinSettlementService
    {
        private readonly AppDbContext _context;
        private readonly IReceiptPdfService _receiptPdf;
        private readonly INotificationService _notif;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PayinSettlementService> _logger;

        public PayinSettlementService(
            AppDbContext context,
            IReceiptPdfService receiptPdf,
            INotificationService notif,
            IServiceScopeFactory scopeFactory,
            ILogger<PayinSettlementService> logger)
        {
            _context = context;
            _receiptPdf = receiptPdf;
            _notif = notif;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<PayinSettlementResult> SettleAsync(
            int paymentId,
            PaymentStatus terminalStatus,
            long feesFcfa,
            long netCreditedFcfa,
            string? senePayTransactionId,
            DateTime? eventTime,
            string? failureReason,
            string source,
            CancellationToken ct = default)
        {
            if (terminalStatus == PaymentStatus.Pending)
            {
                throw new ArgumentException(
                    "SettleAsync ne traite que des statuts terminaux (pas Pending).", nameof(terminalStatus));
            }

            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var payment = await _context.Payments
                    .Include(p => p.InvoiceAllocations)
                    .FirstOrDefaultAsync(p => p.Id == paymentId, ct)
                ?? throw new InvalidOperationException(
                    $"Payment.Id={paymentId} introuvable pour règlement (source={source}).");

            // Verrou pessimiste wallet AVANT de lire le statut : sérialise tout
            // règlement concurrent sur la MÊME école (webhook vs poll vs un autre
            // payment de l'école). Puis on RE-LIT le statut du Payment sous le
            // verrou — c'est ce qui garantit qu'un seul règlement transite (pas
            // de double crédit). Wallet null = école sans fondations (ne devrait
            // jamais arriver) ; on tolère pour le chemin échec (pas de crédit).
            var wallet = await _context.LockWalletAsync(payment.SchoolId, ct);
            await _context.Entry(payment).ReloadAsync(ct);

            // Idempotence : déjà tranché par un webhook/poll concurrent → no-op.
            if (payment.Status != PaymentStatus.Pending)
            {
                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "[payin-settle] Payment.Id={Id} déjà en {Status} (source={Source}) — règlement ignoré",
                    payment.Id, payment.Status, source);
                return new PayinSettlementResult(PayinSettlementOutcome.AlreadySettled, payment.Status);
            }

            payment.FeesFcfa = feesFcfa;
            payment.NetCreditedFcfa = netCreditedFcfa;
            if (!string.IsNullOrWhiteSpace(senePayTransactionId))
                payment.SenePayTransactionId = senePayTransactionId;
            payment.Status = terminalStatus;

            switch (terminalStatus)
            {
                case PaymentStatus.Completed:
                    payment.PaidAt = eventTime?.ToUtcSafe() ?? DateTime.UtcNow;
                    CreditWalletAndInvoice(payment, wallet);
                    break;

                case PaymentStatus.Failed:
                case PaymentStatus.Cancelled:
                case PaymentStatus.Expired:
                    payment.FailedAt = eventTime?.ToUtcSafe() ?? DateTime.UtcNow;
                    payment.FailureReason = failureReason;
                    // Rien à débiter — le wallet n'a jamais été crédité.
                    break;

                default:
                    throw new InvalidOperationException($"Statut terminal inattendu : {terminalStatus}");
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "[payin-settle] Payment.Id={Id} → {Status} (source={Source}, fees={Fees}, net={Net})",
                payment.Id, terminalStatus, source, feesFcfa, netCreditedFcfa);

            return new PayinSettlementResult(PayinSettlementOutcome.Transitioned, terminalStatus);
        }

        /// <summary>
        /// Crédit wallet école + invoice. Le <paramref name="wallet"/> est DÉJÀ
        /// verrouillé (FOR UPDATE) par l'appelant — on ne re-verrouille pas.
        /// Logique identique au webhook historique (§82 wallet / §106 invoice).
        /// </summary>
        private void CreditWalletAndInvoice(Payment payment, SchoolWallet? wallet)
        {
            var netAmount = payment.NetCreditedFcfa;

            // FeesPayer=Parent : créditer le montant CIBLE (l'école reçoit ce
            // qu'elle a fixé ; la majoration +8% couvre les frais). FeesPayer=
            // School : créditer le NET (l'école absorbe les frais). Fallback net
            // pour les anciens Payments sans TargetAmountFcfa. (Cf. §82.)
            var amountToCredit = payment.FeesPayer == FeesPayer.Parent && payment.TargetAmountFcfa > 0
                ? payment.TargetAmountFcfa
                : netAmount;

            if (amountToCredit <= 0)
            {
                _logger.LogWarning(
                    "[payin-settle] montant à créditer={Amount} <= 0 pour Payment.Id={Id} (net={Net}, target={Target}, feesPayer={FeesPayer}) — pas de crédit wallet",
                    amountToCredit, payment.Id, netAmount, payment.TargetAmountFcfa, payment.FeesPayer);
                return;
            }

            if (wallet == null)
            {
                // Completed sans wallet = anomalie (toute école a un wallet via
                // EnsurePaymentFoundations). On lève → la transaction rollback,
                // le Payment reste Pending, le poll/webhook réessaiera.
                throw new InvalidOperationException(
                    $"SchoolWallet manquant pour SchoolId={payment.SchoolId} (Payment.Id={payment.Id})");
            }

            wallet.AvailableBalance += amountToCredit;
            wallet.TotalCreditedLifetime += amountToCredit;

            // Poche « Don » : un don crédite Available ET DonationBalance (l'école
            // pourra ensuite choisir de retirer sur cette poche). Invariant préservé :
            // DonationBalance <= Available. Un paiement / topup ne touche pas la poche.
            if (payment.Purpose == PaymentPurpose.Donation)
                wallet.DonationBalanceFcfa += amountToCredit;

            wallet.UpdatedAt = DateTime.UtcNow;

            // Source dénormalisée (badge affiché dans l'historique wallet — un don
            // se distingue d'un paiement parent). Aucun impact financier.
            var (source, note) = payment.Purpose switch
            {
                PaymentPurpose.Donation => (WalletSource.Donation, $"Don {payment.SenePayTransactionId}"),
                PaymentPurpose.WalletTopup => (WalletSource.Topup, $"Recharge {payment.SenePayTransactionId}"),
                _ => (WalletSource.Payment, $"Payment {payment.SenePayTransactionId}")
            };

            _context.WalletTransactions.Add(new WalletTransaction
            {
                SchoolId = payment.SchoolId,
                Type = WalletTransactionType.Credit,
                Source = source,
                AmountFcfa = amountToCredit,
                BalanceAfter = wallet.AvailableBalance,
                RelatedEntity = payment.Purpose == PaymentPurpose.WalletTopup
                    ? WalletRelatedEntity.Topup
                    : WalletRelatedEntity.Payment,
                RelatedId = payment.Id,
                Note = note,
                OccurredAt = DateTime.UtcNow
            });

            // Invoice(s) : TOUJOURS créditée(s) du montant CIBLE (dette de la
            // famille), dans les 2 modes FeesPayer. NE JAMAIS créditer du net
            // (sinon facture ZOMBIE jamais soldée en mode School). Cf. §106.
            if (payment.InvoiceAllocations.Count > 0)
            {
                // Paiement CONSOLIDÉ : chaque facture reçoit son allocation figée
                // à l'initiation (= son reste dû d'alors). all-or-nothing : ce
                // crédit n'arrive qu'à la complétion du paiement unique.
                var allocIds = payment.InvoiceAllocations.Select(a => a.InvoiceId).ToList();
                var invoices = _context.Invoices.Where(i => allocIds.Contains(i.Id)).ToList();
                foreach (var alloc in payment.InvoiceAllocations)
                {
                    var invoice = invoices.FirstOrDefault(i => i.Id == alloc.InvoiceId);
                    if (invoice == null) continue;
                    invoice.AmountPaidFcfa += alloc.AmountFcfa;
                    invoice.UpdatedAt = DateTime.UtcNow;
                    if (invoice.AmountPaidFcfa >= invoice.AmountDueFcfa)
                        invoice.Status = InvoiceStatus.Paid;
                }
            }
            else if (payment.InvoiceId is int invoiceId)
            {
                var invoice = _context.Invoices.FirstOrDefault(i => i.Id == invoiceId);
                if (invoice != null)
                {
                    var creditedToInvoice = payment.TargetAmountFcfa > 0
                        ? payment.TargetAmountFcfa
                        : netAmount;
                    invoice.AmountPaidFcfa += creditedToInvoice;
                    invoice.UpdatedAt = DateTime.UtcNow;
                    if (invoice.AmountPaidFcfa >= invoice.AmountDueFcfa)
                        invoice.Status = InvoiceStatus.Paid;
                }
            }
        }

        public async Task RunPostCompletionEffectsAsync(int paymentId, string source, CancellationToken ct = default)
        {
            // Re-lecture fraîche du Payment complété.
            var payment = await _context.Payments
                .Include(p => p.Student)
                .Include(p => p.Donor)
                .Include(p => p.Guardian)
                .Include(p => p.InvoiceAllocations).ThenInclude(a => a.Invoice).ThenInclude(i => i.Student)
                .Include(p => p.StudentAllocations).ThenInclude(a => a.Student)
                .FirstOrDefaultAsync(p => p.Id == paymentId, ct);
            if (payment == null || payment.Status != PaymentStatus.Completed)
                return;

            var school0 = await _context.Schools.FirstOrDefaultAsync(s => s.Id == payment.SchoolId, ct);

            // Paiement CONSOLIDÉ (« paiement global » d'un parent) : lignes reçu +
            // libellé « vos enfants » / nom de l'unique enfant réglé.
            // Ventilé par facture (montant fixe) OU par enfant (montant libre via lien).
            var consolidatedLines = ReceiptConsolidatedLine.LinesFor(payment);
            var isConsolidated = consolidatedLines != null;
            var singleChildName = isConsolidated && consolidatedLines!.Count == 1
                ? consolidatedLines[0].StudentName
                : null;
            // Libellé côté PARENT (« vos enfants ») et côté ÉCOLE (« plusieurs
            // élèves ») quand le paiement règle >1 enfant.
            var consolidatedChildLabel = isConsolidated ? (singleChildName ?? "vos enfants") : null;
            var consolidatedSchoolLabel = isConsolidated ? (singleChildName ?? "plusieurs élèves") : null;

            // -------- Reçu PDF (best-effort) --------
            try
            {
                var invoice = payment.InvoiceId.HasValue
                    ? await _context.Invoices.FirstOrDefaultAsync(x => x.Id == payment.InvoiceId.Value, ct)
                    : null;
                if (school0 != null)
                {
                    var pdfPath = await _receiptPdf.GenerateAsync(
                        payment, school0, payment.Student, invoice, payment.Donor, consolidatedLines, payment.Guardian);
                    await _context.Payments
                        .Where(p => p.Id == payment.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.ReceiptPdfPath, pdfPath), ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[payin-settle] Échec génération reçu PDF Payment.Id={Id} (source={Source}) — pas bloquant",
                    payment.Id, source);
            }

            var shownAmount = payment.TargetAmountFcfa > 0 ? payment.TargetAmountFcfa : payment.AmountFcfa;
            var isTopup = payment.Purpose == PaymentPurpose.WalletTopup;

            // ==================== DON (push uniquement, pas de SMS) ====================
            if (payment.Purpose == PaymentPurpose.Donation)
            {
                var donorName = string.IsNullOrWhiteSpace(payment.Donor?.FullName)
                    ? "un donateur"
                    : payment.Donor!.FullName!.Trim();
                var schoolName = school0?.Name ?? "un daara";

                // Push à l'ÉCOLE (admin + personnel) : « don reçu de X ».
                try
                {
                    var schoolUsers = await _context.Users
                        .Where(u => u.SchoolId == payment.SchoolId && !u.IsDeleted
                                    && (u.Role == UserRoles.SchoolAdmin || u.Role == UserRoles.SchoolStaff))
                        .Select(u => new { u.Id, u.PreferredLanguage })
                        .ToListAsync(ct);
                    var msg = NotificationTemplates.DonationReceivedSchool(donorName, shownAmount);
                    foreach (var su in schoolUsers)
                        await _notif.SendPushOnlyAsync(new PushOnlyRequest(
                            UserId: su.Id,
                            PreferredLanguage: su.PreferredLanguage ?? "fr",
                            Message: msg,
                            TemplateCode: "DONATION_RECEIVED_SCHOOL",
                            RelatedEntityId: payment.Id,
                            PushRoute: "/payments/overview"));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[payin-settle] Échec push don école Payment.Id={Id} — pas bloquant", payment.Id);
                }

                // Push au DONATEUR : remerciement + reçu dispo.
                if (payment.DonorId.HasValue)
                {
                    try
                    {
                        await _notif.SendPushOnlyAsync(new PushOnlyRequest(
                            UserId: payment.DonorId.Value,
                            PreferredLanguage: payment.Donor?.PreferredLanguage ?? "fr",
                            Message: NotificationTemplates.DonationThanks(shownAmount, schoolName),
                            TemplateCode: "DONATION_THANKS",
                            RelatedEntityId: payment.Id,
                            PushRoute: "/donor/donations"));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[payin-settle] Échec push remerciement donateur Payment.Id={Id} — pas bloquant", payment.Id);
                    }
                }

                // Un don crédite le wallet → peut débloquer un abonnement en impayé.
                await RetrySubscriptionAsync(payment.SchoolId, payment.Id);
                return;
            }

            // -------- SMS « paiement reçu » au responsable (best-effort) --------
            if (payment.GuardianId.HasValue)
            {
                try
                {
                    var guardian = await _context.Users.FirstOrDefaultAsync(
                        u => u.Id == payment.GuardianId.Value && !u.IsDeleted, ct);
                    if (guardian?.PhoneNumber != null)
                    {
                        var eleve = consolidatedChildLabel
                            ?? (payment.Student != null
                                ? $"{payment.Student.FirstName} {payment.Student.LastName}".Trim()
                                : "votre enfant");
                        var platform = await _context.GetPlatformSettingsAsync(ct);
                        var msg = NotificationTemplates.PaymentReceived(eleve, shownAmount);
                        await _notif.SendSmsAsync(new NotificationSmsRequest(
                            UserId: guardian.Id,
                            RawPhone: guardian.PhoneNumber,
                            PreferredLanguage: guardian.PreferredLanguage ?? "fr",
                            Message: msg,
                            Bilingual: platform.SmsBilingual,
                            TemplateCode: "PAYMENT_RECEIVED",
                            RelatedEntityId: payment.Id,
                            PushRoute: "/guardian/invoices"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[payin-settle] Échec SMS paiement reçu Payment.Id={Id} — pas bloquant", payment.Id);
                }
            }

            // -------- Push « paiement reçu » à l'ÉCOLE (admin + personnel) --------
            if (payment.SchoolId > 0)
            {
                try
                {
                    var schoolUsers = await _context.Users
                        .Where(u => u.SchoolId == payment.SchoolId
                                    && !u.IsDeleted
                                    && (u.Role == UserRoles.SchoolAdmin || u.Role == UserRoles.SchoolStaff))
                        .Select(u => new { u.Id, u.PreferredLanguage })
                        .ToListAsync(ct);
                    if (schoolUsers.Count > 0)
                    {
                        var msg = isTopup
                            ? NotificationTemplates.WalletTopupReceived(shownAmount)
                            : NotificationTemplates.PaymentReceivedSchool(
                                consolidatedSchoolLabel
                                    ?? (payment.Student != null
                                        ? $"{payment.Student.FirstName} {payment.Student.LastName}".Trim()
                                        : "un eleve"),
                                shownAmount);
                        foreach (var su in schoolUsers)
                        {
                            await _notif.SendPushOnlyAsync(new PushOnlyRequest(
                                UserId: su.Id,
                                PreferredLanguage: su.PreferredLanguage ?? "fr",
                                Message: msg,
                                TemplateCode: "SCHOOL_PAYMENT_RECEIVED",
                                RelatedEntityId: payment.Id,
                                PushRoute: "/payments/overview"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[payin-settle] Échec push école Payment.Id={Id} — pas bloquant", payment.Id);
                }
            }

            // -------- Retry abonnement plateforme (scope DI séparé, best-effort) --------
            await RetrySubscriptionAsync(payment.SchoolId, payment.Id);
        }

        /// <summary>
        /// Re-tente le prélèvement de l'abonnement de l'école après un crédit wallet
        /// (paiement, topup OU don) — un crédit peut débloquer une école en impayé.
        /// Scope DI séparé (DbContext frais, ne pollue pas le change tracker) et
        /// CancellationToken.None (pas coupé si SenePay abandonne la connexion).
        /// Best-effort : ne lève jamais.
        /// </summary>
        private async Task RetrySubscriptionAsync(int schoolId, int paymentId)
        {
            if (schoolId <= 0) return;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var billing = scope.ServiceProvider.GetRequiredService<ISubscriptionBillingService>();
                var outcome = await billing.RetryForSchoolAsync(schoolId, DateTime.UtcNow, CancellationToken.None);
                if (outcome == BillingOutcome.Paid)
                    _logger.LogInformation(
                        "[payin-settle] Abonnement École {SchoolId} débloqué après crédit (Payment {Id})",
                        schoolId, paymentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[payin-settle] Retry abonnement École {SchoolId} échoué — pas bloquant", schoolId);
            }
        }

        // Le libellé de mois des lignes de reçu vit désormais dans
        // ReceiptConsolidatedLine.For (source unique des trois sites).
    }
}
