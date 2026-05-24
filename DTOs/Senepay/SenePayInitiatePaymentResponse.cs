using System.Text.Json.Serialization;

namespace Idara.API.DTOs.Senepay
{
    /// <summary>
    /// Réponse de `POST /api/v1/payments/initiate`. ATTENTION : SenePay
    /// retourne TOUJOURS 200 OK même sur échec fonctionnel (provider refusé,
    /// OTP erroné, solde insuffisant). Inspecter `status` pour distinguer
    /// Pending / Completed / Failed / Cancelled — pas `statut` qui est figé
    /// à true pour compatibilité historique (cf. doc §1 note ligne 645).
    /// </summary>
    public class SenePayInitiatePaymentResponse
    {
        [JsonPropertyName("statut")]
        public bool Statut { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>Token PSP (afp_tx_…) — c'est ce qu'on stocke dans Payment.SenePayInternalId ? NON : voir InternalId.</summary>
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        /// <summary>URL Wave uniquement (QR/page paiement). Null pour Orange/USSD.</summary>
        [JsonPropertyName("redirectUrl")]
        public string? RedirectUrl { get; set; }

        /// <summary>ID interne SenePay (SENEPAY_PAYIN_xxx) — stable, utilisé pour rapprochement comptable.</summary>
        [JsonPropertyName("internalId")]
        public string? InternalId { get; set; }

        /// <summary>"Pending" / "Completed" / "Cancelled" / "Failed".</summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("errorCode")]
        public string? ErrorCode { get; set; }

        [JsonPropertyName("failedReason")]
        public string? FailedReason { get; set; }

        /// <summary>REDIRECT_TO_PROVIDER_LINK / USSD_PUSH / OTP_REQUIRED / NONE.</summary>
        [JsonPropertyName("nextAction")]
        public string? NextAction { get; set; }

        [JsonPropertyName("otpRequired")]
        public bool OtpRequired { get; set; }
    }
}
