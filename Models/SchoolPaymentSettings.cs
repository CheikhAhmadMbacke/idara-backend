using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Configuration de paiement par école (1-1 avec School). Créée
    /// automatiquement au seed initial. La PK est SchoolId (pas d'Id séparé) :
    /// une école = une seule config.
    /// </summary>
    public class SchoolPaymentSettings
    {
        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        public BillingMode BillingMode { get; set; } = BillingMode.FixedAmount;
        public FeesPayer FeesPayer { get; set; } = FeesPayer.Parent;

        /// <summary>Jour du mois (1-15) où le cron MonthlyInvoiceGenerationJob génère les Invoices.</summary>
        public int MonthlyDueDay { get; set; } = 5;

        public BillingPeriod BillingPeriod { get; set; } = BillingPeriod.Monthly;

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
