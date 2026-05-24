namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Réponse retournée au client Flutter après initiation. Le client utilise
    /// `NextAction` pour piloter son UX :
    /// - "REDIRECT_TO_PROVIDER_LINK" → ouvrir RedirectUrl (Wave)
    /// - "OTP_REQUIRED" → demander OTP à l'utilisateur, puis rappeler /initiate avec PaymentId + OtpCode
    /// - "USSD_PUSH" → afficher "Confirmez sur votre téléphone", poller /status
    /// - "NONE" → terminé (success ou fail final), lire Status + FailureReason
    /// </summary>
    public class InitiatePaymentResponseDto
    {
        public int PaymentId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string NextAction { get; set; } = string.Empty;
        public string? RedirectUrl { get; set; }
        public bool OtpRequired { get; set; }
        public string? ErrorCode { get; set; }
        public string? FailureReason { get; set; }

        /// <summary>Montant effectivement débité du parent (inclut +8% si FeesPayer=Parent).</summary>
        public long AmountChargedFcfa { get; set; }
    }
}
