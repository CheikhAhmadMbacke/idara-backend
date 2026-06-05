using System.Text.Json.Serialization;

namespace Idara.API.DTOs.Senepay
{
    /// <summary>
    /// Réponse de <c>GET /api/v1/payouts/{id}</c> (le paramètre <c>{id}</c> accepte
    /// le <c>disbursement_id</c> préfixé <c>DISB_</c> OU notre <c>external_id</c>,
    /// doc §4). C'est la source **autoritative** de l'état d'un décaissement :
    /// SenePay rafraîchit l'état en live auprès d'AfribaPay. On l'interroge avant
    /// toute restitution sur un état resté indéterminé (timeout, statut non
    /// terminal). `status` toujours en minuscules.
    /// </summary>
    public class SenePayPayoutStatusResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("disbursement_id")]
        public string? DisbursementId { get; set; }

        [JsonPropertyName("external_id")]
        public string? ExternalId { get; set; }

        /// <summary>
        /// pending / pending_approval / processing / submitted / pending_verification
        /// / completed / failed / cancelled (toujours minuscules).
        /// Terminaux : completed, failed, cancelled. Le reste = indéterminé.
        /// </summary>
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

        [JsonPropertyName("error_code")]
        public string? ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }

        [JsonPropertyName("processed_at")]
        public DateTime? ProcessedAt { get; set; }

        [JsonPropertyName("completed_at")]
        public DateTime? CompletedAt { get; set; }
    }
}
