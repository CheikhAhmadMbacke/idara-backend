namespace Idara.API.DTOs.Admin
{
    /// <summary>
    /// Suivi financier d'UNE école vu du back-office (SuperAdmin) : ce qu'elle
    /// doit à Idara (abonnement + échéance), si elle a payé ce mois-ci, son
    /// activité (CA scolarité), sa capacité à payer (wallet vs prochain
    /// prélèvement) et son palier d'effectif. Tout est RECALCULÉ depuis les
    /// tables sources à la lecture (§144 : ce qui se calcule ne se stocke pas).
    /// </summary>
    public class SchoolFinanceDto
    {
        public int SchoolId { get; set; }
        public string SchoolName { get; set; } = string.Empty;

        // --- Abonnement : ce que l'école devra, et quand ---

        /// <summary>"Trial" | "Active" | "PendingPayment" | "ReadOnly" | "Suspended".</summary>
        public string SubscriptionStatus { get; set; } = "Trial";

        public string? PlanName { get; set; }
        public bool PlanIsCustom { get; set; }

        /// <summary>Montant snapshoté de l'abonnement courant (FCFA / cycle).</summary>
        public long SubscriptionAmountFcfa { get; set; }

        /// <summary>
        /// Ce qui sera RÉELLEMENT prélevé à la prochaine échéance : le snapshot,
        /// sauf si l'effectif dépasse le palier d'un plan public — l'auto-upgrade
        /// (§101) facturera alors le plan suggéré.
        /// </summary>
        public long NextChargeFcfa { get; set; }

        /// <summary>Prochaine échéance de prélèvement.</summary>
        public DateTime NextBillingAt { get; set; }

        /// <summary>Fin d'essai (informatif quand Status == Trial).</summary>
        public DateTime TrialEndsAt { get; set; }

        /// <summary>PendingPayment / ReadOnly / Suspended = échéance dépassée non payée.</summary>
        public bool InArrears { get; set; }

        /// <summary>Échéance manquée à l'origine de l'impayé (quand <see cref="InArrears"/>).</summary>
        public DateTime? ArrearsSince { get; set; }

        // --- Paiement d'abonnement du mois CIVIL courant ---

        public bool PaidThisMonth { get; set; }
        public long PaidThisMonthFcfa { get; set; }

        /// <summary>Dernier prélèvement d'abonnement réussi (null = n'a jamais payé).</summary>
        public DateTime? LastSubscriptionPaymentAt { get; set; }

        /// <summary>Total des factures d'abonnement payées depuis le début (revenus Idara).</summary>
        public long IdaraRevenueTotalFcfa { get; set; }

        // --- Couverture du prochain prélèvement ---

        public long WalletAvailableFcfa { get; set; }

        /// <summary>Le wallet couvre <see cref="NextChargeFcfa"/> — sinon, impayé prévisible.</summary>
        public bool CoversNextCharge { get; set; }

        // --- Effectif et palier ---

        public int StudentCount { get; set; }
        public int? PlanStudentMax { get; set; }
        public bool ExceedsCap { get; set; }
        public string? SuggestedPlanName { get; set; }
        public long? SuggestedPlanMonthlyFcfa { get; set; }

        // --- CA de l'école (scolarité : en ligne + espèces ; dons À PART) ---

        /// <summary>Encaissements de scolarité du mois civil courant.</summary>
        public long RevenueCurrentMonthFcfa { get; set; }

        /// <summary>Moyenne mensuelle sur les mois PLEINS de la fenêtre glissante.</summary>
        public long RevenueMonthlyAvgFcfa { get; set; }

        /// <summary>Nombre de mois réellement moyennés (0 à 3 — une jeune école n'est pas diluée).</summary>
        public int RevenueAvgWindowMonths { get; set; }

        /// <summary>Dons reçus ce mois-ci (jamais mélangés au CA de scolarité).</summary>
        public long DonationsCurrentMonthFcfa { get; set; }

        // --- Ancienneté ---

        /// <summary>Début de l'abonnement (création à la validation de l'école).</summary>
        public DateTime SubscribedSince { get; set; }
    }
}
