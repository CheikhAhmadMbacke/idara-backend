using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>Ce qu'un remboursement changerait, montré AVANT de confirmer.</summary>
    public record RefundPreview(
        int PaymentId,
        int SchoolId,
        string SchoolName,
        string Purpose,
        long AmountFcfa,
        DateTime? PaidAt,
        string? PayerPhone,
        long WalletDebitFcfa,
        long WalletAvailableFcfa,
        IReadOnlyList<string> Effects,
        string? BlockedReason);

    public record RefundResult(bool Ok, string? Error, RefundPreview? Preview);

    /// <summary>
    /// 💸 Enregistre un remboursement fait depuis l'application Wave (2026-10-21,
    /// §308). Wave ne rembourse pas par l'API : l'argent est déjà reparti quand
    /// on arrive ici. Ce service ne fait que l'ÉCRITURE COMPTABLE, et la fait
    /// complète :
    /// <list type="bullet">
    /// <item>le paiement reste <c>Completed</c> (il a eu lieu) et porte le
    /// remboursement — append-only, §55 ;</item>
    /// <item>si l'argent avait été versé à l'école, il est repris sur son solde
    /// (D baisse d'autant), sous le verrou du wallet (§69) ;</item>
    /// <item>les factures qu'il réglait sont recalculées SANS lui ;</item>
    /// <item>la plateforme absorbe l'écart (frais Wave perdus) : c'est
    /// <c>PlatformFinanceService</c> qui le lit, R = D + P reste exact.</item>
    /// </list>
    /// <para>Un remboursement qui offrirait un mois d'abonnement (rembourser le
    /// SEUL paiement d'une facture soldée) est refusé : « je n'offre rien ».</para>
    /// </summary>
    public class PaymentRefundService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<PaymentRefundService> _logger;

        public PaymentRefundService(AppDbContext db, ILogger<PaymentRefundService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>Accepte « 470 » comme « idara-paiement-470 ».</summary>
        public static int? ParsePaymentRef(string? input)
        {
            var s = input?.Trim();
            if (string.IsNullOrEmpty(s)) return null;
            if (int.TryParse(s, out var id)) return id;
            return WaveReference.TryParsePayment(s, out var pid) ? pid : null;
        }

        public async Task<RefundPreview?> PreviewAsync(int paymentId, CancellationToken ct)
        {
            var payment = await _db.Payments.AsNoTracking()
                .Include(p => p.InvoiceAllocations)
                .FirstOrDefaultAsync(p => p.Id == paymentId, ct);
            if (payment == null) return null;
            return await BuildPreviewAsync(payment, ct);
        }

        public async Task<RefundResult> RefundAsync(
            int paymentId, string reason, string? providerReference, int adminId, CancellationToken ct)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var payment = await _db.Payments
                .Include(p => p.InvoiceAllocations)
                .FirstOrDefaultAsync(p => p.Id == paymentId, ct);
            if (payment == null) return new(false, "Paiement introuvable.", null);

            var wallet = await _db.LockWalletAsync(payment.SchoolId, ct);
            await _db.Entry(payment).ReloadAsync(ct);

            var preview = await BuildPreviewAsync(payment, ct);
            if (preview.BlockedReason != null)
            {
                await tx.RollbackAsync(ct);
                return new(false, preview.BlockedReason, preview);
            }

            var now = DateTime.UtcNow;
            var debit = preview.WalletDebitFcfa;
            if (debit > 0)
            {
                if (wallet == null || wallet.AvailableBalance < debit)
                {
                    await tx.RollbackAsync(ct);
                    return new(false, "Solde de l'école insuffisant pour reprendre la somme.", preview);
                }
                wallet.AvailableBalance -= debit;
                if (payment.Purpose == PaymentPurpose.Donation)
                    wallet.DonationBalanceFcfa -= Math.Min(wallet.DonationBalanceFcfa, debit);
                wallet.DonationBalanceFcfa = Math.Min(wallet.DonationBalanceFcfa, wallet.AvailableBalance);
                wallet.UpdatedAt = now;

                _db.WalletTransactions.Add(new WalletTransaction
                {
                    SchoolId = payment.SchoolId,
                    Type = WalletTransactionType.Debit,
                    Source = WalletSource.Adjustment,
                    AmountFcfa = -debit,
                    BalanceAfter = wallet.AvailableBalance,
                    RelatedEntity = WalletRelatedEntity.Refund,
                    RelatedId = payment.Id,
                    Note = $"Remboursement du paiement {payment.Id} : {reason}",
                    OccurredAt = now,
                });
            }

            payment.RefundedAt = now;
            payment.RefundedFcfa = await ReserveOutflowAsync(payment, ct);
            payment.RefundWalletDebitedFcfa = debit;
            payment.RefundReason = reason.Trim();
            payment.RefundProviderReference = string.IsNullOrWhiteSpace(providerReference) ? null : providerReference.Trim();
            payment.RefundedById = adminId;
            await _db.SaveChangesAsync(ct);

            // Les factures sont recalculées APRÈS l'écriture : le paiement
            // remboursé ne compte plus dans leur somme.
            foreach (var (invoiceId, share) in InvoiceSharesOf(payment))
                await RecomputeInvoiceAsync(invoiceId, share, now, ct);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogWarning(
                "[refund] Paiement {Id} remboursé ({Amount} F, repris sur le solde {Debit} F) par {Admin} : {Reason}",
                payment.Id, payment.AmountFcfa, debit, adminId, reason);
            return new(true, null, preview);
        }

        private async Task<RefundPreview> BuildPreviewAsync(Payment payment, CancellationToken ct)
        {
            var school = await _db.Schools.AsNoTracking()
                .Where(s => s.Id == payment.SchoolId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? $"#{payment.SchoolId}";
            var available = await _db.SchoolWallets.AsNoTracking()
                .Where(w => w.SchoolId == payment.SchoolId)
                .Select(w => (long?)w.AvailableBalance).FirstOrDefaultAsync(ct) ?? 0;
            var payer = payment.DonorPhone
                ?? await _db.Users.Where(u => u.Id == payment.GuardianId).Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct);

            var effects = new List<string>();
            string? blocked = null;
            long debit = 0;

            if (payment.RefundedAt != null) blocked = $"Déjà remboursé le {payment.RefundedAt:dd/MM/yyyy}.";
            else if (payment.Status != PaymentStatus.Completed) blocked = "Seul un paiement réussi peut être remboursé.";
            else if (payment.Operator == PaymentOperator.Cash) blocked = "Un encaissement en espèces se rend au guichet : annulez-le depuis l'école.";

            if (blocked == null)
            {
                switch (payment.Purpose)
                {
                    case PaymentPurpose.Subscription:
                        if (await SettlesASubscriptionAloneAsync(payment, ct))
                            blocked = "Ce paiement est le seul qui règle ce mois d'abonnement : le rembourser offrirait le mois. "
                                      + "Remboursez plutôt le paiement en double.";
                        else
                            effects.Add("Abonnement : rien ne change (ce paiement ne réglait aucun mois en propre).");
                        break;

                    case PaymentPurpose.OcrPages:
                    case PaymentPurpose.AssistantCredits:
                        effects.Add("Les pages ou commandes achetées restent acquises à l'école (à retirer à la main si besoin).");
                        break;

                    default:
                        debit = payment.WalletCreditedFcfa;
                        if (debit > 0)
                        {
                            effects.Add($"{Fcfa(debit)} repris sur le solde de l'école (disponible : {Fcfa(available)}).");
                            if (available < debit)
                                blocked = $"Le solde de l'école ({Fcfa(available)}) ne couvre pas la reprise de {Fcfa(debit)} : "
                                          + "l'argent a déjà été retiré.";
                        }
                        if (InvoiceIdsOf(payment).Any())
                            effects.Add("La ou les factures réglées par ce paiement sont recalculées sans lui.");
                        break;
                }
            }

            if (blocked == null)
            {
                var loss = await ReserveOutflowAsync(payment, ct) - debit;
                effects.Add(loss > 0
                    ? $"Gains plateforme : −{Fcfa(loss)} (frais Wave perdus et part non versée à l'école)."
                    : "Gains plateforme : inchangés.");
                effects.Add("Le paiement reste visible dans l'historique, marqué « remboursé ».");
            }

            return new RefundPreview(payment.Id, payment.SchoolId, school, payment.Purpose.ToString(),
                payment.AmountFcfa, payment.PaidAt, payer, debit, available, effects, blocked);
        }

        /// <summary>
        /// Vrai si ce paiement est le SEUL à régler une facture d'abonnement
        /// soldée (ni prélèvement du solde, ni autre paiement non remboursé).
        /// </summary>
        private async Task<bool> SettlesASubscriptionAloneAsync(Payment payment, CancellationToken ct)
        {
            if (payment.SubscriptionInvoiceId is not int invId) return false;
            var invoice = await _db.SubscriptionInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invId, ct);
            if (invoice == null || invoice.Status != SubscriptionInvoiceStatus.Paid) return false;
            if (invoice.WalletTransactionId != null) return false;
            return !await _db.Payments.AnyAsync(p => p.Id != payment.Id
                && p.SubscriptionInvoiceId == invId
                && p.Status == PaymentStatus.Completed
                && p.RefundedAt == null, ct);
        }

        /// <summary>
        /// Ce que le remboursement retire de la réserve : la ligne de
        /// remboursement du registre Wave si elle est déjà recopiée, sinon le
        /// net encaissé — Wave restitue sa commission en remboursant (§308).
        /// </summary>
        private async Task<long> ReserveOutflowAsync(Payment payment, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(payment.ProviderTransactionId))
            {
                var line = await _db.WaveLedgerEntries.AsNoTracking()
                    .Where(e => e.IsReversal && e.CheckoutSessionId == payment.ProviderTransactionId)
                    .Select(e => (long?)e.AmountFcfa).FirstOrDefaultAsync(ct);
                if (line is long amount && amount != 0) return Math.Abs(amount);
            }
            return payment.NetCreditedFcfa > 0 ? payment.NetCreditedFcfa : payment.AmountFcfa;
        }

        private static IEnumerable<int> InvoiceIdsOf(Payment payment) =>
            InvoiceSharesOf(payment).Select(x => x.InvoiceId);

        /// <summary>Ce que le paiement apportait à chaque facture (nominal, avant plafonnement).</summary>
        private static IEnumerable<(int InvoiceId, long Share)> InvoiceSharesOf(Payment payment) =>
            payment.InvoiceAllocations.Count > 0
                ? payment.InvoiceAllocations.GroupBy(a => a.InvoiceId).Select(g => (g.Key, g.Sum(a => a.AmountFcfa)))
                : payment.InvoiceId is int id
                    ? new[] { (id, payment.TargetAmountFcfa > 0 ? payment.TargetAmountFcfa : payment.NetCreditedFcfa) }
                    : Array.Empty<(int, long)>();

        /// <summary>
        /// Recalcule ce qu'une facture a reçu, à partir des paiements réussis et
        /// non remboursés qui la visent. Ne fait que BAISSER : on retire la part
        /// du remboursé, on n'invente jamais un règlement.
        /// </summary>
        private async Task RecomputeInvoiceAsync(int invoiceId, long refundedShare, DateTime now, CancellationToken ct)
        {
            var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
            if (invoice == null || invoice.Status == InvoiceStatus.Cancelled) return;

            var direct = await _db.Payments
                .Where(p => p.InvoiceId == invoiceId && p.Status == PaymentStatus.Completed
                            && p.RefundedAt == null && !p.InvoiceAllocations.Any())
                .SumAsync(p => p.Operator == PaymentOperator.Cash
                    ? p.AmountFcfa
                    : (p.TargetAmountFcfa > 0 ? p.TargetAmountFcfa : p.NetCreditedFcfa), ct);
            var allocated = await _db.PaymentInvoiceAllocations
                .Where(a => a.InvoiceId == invoiceId && a.Payment.Status == PaymentStatus.Completed
                            && a.Payment.RefundedAt == null)
                .SumAsync(a => a.AmountFcfa, ct);

            // Jamais plus bas que « actuel − part du remboursé » : un historique
            // que ce calcul ne voit pas (ancienne écriture) n'est pas effacé.
            var paid = Math.Max(
                Math.Min(invoice.AmountDueFcfa, direct + allocated),
                Math.Max(0, invoice.AmountPaidFcfa - refundedShare));
            if (paid >= invoice.AmountPaidFcfa) return;

            invoice.AmountPaidFcfa = paid;
            invoice.UpdatedAt = now;
            if (invoice.Status == InvoiceStatus.Paid && paid < invoice.AmountDueFcfa)
                invoice.Status = InvoiceStatus.Pending;
        }

        private static string Fcfa(long amount) =>
            amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ") + " F";
    }
}
