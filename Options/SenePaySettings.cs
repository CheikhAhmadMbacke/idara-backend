namespace Idara.API.Options
{
    public class SenePaySettings
    {
        public const string SectionName = "SenePay";

        public string BaseUrl { get; set; } = "https://api.sene-pay.com";

        /// <summary>Clé publique SenePay (préfixée pk_live_* en prod, pk_test_* en sandbox).</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Secret SenePay (sk_live_* / sk_test_*). À garder côté serveur uniquement.</summary>
        public string ApiSecret { get; set; } = string.Empty;

        /// <summary>
        /// Secret de signature des webhooks SenePay (préfixé whsec_). Distinct
        /// de ApiSecret — utilisé pour vérifier le HMAC-SHA256 du header
        /// X-SenePay-Signature sur le corps BRUT du webhook reçu.
        /// </summary>
        public string WebhookSecret { get; set; } = string.Empty;
    }
}
