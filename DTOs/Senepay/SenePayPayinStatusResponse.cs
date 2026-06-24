using System.Text.Json.Serialization;

namespace Idara.API.DTOs.Senepay
{
    /// <summary>
    /// Réponse de `GET /api/v1/{token}/status` — état AUTORITATIF d'un payin.
    /// Source de vérité pour trancher un Payment resté Pending (webhook manqué
    /// ou paiement abandonné par le parent). Cf. doc SenePay §4.
    ///
    /// Montants en decimal (FCFA sans centimes en pratique — on arrondit au long
    /// à l'usage). `status` ∈ Pending / Completed / Failed / Cancelled.
    /// `creditedAmount` = net réellement crédité au wallet marchand (= NetAmount
    /// du webhook), `totalFee` = providerFee + senePayFee agrégés.
    /// </summary>
    public class SenePayPayinStatusResponse
    {
        [JsonPropertyName("statut")]
        public bool Statut { get; set; }

        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("orderId")]
        public string? OrderId { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        /// <summary>"Pending" / "Completed" / "Failed" / "Cancelled".</summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("totalFee")]
        public decimal TotalFee { get; set; }

        /// <summary>Net crédité au wallet marchand (équivalent NetAmount webhook).</summary>
        [JsonPropertyName("creditedAmount")]
        public decimal CreditedAmount { get; set; }

        [JsonPropertyName("errorCode")]
        public string? ErrorCode { get; set; }

        [JsonPropertyName("failedReason")]
        public string? FailedReason { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime? CreatedAt { get; set; }
    }
}
