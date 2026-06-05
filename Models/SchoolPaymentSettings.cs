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

        /// <summary>
        /// Tarif général de l'école : montant mensuel appliqué à TOUS les élèves
        /// qui n'ont ni override individuel ni tarif de classe. Null = pas de
        /// tarif général (on retombe sur tarif classe / override uniquement).
        /// Hiérarchie de résolution : override élève > tarif classe > tarif général.
        /// Pratique pour les daara mono-tarif (un seul montant pour toute l'école).
        /// </summary>
        public long? GeneralMonthlyFeeFcfa { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
