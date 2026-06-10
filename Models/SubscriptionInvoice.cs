using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Facture d'abonnement plateforme (école → Idara), générée par le cron de
    /// facturation à chaque échéance. Append-only (entité de facturation).
    /// </summary>
    public class SubscriptionInvoice
    {
        public int Id { get; set; }

        public int SubscriptionId { get; set; }
        public Subscription Subscription { get; set; } = null!;

        public int SchoolId { get; set; }

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        public long AmountFcfa { get; set; }

        public SubscriptionInvoiceStatus Status { get; set; } = SubscriptionInvoiceStatus.Pending;

        public DateTime IssuedAt { get; set; }
        public DateTime? PaidAt { get; set; }

        /// <summary>Id de la WalletTransaction du débit (audit), si payé par prélèvement wallet.</summary>
        public int? WalletTransactionId { get; set; }

        public string? PdfPath { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
