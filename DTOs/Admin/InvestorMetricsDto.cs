namespace Idara.API.DTOs.Admin
{
    /// <summary>
    /// Espace « Investisseurs » (SuperAdmin) : l'historique chiffré de la
    /// plateforme, présentable comme PREUVE à un investisseur. Tout est
    /// RECALCULÉ à la lecture depuis les tables sources (§144) — aucun compteur
    /// stocké, donc aucun chiffre qui puisse dériver de la comptabilité réelle
    /// (Payments, SubscriptionInvoices, Withdrawals, Schools, Students, Users).
    /// </summary>
    public class InvestorMetricsDto
    {
        public DateTime GeneratedAt { get; set; }

        public InvestorKpisDto Kpis { get; set; } = new();

        /// <summary>Série mensuelle depuis le premier mois d'activité, ordre CHRONOLOGIQUE.</summary>
        public List<InvestorMonthDto> Months { get; set; } = new();
    }

    /// <summary>Indicateurs « à aujourd'hui ».</summary>
    public class InvestorKpisDto
    {
        // --- Parc ---
        public int SchoolsValidatedTotal { get; set; }
        /// <summary>Abonnements Active (payants, à jour).</summary>
        public int SchoolsActivePaying { get; set; }
        public int SchoolsInTrial { get; set; }
        /// <summary>PendingPayment + ReadOnly (impayé en cours d'escalade).</summary>
        public int SchoolsInArrears { get; set; }
        public int SchoolsSuspended { get; set; }
        /// <summary>Effectif RÉEL aujourd'hui (ni supprimés, ni sortis — §159).</summary>
        public int StudentsActiveTotal { get; set; }
        public int GuardianAccountsTotal { get; set; }
        public int TeacherStaffAccountsTotal { get; set; }

        // --- Revenu récurrent ---
        /// <summary>MRR : Σ des montants d'abonnement des écoles en statut Active.</summary>
        public long MrrActiveFcfa { get; set; }
        /// <summary>MRR potentiel : Σ des montants des écoles encore en essai.</summary>
        public long MrrPipelineFcfa { get; set; }
        /// <summary>Revenu moyen par école payante (MRR actif / écoles actives).</summary>
        public long ArpuFcfa { get; set; }

        // --- Cumuls depuis le lancement ---
        public long GmvOnlineTotalFcfa { get; set; }
        public int PaymentsOnlineCountTotal { get; set; }
        public long GmvCashTotalFcfa { get; set; }
        public long SubscriptionRevenueTotalFcfa { get; set; }
        public long PaymentMarginTotalFcfa { get; set; }
        /// <summary>CA plateforme cumulé = abonnements + marge sur paiements.</summary>
        public long GrossRevenueTotalFcfa { get; set; }
    }

    /// <summary>Un mois civil de la série historique.</summary>
    public class InvestorMonthDto
    {
        public int Year { get; set; }
        public int Month { get; set; }

        /// <summary>Mois en cours = chiffres PARTIELS (dit explicitement — un
        /// investisseur ne doit jamais prendre un mois entamé pour un mois plein).</summary>
        public bool IsCurrentPartialMonth { get; set; }

        // --- CA plateforme ---
        /// <summary>Factures d'abonnement encaissées ce mois.</summary>
        public long SubscriptionRevenueFcfa { get; set; }
        /// <summary>Marge sur les paiements en ligne du mois (excédent de la majoration, §112).</summary>
        public long PaymentMarginFcfa { get; set; }
        /// <summary>CA du mois = abonnements + marge paiements.</summary>
        public long GrossRevenueFcfa { get; set; }
        /// <summary>Coût direct : frais des retraits écoles complétés ce mois.</summary>
        public long PayoutFeesFcfa { get; set; }
        /// <summary>CA net du mois = CA − frais de payout.</summary>
        public long NetRevenueFcfa { get; set; }

        // --- Volume traité (la traction d'une plateforme de paiement) ---
        /// <summary>Σ des montants payés EN LIGNE (Wave/Orange, tous motifs) ce mois.</summary>
        public long GmvOnlineFcfa { get; set; }
        public int PaymentsOnlineCount { get; set; }
        /// <summary>Espèces encaissées au guichet et tracées dans Idara ce mois.</summary>
        public long GmvCashFcfa { get; set; }
        public int PaymentsCashCount { get; set; }

        // --- Croissance du parc ---
        /// <summary>Écoles VALIDÉES ce mois (entrée réelle en service, pas l'inscription).</summary>
        public int NewSchools { get; set; }
        public int CumulativeSchools { get; set; }
        /// <summary>Fiches élèves créées ce mois (hors supprimées).</summary>
        public int NewStudents { get; set; }
        /// <summary>Cumul des fiches créées (≠ effectif actif : les sortis y restent).</summary>
        public int CumulativeStudents { get; set; }
        public int NewGuardianAccounts { get; set; }
        public int CumulativeGuardianAccounts { get; set; }
    }
}
