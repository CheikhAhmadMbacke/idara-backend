using System.Text.Json.Serialization;

namespace Idara.API.DTOs.Senepay
{
    /// <summary>
    /// Corps de requête `POST /api/v1/payments/initiate` côté SenePay (API
    /// Direct). Tous les noms de propriété sont en camelCase tels qu'attendus
    /// par SenePay — ne PAS renommer.
    /// </summary>
    public class SenePayInitiatePaymentRequest
    {
        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "XOF";

        [JsonPropertyName("countryCode")]
        public string CountryCode { get; set; } = "SN";

        /// <summary>"wave" ou "orange" (MVP Sénégal).</summary>
        [JsonPropertyName("operator")]
        public string Operator { get; set; } = string.Empty;

        /// <summary>Format international, ex: "+221770000001". SenePay normalise.</summary>
        [JsonPropertyName("customerPhone")]
        public string CustomerPhone { get; set; } = string.Empty;

        /// <summary>Pour Orange Sénégal/CI/BF/GN sur le 2e appel uniquement.</summary>
        [JsonPropertyName("otpCode")]
        public string? OtpCode { get; set; }

        /// <summary>Notre Payment.Id sérialisé en string — permet de retrouver le Payment au retour du webhook.</summary>
        [JsonPropertyName("orderId")]
        public string? OrderId { get; set; }

        [JsonPropertyName("customerName")]
        public string? CustomerName { get; set; }

        /// <summary>URL où SenePay redirige le client Wave après paiement (UI Flutter ouvre ensuite la page de polling).</summary>
        [JsonPropertyName("returnUrl")]
        public string? ReturnUrl { get; set; }

        [JsonPropertyName("cancelUrl")]
        public string? CancelUrl { get; set; }

        /// <summary>URL de notre webhook — passée à chaque init (pas de réglage dashboard SenePay).</summary>
        [JsonPropertyName("webhookUrl")]
        public string? WebhookUrl { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; set; }
    }
}
