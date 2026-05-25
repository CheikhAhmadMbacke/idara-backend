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

        /// <summary>
        /// URL absolue de notre endpoint webhook payin, envoyée à SenePay dans
        /// le champ `webhookUrl` de chaque `POST /payments/initiate`. Côté
        /// SenePay, il n'y a PAS de webhook dashboard-global ; chaque init le
        /// reçoit (cf. feedback_senepay_webhook_per_request en mémoire).
        /// </summary>
        public string WebhookPayinUrl { get; set; } = "https://api.idara.sn/api/webhooks/senepay/payin";

        /// <summary>
        /// Base URL absolue utilisée pour construire le successUrl / cancelUrl
        /// envoyé à SenePay à chaque init. Ces URLs pointent sur notre page
        /// HTML de résultat `/pay/{paymentId}/{token}` — ouverte par le
        /// navigateur du parent après confirmation Wave, sans JWT (auth =
        /// match du token public stocké sur le Payment).
        /// </summary>
        public string PublicBaseUrl { get; set; } = "https://api.idara.sn";

        /// <summary>
        /// URL absolue de notre endpoint webhook payout (utilisée Phase 3).
        /// Envoyée à SenePay dans le champ `callback_url` de chaque payout.
        /// </summary>
        public string WebhookPayoutUrl { get; set; } = "https://api.idara.sn/api/webhooks/senepay/payout";
    }
}
