using Idara.API.DTOs.Senepay;

namespace Idara.API.Services
{
    /// <summary>
    /// Client HTTP typé pour l'API SenePay. Tous les appels portent les headers
    /// X-Api-Key + X-Api-Secret configurés une fois dans Program.cs via
    /// AddHttpClient&lt;ISenePayClient, SenePayClient&gt;().
    /// </summary>
    public interface ISenePayClient
    {
        /// <summary>
        /// `POST /api/v1/payments/initiate` — initie un payin parent → école.
        /// Retourne la réponse SenePay (statut, token, nextAction, errorCode...).
        /// </summary>
        /// <exception cref="SenePayApiException">
        /// Lancée si SenePay retourne une vraie 4xx/5xx (problème technique).
        /// Les échecs FONCTIONNELS (status=Failed, OTP erroné) reviennent en
        /// 200 OK avec status non-Pending — pas d'exception.
        /// </exception>
        Task<SenePayInitiatePaymentResponse> InitiatePaymentAsync(
            SenePayInitiatePaymentRequest request,
            CancellationToken ct = default);
    }
}
