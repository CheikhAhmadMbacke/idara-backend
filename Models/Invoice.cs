using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Facture mensuelle générée pour un élève en mode BillingMode.FixedAmount.
    /// En FreeAmount, aucune Invoice n'est pré-générée — les Payment sont créés
    /// directement sans InvoiceId.
    ///
    /// AmountDueFcfa est snapshoté à la génération, MAIS re-tarifé dynamiquement
    /// tant que la facture est IMPAYÉE : un changement de tarif (général, classe,
    /// override) met à jour les factures impayées via InvoiceRepricingService
    /// (décision produit 2026-07-07). Les factures payées/partiellement payées/
    /// annulées, et celles avec un paiement en cours, restent figées.
    /// </summary>
    public class Invoice
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        /// <summary>Premier jour de la période facturée (UTC, minuit).</summary>
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public DateTime DueDate { get; set; }

        public long AmountDueFcfa { get; set; }
        public long AmountPaidFcfa { get; set; }

        public InvoiceStatus Status { get; set; } = InvoiceStatus.Pending;

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
