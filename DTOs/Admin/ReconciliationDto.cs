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
        /// <summary>
        /// P total = Subscriptions + Surplus8 + PagesDeLecture + Injections
        /// + RetoursDébits − FraisPayoutÉcole − Sorties.
        /// </summary>
        public long TotalFcfa { get; set; }

        /// <summary>Revenus d'abonnement encaissés (factures abo payées).</summary>
        public long SubscriptionRevenueFcfa { get; set; }

        /// <summary>Excédent de la majoration des payins (part au-dessus du crédit école).
        /// ⚠️ Depuis le 2026-09-13 la majoration est calibrée pour être NEUTRE : cet
        /// excédent couvre le frais de retrait à venir, il n'est pas un gain.</summary>
        public long Surplus8PercentFcfa { get; set; }

        /// <summary>
        /// Pages de lecture de cahier vendues aux écoles, en NET encaissé. La
        /// seule recette entrante qui ne crédite aucun wallet : elle entre donc
        /// en totalité dans P.
        /// </summary>
        public long OcrPageRevenueFcfa { get; set; }

        /// <summary>Part des frais de retrait école restée à la charge de la
        /// plateforme = (montant reçu + frais) − débit du portefeuille. Nulle pour
        /// les retraits postérieurs au 2026-09-17 : l'école paie sa propre sortie,
        /// R et D baissent du même montant, P ne bouge pas.</summary>
        public long SchoolPayoutFeesFcfa { get; set; }

        /// <summary>Sorties plateforme enregistrées (retraits gains + ajustements manuels).</summary>
        public long PlatformOutflowsFcfa { get; set; }

        /// <summary>Injections de capital enregistrées (argent ajouté à la réserve, +P).</summary>
        public long CapitalInjectionsFcfa { get; set; }

        /// <summary>Contreparties des débits manuels de wallets école (montant revenu aux gains, +P).</summary>
        public long SchoolDebitReturnsFcfa { get; set; }
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
