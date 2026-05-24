using System.Text.Json.Serialization;

namespace Idara.API.DTOs.Senepay
{
    /// <summary>
    /// Payload reçu de SenePay sur le webhook payin (Checkout ET API Direct partagent
    /// le même format — cf. doc SenePay §4 et note ligne 764).
    ///
    /// Les noms de propriétés sont en camelCase côté SenePay (différent du payout
    /// qui est en snake_case — attention à ne pas confondre). Le statut "Complete"
    /// est SANS le 'd' final, voir doc §4 note ligne 304.
    /// </summary>
    public class SenePayPayinWebhookPayload
    {
        /// <summary>Type d'événement, ex: "checkout.session.completed" / "checkout.session.failed".</summary>
        [JsonPropertyName("event")]
        public string? Event { get; set; }

        /// <summary>Token de session SenePay (ex: "chk_abc123" pour Checkout, ou "afp_tx_..." pour Direct).</summary>
        [JsonPropertyName("sessionToken")]
        public string? SessionToken { get; set; }

        /// <summary>Notre référence côté Idara — on y mettra le Payment.Id sérialisé en string.</summary>
        [JsonPropertyName("orderReference")]
        public string? OrderReference { get; set; }

        /// <summary>"Complete" (sans 'd') sur succès, "Failed" sur échec. Pas "Completed".</summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("fees")]
        public long Fees { get; set; }

        [JsonPropertyName("netAmount")]
        public long NetAmount { get; set; }

        /// <summary>ID stable du payin côté SenePay (ex: "SENEPAY_PAYIN_a1b2c3d4..."). Notre clé d'idempotence.</summary>
        [JsonPropertyName("transactionId")]
        public string? TransactionId { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime? Timestamp { get; set; }
    }
}
