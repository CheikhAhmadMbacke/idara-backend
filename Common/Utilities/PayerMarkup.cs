using Idara.API.Enums;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// 🔴 <b>Interdiction de majorer le payeur.</b> L'article 8.2 du contrat
    /// Wave (renforcé par le 16.4.2) prévoit la <b>résiliation SANS PRÉAVIS</b>
    /// si des frais sont facturés à un Détenteur pour payer via Wave.
    ///
    /// <para>Ce verrou existe parce que la clause peut être déclenchée par un
    /// tiers : il suffisait qu'un directeur remette « le parent paie les frais »
    /// dans l'écran de configuration pour que la majoration reparte sur les
    /// familles. Le réglage est désormais refusé côté serveur, et ce garde-fou
    /// couvre le cas où une valeur « Parent » subsisterait malgré tout en base.</para>
    ///
    /// <para>⚠️ Les <b>établissements</b> ne sont pas visés par la clause :
    /// ils reçoivent de l'argent, ils n'en paient pas. Leur faire porter la
    /// commission est conforme, et c'est ce que fait le mode « l'école paie ».</para>
    /// </summary>
    public static class PayerMarkup
    {
        /// <summary>
        /// Toujours <c>false</c> depuis la bascule vers Wave (2026-09-17). Ne
        /// repasser à <c>true</c> qu'avec un avenant écrit au contrat.
        /// </summary>
        public const bool Allowed = false;

        /// <summary>
        /// Politique de frais réellement applicable, quel que soit ce que dit
        /// le réglage enregistré. Un seul endroit décide.
        /// </summary>
        public static FeesPayer Effective(FeesPayer stored) =>
            Allowed ? stored : FeesPayer.School;

        /// <summary>
        /// Montant à débiter pour encaisser <paramref name="targetFcfa"/>.
        /// Sans majoration autorisée, c'est le montant exact : le payeur règle
        /// ce qu'il doit, la commission est prélevée sur ce qui est reversé.
        /// </summary>
        public static long ChargeFor(ProviderFees fees, FeesPayer stored, long targetFcfa) =>
            Allowed && Effective(stored) == FeesPayer.Parent && fees.IsConfigured
                ? fees.ChargeFor(targetFcfa)
                : targetFcfa;
    }
}
