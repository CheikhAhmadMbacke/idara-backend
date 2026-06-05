using System.ComponentModel.DataAnnotations.Schema;

namespace Idara.API.Models
{
    /// <summary>
    /// Réglages globaux de la plateforme, éditables par le SuperAdmin pour ne
    /// pas avoir à toucher au code source quand SenePay change sa tarification
    /// ou qu'on veut ajuster un seuil. Table singleton : une seule ligne, PK
    /// figée à <see cref="SingletonId"/>.
    ///
    /// Les pourcentages sont stockés sous forme humaine (8 = +8 %, 1.77 =
    /// 1,77 %) ; les taux exploités par le code sont dérivés via
    /// <see cref="ParentFeeMultiplier"/> / <see cref="PayoutFeeRate"/>.
    /// </summary>
    public class PlatformSettings
    {
        public const int SingletonId = 1;

        public int Id { get; set; } = SingletonId;

        /// <summary>Montant minimum d'un paiement parent (contrainte SenePay : 200 FCFA).</summary>
        public long MinPayinFcfa { get; set; } = 200;

        /// <summary>Montant minimum d'un retrait / transfert sortant.</summary>
        public long MinWithdrawalFcfa { get; set; } = 25000;

        /// <summary>Majoration appliquée au parent quand FeesPayer=Parent (8 = +8 %).</summary>
        public double ParentFeePercent { get; set; } = 8.0;

        /// <summary>Frais opérateur+taxe au payout (1,77 %), absorbés par la majoration sortante.</summary>
        public double PayoutFeePercent { get; set; } = 1.77;

        public DateTime? UpdatedAt { get; set; }

        /// <summary>Multiplicateur appliqué au montant cible parent : 1 + p/100.</summary>
        [NotMapped]
        public double ParentFeeMultiplier => 1 + ParentFeePercent / 100.0;

        /// <summary>Taux de frais payout : p/100. Sert à majorer le montant envoyé à SenePay.</summary>
        [NotMapped]
        public double PayoutFeeRate => PayoutFeePercent / 100.0;
    }
}
