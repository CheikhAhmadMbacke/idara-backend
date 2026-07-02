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

        /// <summary>Réf marchand. Pour un retrait Idara = Withdrawal.Id (numérique) ; null pour un retrait dashboard.</summary>
        [JsonPropertyName("external_id")]
        public string? ExternalId { get; set; }

        /// <summary>
        /// Source du décaissement : "api" (initié par notre backend) ou "dashboard"
        /// (retrait manuel depuis le dashboard marchand SenePay). Champ demandé à
        /// SenePay — null tant que l'endpoint n'est pas enrichi ; on retombe alors
        /// sur le préfixe du disbursement_id (DISB_ / SENEPAY_PAYOUT_).
        /// </summary>
        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        // Montants en decimal (SenePay renvoie des décimaux, cf. gotcha §53).

        /// <summary>Montant du décaissement (envoyé au bénéficiaire).</summary>
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        /// <summary>Montant net reçu par le bénéficiaire.</summary>
        [JsonPropertyName("net_amount")]
        public decimal NetAmount { get; set; }

        /// <summary>Frais opérateur prélevés (0 tant que SenePay ne le renvoie pas).</summary>
        [JsonPropertyName("fees")]
        public decimal Fees { get; set; }

        /// <summary>
        /// Montant TOTAL débité de la réserve marchand pour ce décaissement (frais
        /// inclus) = l'impact réel sur le solde. C'est ce qu'on impute en
        /// réconciliation. Champ demandé à SenePay ; si absent, on retombe sur
        /// (amount + fees).
        /// </summary>
        [JsonPropertyName("amount_debited")]
        public decimal? AmountDebited { get; set; }

        [JsonPropertyName("recipient_phone")]
        public string? RecipientPhone { get; set; }

        [JsonPropertyName("recipient_name")]
        public string? RecipientName { get; set; }

        [JsonPropertyName("operator")]
        public string? Operator { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }

        [JsonPropertyName("completed_at")]
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Impact réel sur la réserve marchand : amount_debited si fourni, sinon
        /// amount + fees (fallback avant l'enrichissement SenePay). C'est le montant
        /// à imputer en réconciliation pour un retrait dashboard.
        /// </summary>
        public decimal ReserveDebit => AmountDebited ?? (Amount + Fees);

        /// <summary>
        /// true = retrait effectué depuis le dashboard marchand (hors Idara).
        /// Priorité au champ explicite `source`, repli sur le préfixe du disbursement_id.
        /// </summary>
        public bool IsDashboard =>
            string.Equals(Source, "dashboard", StringComparison.OrdinalIgnoreCase)
            || (Source == null && DisbursementId != null
                && DisbursementId.StartsWith("SENEPAY_PAYOUT_", StringComparison.OrdinalIgnoreCase));
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
