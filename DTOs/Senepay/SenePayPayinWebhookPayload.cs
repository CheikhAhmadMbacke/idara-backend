using System.Text.Json.Serialization;

namespace Idara.API.DTOs.Senepay
{
    /// <summary>
    /// Payload réel envoyé par SenePay sur le webhook payin (API Direct).
    ///
    /// IMPORTANT : la doc SenePay publique décrit le format Checkout (camelCase,
    /// "Complete" sans 'd'). L'API Direct utilise un format DIFFÉRENT —
    /// snake_case partout + montants en `decimal` + statut "Completed" avec 'd'.
    /// Découvert empiriquement le 2026-05-24 via un vrai webhook reçu.
    ///
    /// Si SenePay aligne un jour les deux formats, on adaptera ce DTO. En
    /// attendant, ce DTO matche EXACTEMENT le format API Direct observé.
    /// </summary>
    public class SenePayPayinWebhookPayload
    {
        /// <summary>"payin.completed" / "payin.failed" — API Direct.
        /// (Doc Checkout dit "checkout.session.completed" — formats différents.)</summary>
        [JsonPropertyName("event")]
        public string? Event { get; set; }

        /// <summary>ID stable côté SenePay (ex: "SENEPAY_PAYIN_xxx"). Notre clé d'idempotence.</summary>
        [JsonPropertyName("transaction_id")]
        public string? TransactionId { get; set; }

        /// <summary>Notre Payment.Id sérialisé en string (passé via `orderId` à l'init).</summary>
        [JsonPropertyName("order_id")]
        public string? OrderId { get; set; }

        /// <summary>"Completed" / "Failed" / "Cancelled" / "Expired". Attention 'd' final (≠ doc Checkout).</summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        /// <summary>Montant brut payé par le parent. SenePay envoie en decimal (200.0), pas long.</summary>
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        /// <summary>Total frais SenePay+opérateur prélevés au payin (decimal).</summary>
        [JsonPropertyName("fees")]
        public decimal Fees { get; set; }

        /// <summary>Montant net crédité au wallet marchand SenePay (decimal).</summary>
        [JsonPropertyName("net_amount")]
        public decimal NetAmount { get; set; }

        /// <summary>ID Wave/AfribaPay/Orange... (le PSP en aval de SenePay). Ex: "PIM260524...".</summary>
        [JsonPropertyName("provider_transaction_id")]
        public string? ProviderTransactionId { get; set; }

        [JsonPropertyName("operator")]
        public string? Operator { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("customer_phone")]
        public string? CustomerPhone { get; set; }

        [JsonPropertyName("failed_reason")]
        public string? FailedReason { get; set; }

        [JsonPropertyName("error_code")]
        public string? ErrorCode { get; set; }

        [JsonPropertyName("is_sandbox")]
        public bool IsSandbox { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime? Timestamp { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; set; }
    }
}
