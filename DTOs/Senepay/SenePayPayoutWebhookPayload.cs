using System.Text.Json.Serialization;

namespace Idara.API.DTOs.Senepay
{
    /// <summary>
    /// Payload du webhook payout SenePay (`disbursement.completed` /
    /// `disbursement.failed`). Payload **plat**, **snake_case**, pas d'enveloppe
    /// `data` (doc §6). `external_id` = notre Withdrawal.Id. La signature
    /// HMAC-SHA256 porte sur le corps BRUT (cf. gotcha §49).
    /// </summary>
    public class SenePayPayoutWebhookPayload
    {
        /// <summary>"disbursement.completed" / "disbursement.failed".</summary>
        [JsonPropertyName("event")]
        public string? Event { get; set; }

        /// <summary>ID SenePay du décaissement. Notre clé d'idempotence webhook.</summary>
        [JsonPropertyName("disbursement_id")]
        public string? DisbursementId { get; set; }

        /// <summary>Notre Withdrawal.Id sérialisé (passé en external_id à l'appel).</summary>
        [JsonPropertyName("external_id")]
        public string? ExternalId { get; set; }

        [JsonPropertyName("batch_id")]
        public string? BatchId { get; set; }

        /// <summary>"completed" / "failed" (minuscules).</summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        /// <summary>Net réellement reçu par le bénéficiaire (= amount − fees.provider).</summary>
        [JsonPropertyName("net_amount")]
        public decimal NetAmount { get; set; }

        [JsonPropertyName("fees")]
        public SenePayPayoutFees? Fees { get; set; }

        [JsonPropertyName("error_code")]
        public string? ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; set; }

        [JsonPropertyName("completed_at")]
        public DateTime? CompletedAt { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime? Timestamp { get; set; }
    }
}
