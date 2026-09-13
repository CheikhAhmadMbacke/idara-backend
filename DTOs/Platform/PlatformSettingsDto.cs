using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Platform
{
    /// <summary>Vue lecture des réglages globaux plateforme.</summary>
    public class PlatformSettingsDto
    {
        public long MinPayinFcfa { get; set; }
        public long MinWithdrawalFcfa { get; set; }
        /// <summary>Taux retenu par SenePay + opérateur sur un encaissement (saisi).</summary>
        public double PayinFeePercent { get; set; }

        /// <summary>Frais opérateur sur un décaissement, prélevés en plus (saisi).</summary>
        public double PayoutFeePercent { get; set; }

        /// <summary>
        /// Majoration au payeur — <b>dérivée</b> des deux taux ci-dessus, plus
        /// jamais saisie. Conservée sous ce nom pour que les écrans parents et
        /// les pages publiques qui l'affichent n'aient rien à changer.
        /// </summary>
        public double ParentFeePercent { get; set; }

        public bool SmsBilingual { get; set; }
        public bool SubscriptionEnforcementEnabled { get; set; }
        public DateTime? UpdatedAt { get; set; }

        /// <summary>Ce que la production a réellement prélevé, en regard des taux saisis.</summary>
        public FeeCalibrationDto Calibration { get; set; } = new();
    }

    /// <summary>
    /// 🔎 <b>Le contrôle qui empêche la rechute.</b> Les taux saisis servent à
    /// calculer ce qu'on demande au payeur ; ceux-ci disent ce que le
    /// prestataire a <b>effectivement</b> prélevé. Tant que les deux coïncident,
    /// l'aller-retour d'un paiement est neutre pour la plateforme.
    /// </summary>
    /// <remarks>
    /// Sans cet écran, une hausse de la grille SenePay ne se manifeste par
    /// aucune erreur : elle se paie, en silence, sur la trésorerie de la
    /// plateforme. C'est très exactement ce qui s'est produit pendant quatre
    /// mois avec une majoration sous-calibrée de 0,45 point.
    /// </remarks>
    public class FeeCalibrationDto
    {
        /// <summary>Paiements <c>Completed</c> hors espèces pris dans la mesure.</summary>
        public int PayinSampleCount { get; set; }

        /// <summary>Taux d'encaissement MESURÉ : (brut − net crédité) / brut, en %.</summary>
        public double? MeasuredPayinFeePercent { get; set; }

        /// <summary>Retraits <c>Completed</c> dont les frais sont connus.</summary>
        public int PayoutSampleCount { get; set; }

        /// <summary>Taux de décaissement MESURÉ : frais / montant retiré, en %.</summary>
        public double? MeasuredPayoutFeePercent { get; set; }

        /// <summary>
        /// Majoration qui serait neutre d'après les taux MESURÉS. À comparer à
        /// <see cref="PlatformSettingsDto.ParentFeePercent"/> : un écart
        /// signifie que la plateforme avance (ou encaisse) la différence.
        /// </summary>
        public double? MeasuredNeutralParentFeePercent { get; set; }

        /// <summary>
        /// Ce que l'écart coûte (négatif) ou rapporte (positif) à la plateforme
        /// sur 1 000 000 FCFA facturés aux familles. Rend l'écart lisible :
        /// « 0,45 point » ne parle à personne, « 4 154 F par million » si.
        /// </summary>
        public long? GapPerMillionFcfa { get; set; }
    }

    /// <summary>Mise à jour des réglages globaux (SuperAdmin only).</summary>
    public class UpdatePlatformSettingsDto
    {
        [Range(0, 1_000_000, ErrorMessage = "Le montant minimum de paiement doit être entre 0 et 1 000 000.")]
        public long MinPayinFcfa { get; set; }

        [Range(0, 100_000_000, ErrorMessage = "Le montant minimum de retrait doit être entre 0 et 100 000 000.")]
        public long MinWithdrawalFcfa { get; set; }

        // 🔴 La majoration au payeur N'EST PLUS saisie : elle se déduit des deux
        // taux ci-dessous (cf. PlatformSettings.ParentFeeMultiplier). La saisir
        // était la cause de l'écart de 0,45 point resté invisible quatre mois.
        //
        // Les deux sont bornés < 100 strict : le taux d'encaissement est au
        // DÉNOMINATEUR de la majoration, donc 100 % donnerait une division par
        // zéro. 95 est déjà très au-dessus du réel (5,37 % et 1,77 %).

        //
        // 🔴 NULLABLES, et c'est le §140 qui l'impose. Une application déjà
        // installée envoie l'ANCIEN corps : `parentFeePercent` + `payoutFeePercent`,
        // sans `payinFeePercent`. Sur un double NON-nullable, l'absence vaut ZÉRO —
        // le taux d'encaissement tomberait à 0, la majoration à 1,77 %, et chaque
        // famille paierait 5,8 points de moins que ce que coûte son paiement.
        // Silencieusement, jusqu'à ce que la trésorerie le dise.
        //
        // `null` = « ne pas toucher », comme pour tout PATCH partiel du projet (§12).

        /// <summary>Taux retenu par SenePay + opérateur sur un encaissement (5.37 = 5,37 %).</summary>
        [Range(0.01, 95, ErrorMessage = "Les frais d'encaissement (%) doivent être entre 0,01 et 95.")]
        public double? PayinFeePercent { get; set; }

        /// <summary>Frais opérateur sur un décaissement (1.77 = 1,77 %).</summary>
        [Range(0, 95, ErrorMessage = "Les frais de retrait (%) doivent être entre 0 et 95.")]
        public double? PayoutFeePercent { get; set; }

        /// <summary>Envoyer les SMS en FR+AR (true) ou une seule langue par utilisateur (false).</summary>
        public bool SmsBilingual { get; set; } = true;

        /// <summary>Active le blocage 402 des écoles en ReadOnly/Suspended (machine à états abonnement). Défaut false.</summary>
        public bool SubscriptionEnforcementEnabled { get; set; } = false;
    }
}
