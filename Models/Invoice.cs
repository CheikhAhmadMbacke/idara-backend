using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Facture mensuelle générée pour un élève en mode BillingMode.FixedAmount.
    /// En FreeAmount, aucune Invoice n'est pré-générée — les Payment sont créés
    /// directement sans InvoiceId.
    ///
    /// AmountDueFcfa est SNAPSHOTÉ à la génération : un changement ultérieur du
    /// ClassFee ou du StudentFeeOverride n'impacte pas les Invoices déjà émises.
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
