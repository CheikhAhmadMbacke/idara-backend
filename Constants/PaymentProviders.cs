namespace Idara.API.Constants
{
    /// <summary>
    /// Noms des prestataires de paiement, tels qu'ils sont ECRITS EN BASE
    /// (<c>Payment.Provider</c>, <c>Withdrawal.Provider</c>,
    /// <c>WebhookEvent.Provider</c>).
    ///
    /// <para>Figés dans le code, jamais lus d'une configuration : une faute de
    /// frappe dans un réglage casserait l'idempotence des webhooks et ferait
    /// interroger le mauvais prestataire, en silence.</para>
    /// </summary>
    public static class PaymentProviders
    {
        /// <summary>Prestataire en service depuis le 2026-09-17.</summary>
        public const string Wave = "Wave";

        /// <summary>
        /// Prestataire historique. Ne traite plus AUCUN encaissement ni
        /// décaissement neuf ; conservé en LECTURE le temps de solder les
        /// paiements encore en cours et de rapatrier la réserve.
        /// </summary>
        public const string SenePay = "SenePay";
    }
}
