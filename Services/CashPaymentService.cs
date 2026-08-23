using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>Résultat d'un encaissement en espèces.</summary>
    public record CashPaymentResult(bool Ok, string? Error, Payment? Payment);

    public interface ICashPaymentService
    {
        Task<CashPaymentResult> CollectAsync(
            int schoolId, int invoiceId, long amountFcfa, string? note,
            DateTime? occurredAt, int? userId, CancellationToken ct);

        Task<CashPaymentResult> CancelAsync(
            int schoolId, int paymentId, string? reason, int? userId,
            CancellationToken ct);
    }

    /// <summary>
    /// 💵 <b>Encaissement en espèces au guichet du daara.</b>
    ///
    /// <para><b>Le trou comblé.</b> La plupart des inscriptions se règlent sur
    /// place, en liquide, le jour où l'enfant arrive — et jusqu'ici l'application
    /// n'avait <b>aucun</b> moyen de l'enregistrer : un seul chemin créditait une
    /// facture, le règlement d'un paiement en ligne. Conséquence pour une famille
    /// qui avait payé : élève affiché « en retard », compté dans les impayés de
    /// l'accueil, <b>nommé</b> dans l'alerte, relancé par SMS, et sollicité par le
    /// lien de paiement. Le daara tenait deux comptabilités qui ne se parlaient
    /// pas.</para>
    ///
    /// <para>🔴 <b>LA RÈGLE D'OR : un encaissement en espèces ne crédite JAMAIS
    /// le wallet.</b> Le wallet est l'argent réellement détenu chez le
    /// prestataire, base de la réconciliation <c>R = D + P</c> (§112). Le créditer
    /// ici ferait croire à l'école qu'elle peut retirer un argent qui est dans son
    /// tiroir : le virement échouerait faute de réserve (§111), et l'invariant
    /// serait cassé — soit, littéralement, de la création de monnaie. L'espèce va
    /// dans la <b>caisse</b>, le compte prévu pour l'argent physique.</para>
    ///
    /// <para>Trois effets, un seul geste : la facture est créditée, une entrée de
    /// caisse est écrite, et un <see cref="Payment"/> est tracé (qui a encaissé,
    /// quand, combien) pour l'historique et le reçu.</para>
    /// </summary>
    public class CashPaymentService : ICashPaymentService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<CashPaymentService> _logger;

        /// <summary>Libellé de la catégorie de caisse portée par ces écritures.</summary>
        public const string CashCategoryLabel = "Scolarité";

        public CashPaymentService(AppDbContext context, ILogger<CashPaymentService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<CashPaymentResult> CollectAsync(
            int schoolId, int invoiceId, long amountFcfa, string? note,
            DateTime? occurredAt, int? userId, CancellationToken ct)
        {
            if (amountFcfa <= 0)
                return new(false, "Le montant doit être supérieur à zéro.", null);

            var invoice = await _context.Invoices
                .FirstOrDefaultAsync(i => i.Id == invoiceId && i.SchoolId == schoolId, ct);
            if (invoice == null)
                return new(false, "Facture introuvable.", null);
            if (invoice.Status == InvoiceStatus.Cancelled)
                return new(false, "Cette facture est annulée.", null);

            var remaining = invoice.AmountDueFcfa - invoice.AmountPaidFcfa;
            if (remaining <= 0)
                return new(false, "Cette facture est déjà soldée.", null);

            // ⚠️ Encaisser plus que le reste dû rendrait la facture « payée à
            // 150 % » et fausserait la caisse : on refuse plutôt que de tronquer
            // en silence — le daara doit voir qu'il s'est trompé de montant.
            if (amountFcfa > remaining)
                return new(false,
                    $"Le montant dépasse le reste dû ({remaining:N0} FCFA).".Replace(",", " "), null);

            var now = DateTime.UtcNow;
            var day = (occurredAt?.Date ?? now.Date);
            day = DateTime.SpecifyKind(day, DateTimeKind.Utc);

            // Responsable rattaché à l'élève (primaire d'abord). Le poser sur le
            // paiement fait apparaître l'encaissement dans SON historique et
            // permet de le prévenir — un parent qui a envoyé quelqu'un payer à sa
            // place n'a sinon aucune trace de ce qui a été réglé.
            var guardianId = await _context.StudentGuardians
                .Where(g => g.StudentId == invoice.StudentId && !g.Guardian.IsDeleted)
                .OrderByDescending(g => g.IsPrimaryGuardian)
                .ThenBy(g => g.GuardianId)
                .Select(g => (int?)g.GuardianId)
                .FirstOrDefaultAsync(ct);

            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var payment = new Payment
            {
                SchoolId = schoolId,
                StudentId = invoice.StudentId,
                GuardianId = guardianId,
                InvoiceId = invoice.Id,
                Purpose = PaymentPurpose.SchoolFee,
                Operator = PaymentOperator.Cash,
                // Aucun frais : rien ne transite par le prestataire. Le daara
                // reçoit exactement ce que la famille a remis.
                AmountFcfa = amountFcfa,
                FeesFcfa = 0,
                NetCreditedFcfa = amountFcfa,
                TargetAmountFcfa = amountFcfa,
                FeesPayer = FeesPayer.School,
                Status = PaymentStatus.Completed,
                InitiatedAt = now,
                PaidAt = day,
                CollectedById = userId,
            };
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync(ct);

            // 1) La dette de la famille est soldée d'autant.
            invoice.AmountPaidFcfa += amountFcfa;
            invoice.UpdatedAt = now;
            if (invoice.AmountPaidFcfa >= invoice.AmountDueFcfa)
                invoice.Status = InvoiceStatus.Paid;

            // 2) L'argent est physiquement dans le tiroir → caisse, jamais wallet.
            _context.CashLedgerEntries.Add(new CashLedgerEntry
            {
                SchoolId = schoolId,
                Type = CashEntryType.Income,
                AmountFcfa = amountFcfa,
                Category = CashCategoryLabel,
                PaymentId = payment.Id,
                OccurredAt = day,
                Note = note,
                CreatedById = userId,
                CreatedAt = now,
            });

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "[cash] SchoolId={SchoolId} facture {InvoiceId} : {Amount} FCFA encaissés en espèces (paiement {PaymentId}, par {UserId})",
                schoolId, invoice.Id, amountFcfa, payment.Id, userId);

            return new(true, null, payment);
        }

        /// <summary>
        /// Annule un encaissement en espèces saisi par erreur.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>Rien n'est supprimé</b> : le paiement passe en
        /// <see cref="PaymentStatus.Cancelled"/> (append-only, §55), la facture
        /// est décréditée d'autant et son statut recalculé, et l'écriture de
        /// caisse est retirée du journal. Une saisie de montant se trompe
        /// forcément un jour ; sans ce chemin, la facture resterait soldée à tort
        /// et la caisse fausse, sans recours dans l'application.
        /// </remarks>
        public async Task<CashPaymentResult> CancelAsync(
            int schoolId, int paymentId, string? reason, int? userId,
            CancellationToken ct)
        {
            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.Id == paymentId && p.SchoolId == schoolId, ct);
            if (payment == null)
                return new(false, "Paiement introuvable.", null);

            // 🔴 Seuls les encaissements en espèces s'annulent ici. Un paiement en
            // ligne a bougé de l'argent réel chez le prestataire : l'annuler d'un
            // clic décréditerait la facture sans rien rembourser.
            if (payment.Operator != PaymentOperator.Cash)
                return new(false,
                    "Seul un encaissement en espèces peut être annulé ici.", null);
            if (payment.Status == PaymentStatus.Cancelled)
                return new(false, "Cet encaissement est déjà annulé.", null);
            if (payment.Status != PaymentStatus.Completed)
                return new(false, "Cet encaissement n'est pas encaissé.", null);

            var now = DateTime.UtcNow;
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            if (payment.InvoiceId is int invoiceId)
            {
                var invoice = await _context.Invoices
                    .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
                if (invoice != null)
                {
                    invoice.AmountPaidFcfa -= payment.AmountFcfa;
                    if (invoice.AmountPaidFcfa < 0) invoice.AmountPaidFcfa = 0;
                    invoice.UpdatedAt = now;
                    // Statut recalculé : la facture redevient due, et « en retard »
                    // seulement si son échéance est réellement passée.
                    if (invoice.Status != InvoiceStatus.Cancelled &&
                        invoice.AmountPaidFcfa < invoice.AmountDueFcfa)
                    {
                        invoice.Status = invoice.DueDate.Date < now.Date
                            ? InvoiceStatus.Overdue
                            : InvoiceStatus.Pending;
                    }
                }
            }

            var entries = await _context.CashLedgerEntries
                .Where(e => e.PaymentId == payment.Id && !e.IsDeleted)
                .ToListAsync(ct);
            foreach (var e in entries)
            {
                e.IsDeleted = true;
                e.UpdatedAt = now;
            }

            payment.Status = PaymentStatus.Cancelled;
            payment.FailedAt = now;
            payment.FailureReason = string.IsNullOrWhiteSpace(reason)
                ? "Encaissement en espèces annulé"
                : $"Encaissement en espèces annulé : {reason.Trim()}";

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "[cash] SchoolId={SchoolId} encaissement {PaymentId} annulé par {UserId} ({Reason})",
                schoolId, payment.Id, userId, reason);

            return new(true, null, payment);
        }
    }
}
