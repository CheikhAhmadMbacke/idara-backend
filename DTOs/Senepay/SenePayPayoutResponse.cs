using System.Text.Json.Serialization;

namespace Idara.API.DTOs.Senepay
{
    /// <summary>
    /// Réponse de `POST /api/v1/payouts`. `status` toujours en minuscules
    /// (pending / pending_approval / processing / submitted / completed /
    /// failed / cancelled). Les frais sont nichés dans un objet `fees`.
    /// </summary>
    public class SenePayPayoutResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("disbursement_id")]
        public string? DisbursementId { get; set; }

        [JsonPropertyName("external_id")]
        public string? ExternalId { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("fees")]
        public SenePayPayoutFees? Fees { get; set; }

        [JsonPropertyName("net_amount")]
        public decimal NetAmount { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("error_code")]
        public string? ErrorCode { get; set; }
    }

    /// <summary>Objet `fees` niché : senepay (=0 au payout) + provider (opérateur) + total.</summary>
    public class SenePayPayoutFees
    {
        [JsonPropertyName("senepay")]
        public decimal SenePay { get; set; }

        [JsonPropertyName("provider")]
        public decimal Provider { get; set; }

        [JsonPropertyName("total")]
        public decimal Total { get; set; }
    }
}
