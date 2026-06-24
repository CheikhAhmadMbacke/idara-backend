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

            var payment = await _context.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct)
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
            wallet.UpdatedAt = DateTime.UtcNow;

            _context.WalletTransactions.Add(new WalletTransaction
            {
                SchoolId = payment.SchoolId,
                Type = WalletTransactionType.Credit,
                AmountFcfa = amountToCredit,
                BalanceAfter = wallet.AvailableBalance,
                RelatedEntity = WalletRelatedEntity.Payment,
                RelatedId = payment.Id,
                Note = $"Payment {payment.SenePayTransactionId}",
                OccurredAt = DateTime.UtcNow
            });

            // Invoice : TOUJOURS créditée du montant CIBLE (dette de la famille),
            // dans les 2 modes FeesPayer. NE JAMAIS créditer du net (sinon facture
            // ZOMBIE jamais soldée en mode School). Cf. §106.
            if (payment.InvoiceId is int invoiceId)
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
                .FirstOrDefaultAsync(p => p.Id == paymentId, ct);
            if (payment == null || payment.Status != PaymentStatus.Completed)
                return;

            // -------- Reçu PDF (best-effort) --------
            try
            {
                var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == payment.SchoolId, ct);
                var invoice = payment.InvoiceId.HasValue
                    ? await _context.Invoices.FirstOrDefaultAsync(x => x.Id == payment.InvoiceId.Value, ct)
                    : null;
                if (school != null)
                {
                    var pdfPath = await _receiptPdf.GenerateAsync(payment, school, payment.Student, invoice);
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
            var isTopup = payment.StudentId == null && payment.GuardianId == null;

            // -------- SMS « paiement reçu » au responsable (best-effort) --------
            if (payment.GuardianId.HasValue)
            {
                try
                {
                    var guardian = await _context.Users.FirstOrDefaultAsync(
                        u => u.Id == payment.GuardianId.Value && !u.IsDeleted, ct);
                    if (guardian?.PhoneNumber != null)
                    {
                        var eleve = payment.Student != null
                            ? $"{payment.Student.FirstName} {payment.Student.LastName}".Trim()
                            : "votre enfant";
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
                                payment.Student != null
                                    ? $"{payment.Student.FirstName} {payment.Student.LastName}".Trim()
                                    : "un eleve",
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
            if (payment.SchoolId > 0)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var billing = scope.ServiceProvider.GetRequiredService<ISubscriptionBillingService>();
                    var outcome = await billing.RetryForSchoolAsync(
                        payment.SchoolId, DateTime.UtcNow, CancellationToken.None);
                    if (outcome == BillingOutcome.Paid)
                    {
                        _logger.LogInformation(
                            "[payin-settle] Abonnement École {SchoolId} débloqué après crédit (Payment {Id})",
                            payment.SchoolId, payment.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[payin-settle] Retry abonnement École {SchoolId} échoué — pas bloquant", payment.SchoolId);
                }
            }
        }
    }
}
