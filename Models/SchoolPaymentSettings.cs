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

        /// <summary>Qui paie les frais SenePay sur les PAIEMENTS PARENTS.
        /// Parent = le parent (majoration +8 %, l'école reçoit le montant plein) ;
        /// School = l'école (absorbe les frais, reçoit le net).</summary>
        public FeesPayer FeesPayer { get; set; } = FeesPayer.Parent;

        /// <summary>Qui paie les frais SenePay sur les DONS reçus par le daara.
        /// Parent = le donateur (majoration, le daara reçoit le montant plein) ;
        /// School = le daara (absorbe les frais, reçoit le net). Défaut = School
        /// (décision produit 2026-07-11 : le donateur ne doit pas payer les frais).</summary>
        public FeesPayer DonationFeesPayer { get; set; } = FeesPayer.School;

        /// <summary>Jour du mois (1-15) où le cron MonthlyInvoiceGenerationJob génère les Invoices.</summary>
        public int MonthlyDueDay { get; set; } = 5;

        public BillingPeriod BillingPeriod { get; set; } = BillingPeriod.Monthly;

        /// <summary>
        /// Tarif général de l'école : montant mensuel appliqué à TOUS les élèves
        /// qui n'ont ni tarif personnalisé, ni tarif de statut, ni tarif de
        /// classe. Null = pas de tarif général.
        /// Hiérarchie complète : tarif élève &gt; tarif STATUT &gt; tarif classe
        /// &gt; tarif général (cf. <see cref="Services.FeeResolver"/>).
        /// Pratique pour les daara mono-tarif (un seul montant pour toute l'école).
        /// </summary>
        public long? GeneralMonthlyFeeFcfa { get; set; }

        // ----- Tarifs par régime d'hébergement (2026-08-09) -----
        // Montants mensuels COMPLETS (pas des suppléments) appliqués selon le
        // BoardingStatus de l'élève. Null = pas de tarif pour ce régime → on
        // retombe sur le tarif de classe puis le tarif général.
        // Décision produit : le statut prime sur la classe (un interne paie le
        // tarif internat quelle que soit sa classe).
        // Un élève sans statut renseigné n'est JAMAIS concerné par ces montants.

        /// <summary>Tarif mensuel des internes. Null = non configuré.</summary>
        public long? BoardingMonthlyFeeFcfa { get; set; }

        /// <summary>Tarif mensuel des demi-internes. Null = non configuré.</summary>
        public long? HalfBoardingMonthlyFeeFcfa { get; set; }

        /// <summary>Tarif mensuel des externes. Null = non configuré.</summary>
        public long? DayMonthlyFeeFcfa { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
