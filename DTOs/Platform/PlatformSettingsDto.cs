using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Platform
{
    /// <summary>Vue lecture des réglages globaux plateforme.</summary>
    public class PlatformSettingsDto
    {
        public long MinPayinFcfa { get; set; }
        public long MinWithdrawalFcfa { get; set; }

        // ===== Ce que le prestataire prélève — les SEULS chiffres saisis =====
        // `null` = non renseigné. Ce n'est pas une anomalie : c'est l'état d'une
        // plateforme neuve, et tant qu'il dure aucun paiement « frais au payeur »
        // ne part. Voir PlatformSettings pour la raison (aucune valeur de repli).

        /// <summary>Commission du prestataire à l'encaissement, en % du montant débité.</summary>
        public double? PayinProviderFeePercent { get; set; }

        /// <summary>Part opérateur à l'encaissement, en % HORS TAXE.</summary>
        public double? PayinOperatorFeePercentHt { get; set; }

        /// <summary>Part opérateur au décaissement, en % HORS TAXE.</summary>
        public double? PayoutOperatorFeePercentHt { get; set; }

        /// <summary>TVA sur les commissions opérateur, en %.</summary>
        public double? FeeVatPercent { get; set; }

        /// <summary>Les quatre taux sont-ils renseignés ? Sinon, encaissement refusé.</summary>
        public bool FeesConfigured { get; set; }

        public bool SmsBilingual { get; set; }
        public bool SubscriptionEnforcementEnabled { get; set; }
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// Ce que les taux saisis donnent concrètement, sur quelques montants.
        /// </summary>
        /// <remarks>
        /// 🔑 <b>Il n'y a plus de « taux de majoration » à afficher</b>, et c'est
        /// le fond du sujet : les frais ne sont pas un pourcentage, ils dépendent
        /// du montant (le prestataire arrondit au franc). Montrer un pourcentage
        /// unique serait remontrer la fiction qui a coûté quatre mois. On montre
        /// donc des <b>francs</b>, sur des montants réels.
        /// </remarks>
        public List<FeePreviewRowDto> Preview { get; set; } = new();

        /// <summary>Ce que la production a réellement prélevé, en regard des taux saisis.</summary>
        public FeeCalibrationDto Calibration { get; set; } = new();
    }

    /// <summary>Une ligne d'aperçu : pour cette cible, voilà ce qui se passe.</summary>
    public class FeePreviewRowDto
    {
        /// <summary>Ce que l'école veut encaisser.</summary>
        public long TargetFcfa { get; set; }

        /// <summary>Ce que la famille paiera réellement.</summary>
        public long ChargedFcfa { get; set; }

        /// <summary>Frais retenus à l'encaissement sur ce montant débité.</summary>
        public long PayinFeesFcfa { get; set; }

        /// <summary>Frais du décaissement de la cible, prélevés en plus.</summary>
        public long PayoutFeesFcfa { get; set; }

        /// <summary>
        /// Ce qui reste à la plateforme une fois tout payé. <b>Doit valoir 0.</b>
        /// Un négatif signifie qu'elle avance de l'argent.
        /// </summary>
        public long PlatformBalanceFcfa { get; set; }

        /// <summary>Majoration effective de CETTE ligne, en % — indicatif.</summary>
        public double MarkupPercent { get; set; }
    }

    /// <summary>
    /// 🔎 <b>Le contrôle qui empêche la rechute.</b> Les taux saisis servent à
    /// calculer ce qu'on demande au payeur ; ceux-ci disent ce que le
    /// prestataire a <b>effectivement</b> prélevé.
    /// </summary>
    /// <remarks>
    /// <para>La comparaison porte sur des <b>francs</b>, pas sur des taux : pour
    /// chaque paiement réglé, on recalcule les frais avec les taux saisis et on
    /// confronte au prélèvement observé. Un taux moyen masquerait justement les
    /// erreurs d'arrondi qu'on cherche à voir.</para>
    ///
    /// <para>Sans ce contrôle, une hausse de la grille du prestataire ne se
    /// manifeste par aucune erreur : elle se paie, en silence, sur la trésorerie.
    /// C'est ce qui s'est produit pendant quatre mois.</para>
    /// </remarks>
    public class FeeCalibrationDto
    {
        /// <summary>Paiements <c>Completed</c> hors espèces pris dans la mesure.</summary>
        public int PayinSampleCount { get; set; }

        /// <summary>Parmi eux, ceux dont les frais sont prédits <b>au franc près</b>.</summary>
        public int PayinExactCount { get; set; }

        /// <summary>Écart cumulé (francs) entre frais prédits et frais réels, à l'encaissement.</summary>
        public long PayinGapFcfa { get; set; }

        /// <summary>Retraits <c>Completed</c> dont les frais sont connus.</summary>
        public int PayoutSampleCount { get; set; }

        /// <summary>Parmi eux, ceux dont les frais sont prédits au franc près.</summary>
        public int PayoutExactCount { get; set; }

        /// <summary>Écart cumulé (francs) entre frais prédits et frais réels, au décaissement.</summary>
        public long PayoutGapFcfa { get; set; }

        /// <summary>Taux d'encaissement moyen observé, en % — repère, pas une règle.</summary>
        public double? ObservedPayinPercent { get; set; }

        /// <summary>Taux de décaissement moyen observé, en % — repère, pas une règle.</summary>
        public double? ObservedPayoutPercent { get; set; }

        /// <summary>`true` tant qu'aucun paiement n'a été réglé : rien à confronter.</summary>
        public bool IsEmpty => PayinSampleCount == 0 && PayoutSampleCount == 0;
    }

    /// <summary>Mise à jour des réglages globaux (SuperAdmin only).</summary>
    public class UpdatePlatformSettingsDto
    {
        [Range(0, 1_000_000, ErrorMessage = "Le montant minimum de paiement doit être entre 0 et 1 000 000.")]
        public long MinPayinFcfa { get; set; }

        [Range(0, 100_000_000, ErrorMessage = "Le montant minimum de retrait doit être entre 0 et 100 000 000.")]
        public long MinWithdrawalFcfa { get; set; }

        // 🔴 NULLABLES, et le §140 l'impose. Une application déjà installée
        // envoie l'ancien corps, sans ces champs. Sur un double NON-nullable,
        // l'absence vaut ZÉRO — les taux tomberaient à 0 et chaque famille
        // paierait des frais que personne ne couvre. `null` = « ne pas toucher »,
        // comme pour tout PATCH partiel du projet (§12).
        //
        // Bornés < 100 strict : ces taux servent à résoudre une équation où le
        // montant cherché apparaît des deux côtés ; à 100 % elle n'a pas de
        // solution.

        /// <summary>Commission du prestataire à l'encaissement (3.6 = 3,6 %).</summary>
        [Range(0, 95, ErrorMessage = "La commission d'encaissement (%) doit être entre 0 et 95.")]
        public double? PayinProviderFeePercent { get; set; }

        /// <summary>Part opérateur à l'encaissement, HORS TAXE (1.5 = 1,5 %).</summary>
        [Range(0, 95, ErrorMessage = "La commission opérateur encaissement (%) doit être entre 0 et 95.")]
        public double? PayinOperatorFeePercentHt { get; set; }

        /// <summary>Part opérateur au décaissement, HORS TAXE (1.5 = 1,5 %).</summary>
        [Range(0, 95, ErrorMessage = "La commission opérateur décaissement (%) doit être entre 0 et 95.")]
        public double? PayoutOperatorFeePercentHt { get; set; }

        /// <summary>TVA sur les commissions opérateur (18 = 18 %).</summary>
        [Range(0, 95, ErrorMessage = "La TVA (%) doit être entre 0 et 95.")]
        public double? FeeVatPercent { get; set; }

        /// <summary>Envoyer les SMS en FR+AR (true) ou une seule langue par utilisateur (false).</summary>
        public bool SmsBilingual { get; set; } = true;

        /// <summary>Active le blocage 402 des écoles en ReadOnly/Suspended (machine à états abonnement). Défaut false.</summary>
        public bool SubscriptionEnforcementEnabled { get; set; } = false;
    }
}
