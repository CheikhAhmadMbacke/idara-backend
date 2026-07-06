using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Admin
{
    /// <summary>
    /// Ajustement manuel du solde d'un wallet école par le SuperAdmin (crédit ou
    /// débit). Écriture comptable directe sur la dette D — ne déclenche AUCUN
    /// mouvement SenePay. Cas d'usage : corriger un paiement parent réellement
    /// arrivé mais marqué <c>failed</c> (crédit), ou débiter un retrait payé hors
    /// SenePay (débit).
    /// </summary>
    public class AdjustSchoolWalletDto
    {
        [Range(1, long.MaxValue, ErrorMessage = "Le montant doit être positif.")]
        public long AmountFcfa { get; set; }

        /// <summary>true = créditer (augmente le solde école), false = débiter.</summary>
        public bool IsCredit { get; set; }

        /// <summary>Motif obligatoire (tracé dans l'historique wallet + audit).</summary>
        [Required(ErrorMessage = "Le motif est obligatoire.")]
        [StringLength(500, MinimumLength = 3, ErrorMessage = "Le motif doit faire au moins 3 caractères.")]
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Si true, crée une contrepartie sur les gains plateforme P pour garder
        /// R = D + P exact (ajustement traité comme un transfert école ↔ plateforme) :
        /// crédit → P baisse (la plateforme finance) ; débit → P monte (retirable).
        /// Si false, ajustement brut de D seul (à utiliser quand le montant
        /// correspond à de l'argent réellement entré/sorti de la réserve SenePay —
        /// ex : crédit d'un paiement réellement encaissé → l'écart se résorbe).
        /// </summary>
        public bool CounterpartPlatformGains { get; set; }

        /// <summary>Step-up de sécurité : mot de passe du SuperAdmin connecté.</summary>
        [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>Résultat d'un ajustement de wallet école (nouveau solde).</summary>
    public class AdjustSchoolWalletResultDto
    {
        public int SchoolId { get; set; }
        public long AvailableBalanceFcfa { get; set; }
        public long DonationBalanceFcfa { get; set; }
        public long PendingBalanceFcfa { get; set; }
    }
}
