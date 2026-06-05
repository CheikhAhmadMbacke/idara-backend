using System.Text.Json.Serialization;

namespace Idara.API.DTOs.Senepay
{
    /// <summary>
    /// Corps de `POST /api/v1/payouts` (SenePay Payout / Disbursement).
    ///
    /// ⚠️ L'API Payout est en **snake_case** (≠ payin Direct qui mixe) et les
    /// codes opérateur sont en **minuscules** (`wave`, `orange`). Tout autre
    /// format renvoie HTTP 400 (doc §5). `amount` est un decimal.
    /// </summary>
    public class SenePayPayoutRequest
    {
        /// <summary>
        /// Idempotence : notre Withdrawal.Id sérialisé. Depuis le durcissement
        /// SenePay, l'external_id est **idempotent en replay** : un 2e POST avec
        /// le même external_id renvoie HTTP 200 + le décaissement existant (plus
        /// de 400 DUPLICATE_EXTERNAL_ID). On peut donc rejouer un POST après un
        /// timeout sans risque de double décaissement — on récupère l'état réel.
        /// </summary>
        [JsonPropertyName("external_id")]
        public string ExternalId { get; set; } = string.Empty;

        /// <summary>Montant majoré envoyé à SenePay (le bénéficiaire reçoit net après ~1,77 %).</summary>
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        /// <summary>Numéro bénéficiaire format international, ex "221771234567".</summary>
        [JsonPropertyName("phone")]
        public string Phone { get; set; } = string.Empty;

        [JsonPropertyName("recipient_name")]
        public string? RecipientName { get; set; }

        [JsonPropertyName("country")]
        public string Country { get; set; } = "SN";

        /// <summary>"wave" / "orange" (minuscules).</summary>
        [JsonPropertyName("operator")]
        public string Operator { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>URL webhook payout — passée à chaque appel (pas de réglage dashboard).</summary>
        [JsonPropertyName("callback_url")]
        public string? CallbackUrl { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; set; }
    }
}
