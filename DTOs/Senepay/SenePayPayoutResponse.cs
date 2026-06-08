using System.Text.Json.Serialization;

namespace Idara.API.DTOs.Senepay
{
    /// <summary>
    /// Réponse de `POST /api/v1/payouts`. `status` toujours en minuscules.
    ///
    /// ⚠️ Depuis le durcissement SenePay : le POST **ne renvoie plus `completed`
    /// de façon synchrone** en prod. Il renvoie `submitted` (ordre transmis) ou
    /// `pending_verification` (issue indéterminée : timeout/erreur transport
    /// SenePay↔AfribaPay — les fonds peuvent être sortis ou non). Le statut
    /// TERMINAL (`completed` / `failed` / `cancelled`) arrive par webhook ou via
    /// `GET /payouts/{id}` (autoritatif). On ne restitue JAMAIS sur un statut non
    /// terminal — on passe le Withdrawal en UnderVerification et on poll le GET.
    ///
    /// Les frais sont nichés dans un objet `fees`.
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

        /// <summary>Montant réellement débité de la réserve marchand (= amount + frais en on_top).</summary>
        [JsonPropertyName("amount_debited")]
        public decimal AmountDebited { get; set; }

        /// <summary>Mode de frais effectivement appliqué par SenePay (`on_top` / `inclusive`).</summary>
        [JsonPropertyName("fee_mode")]
        public string? FeeMode { get; set; }

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
