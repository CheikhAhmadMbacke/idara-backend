using System.Text.Json.Serialization;

namespace Idara.API.DTOs.Senepay
{
    /// <summary>Réponse de <c>GET /api/v1/payouts</c> (liste paginée des décaissements).</summary>
    public class SenePayPayoutListResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("data")]
        public List<SenePayPayoutListItem> Data { get; set; } = new();

        [JsonPropertyName("pagination")]
        public SenePayPagination? Pagination { get; set; }
    }

    public class SenePayPayoutListItem
    {
        [JsonPropertyName("disbursement_id")]
        public string? DisbursementId { get; set; }

        /// <summary>Réf marchand. Pour un retrait Idara = Withdrawal.Id (numérique).</summary>
        [JsonPropertyName("external_id")]
        public string? ExternalId { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        // Montants en decimal (SenePay renvoie des décimaux, cf. gotcha §53).
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("net_amount")]
        public decimal NetAmount { get; set; }

        [JsonPropertyName("recipient_phone")]
        public string? RecipientPhone { get; set; }

        [JsonPropertyName("operator")]
        public string? Operator { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }

        [JsonPropertyName("completed_at")]
        public DateTime? CompletedAt { get; set; }
    }

    public class SenePayPagination
    {
        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("pageSize")]
        public int PageSize { get; set; }

        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }

        [JsonPropertyName("totalPages")]
        public int TotalPages { get; set; }
    }
}
