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

        /// <summary>
        /// Mode d'envoi des SMS de notification. <c>true</c> (défaut) : les DEUX
        /// versions FR + AR dans le même corps (compréhension garantie, mais ~3×
        /// plus cher car l'arabe force l'UCS-2 = 70 car/segment). <c>false</c> :
        /// une seule langue selon <c>User.PreferredLanguage</c> (~3× moins cher).
        /// Bascule à chaud par le SuperAdmin sans redéploiement.
        /// </summary>
        public bool SmsBilingual { get; set; } = true;

        /// <summary>
        /// Active l'application de la machine à états d'abonnement (Phase 4) : si
        /// <c>true</c>, le middleware bloque réellement les écoles en ReadOnly
        /// (écritures interdites) / Suspended (tout interdit) avec un 402. Si
        /// <c>false</c> (défaut), tout le code de facturation tourne (génération
        /// de factures, transitions d'état) mais AUCUNE école n'est bloquée —
        /// permet de déployer et d'observer avant d'activer le verrou. Bascule à
        /// chaud par le SuperAdmin (Réglages plateforme).
        /// </summary>
        public bool SubscriptionEnforcementEnabled { get; set; } = false;

        /// <summary>
        /// Rôles (CSV, ex. "SchoolAdmin,SchoolStaff") pour lesquels l'installation
        /// de la DERNIÈRE version Android est OBLIGATOIRE (modal bloquant in-app).
        /// Les rôles absents ne voient qu'un bandeau doux « mise à jour disponible ».
        /// Vide (défaut) = personne n'est forcé. Édité par le SuperAdmin via
        /// l'endpoint dédié <c>/api/app-version/config</c> (PAS via le PUT des
        /// réglages plateforme, pour ne pas être écrasé par un ancien client).
        /// </summary>
        public string AndroidForcedUpdateRoles { get; set; } = string.Empty;

        /// <summary>
        /// 📖 Quand la reprise du type des matières de Coran a été jouée.
        /// </summary>
        /// <remarks>
        /// 🔴 <b>Une reprise de données se joue UNE FOIS.</b> Sans ce marqueur,
        /// elle tournerait à chaque démarrage de l'API : une école qui repasse
        /// volontairement sa matière sur un autre type la verrait rebasculer au
        /// déploiement suivant, sans jamais comprendre pourquoi — elle perdrait
        /// toujours. C'est la discipline du §74, appliquée à une conversion que
        /// PostgreSQL ne sait pas exprimer (la normalisation est en C#) et qui ne
        /// pouvait donc pas vivre dans le <c>Up()</c> d'une migration.
        /// </remarks>
        public DateTime? QuranSubjectsRetypedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        /// <summary>Multiplicateur appliqué au montant cible parent : 1 + p/100.</summary>
        [NotMapped]
        public double ParentFeeMultiplier => 1 + ParentFeePercent / 100.0;

        /// <summary>Taux de frais payout : p/100. Sert à majorer le montant envoyé à SenePay.</summary>
        [NotMapped]
        public double PayoutFeeRate => PayoutFeePercent / 100.0;
    }
}
