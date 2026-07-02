namespace Idara.API.DTOs.Admin
{
    /// <summary>
    /// Réconciliation financière plateforme (SuperAdmin). Identité :
    /// Réserve SenePay (R) = Dette écoles (D) + Gains plateforme (P).
    /// </summary>
    public class ReconciliationDto
    {
        /// <summary>R — solde marchand Idara chez SenePay (live).</summary>
        public long ReserveSenePayFcfa { get; set; }

        /// <summary>Indique si <see cref="ReserveSenePayFcfa"/> vient d'un appel
        /// SenePay réussi (false = SenePay injoignable, valeur non fiable).</summary>
        public bool ReserveLive { get; set; }

        /// <summary>D — total dû aux écoles (Available + Pending de tous les wallets).</summary>
        public long OwedToSchoolsFcfa { get; set; }

        /// <summary>P — gains plateforme (abonnements + excédent 8% − frais payout − sorties).</summary>
        public PlatformBalanceDto Platform { get; set; } = new();

        /// <summary>Écart R − (D + P). ≈ 0 = sain. &lt; 0 = sortie non enregistrée à consigner.</summary>
        public long DiscrepancyFcfa { get; set; }

        /// <summary>"green" | "yellow" | "red" (solvabilité R vs D).</summary>
        public string HealthColor { get; set; } = "green";

        /// <summary>Montant à ajouter pour couvrir strictement la dette (max(0, D − R)).</summary>
        public long AmountToCoverDebtFcfa { get; set; }

        /// <summary>Montant à ajouter pour atteindre la marge sereine (max(0, D×(1+marge) − R)).</summary>
        public long AmountToReachSafeFcfa { get; set; }

        /// <summary>Gains plateforme retirables en sécurité : max(0, min(P, R − D×(1+marge))).</summary>
        public long WithdrawablePlatformFcfa { get; set; }

        /// <summary>Marge de sécurité appliquée (%). Ex : 5 → seuil vert = D×1,05.</summary>
        public double SafetyMarginPercent { get; set; }

        /// <summary>Commentaire d'analyse en clair (FR).</summary>
        public string Analysis { get; set; } = string.Empty;

        public DateTime ComputedAt { get; set; }
    }

    /// <summary>Décomposition des gains plateforme P.</summary>
    public class PlatformBalanceDto
    {
        /// <summary>P total = Subscriptions + Surplus8 − SchoolPayoutFees − Outflows.</summary>
        public long TotalFcfa { get; set; }

        /// <summary>Revenus d'abonnement encaissés (factures abo payées).</summary>
        public long SubscriptionRevenueFcfa { get; set; }

        /// <summary>Excédent de la majoration +8% des payins (part au-dessus du crédit école).</summary>
        public long Surplus8PercentFcfa { get; set; }

        /// <summary>Frais de payout des retraits école (déduits, absorbés par la plateforme).</summary>
        public long SchoolPayoutFeesFcfa { get; set; }

        /// <summary>Sorties plateforme enregistrées (retraits gains + ajustements manuels).</summary>
        public long PlatformOutflowsFcfa { get; set; }

        /// <summary>Injections de capital enregistrées (argent ajouté à la réserve, +P).</summary>
        public long CapitalInjectionsFcfa { get; set; }
    }

    /// <summary>Solde d'une école (vue SuperAdmin).</summary>
    public class SchoolBalanceDto
    {
        public int SchoolId { get; set; }
        public string SchoolName { get; set; } = string.Empty;
        public long AvailableFcfa { get; set; }
        public long PendingFcfa { get; set; }
        public long TotalFcfa { get; set; }
    }
}
