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

        /// <summary>
        /// `POST /api/v1/payouts` — initie un retrait école → Mobile Money.
        /// Retourne la réponse SenePay (disbursement_id, status initial, fees...).
        /// </summary>
        /// <exception cref="SenePayApiException">
        /// Lancée si SenePay retourne une vraie 4xx/5xx (auth, montant invalide,
        /// solde marchand insuffisant, timeout...). L'appelant branche sur
        /// <see cref="SenePayApiException.StatusCode"/> : un 4xx (hors duplicate)
        /// = rejet pré-exécution sûr à restituer ; un 5xx/timeout = indéterminé
        /// → passer le retrait en UnderVerification (ne PAS restituer).
        /// </exception>
        Task<SenePayPayoutResponse> InitiatePayoutAsync(
            SenePayPayoutRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// `GET /api/v1/payouts/{id}` — état **autoritatif** d'un décaissement.
        /// <paramref name="idOrExternalId"/> accepte le disbursement_id (DISB_…)
        /// OU notre external_id (= Withdrawal.Id). Source de vérité pour trancher
        /// un retrait resté en UnderVerification.
        /// </summary>
        /// <returns>
        /// La réponse parsée ; <c>null</c> si SenePay répond 404 (décaissement
        /// jamais créé — ex. après un rejet pré-exécution).
        /// </returns>
        /// <exception cref="SenePayApiException">
        /// Lancée sur timeout / 5xx / réseau : l'appelant garde UnderVerification
        /// et réessaiera au prochain back-off (jamais de restitution sur ambigu).
        /// </exception>
        Task<SenePayPayoutStatusResponse?> GetPayoutStatusAsync(
            string idOrExternalId,
            CancellationToken ct = default);

        /// <summary>
        /// `GET /api/v1/{token}/status` — état **autoritatif** d'un payin.
        /// <paramref name="token"/> = le `token` (afp_tx_…) renvoyé par
        /// `/payments/initiate` et stocké dans <c>Payment.SenePayTransactionId</c>.
        /// Source de vérité pour trancher un Payment resté Pending (webhook
        /// manqué OU paiement abandonné par le parent : solde insuffisant, etc.).
        /// </summary>
        /// <returns>
        /// La réponse parsée ; <c>null</c> si SenePay répond 404
        /// (<c>PAYMENT_NOT_FOUND</c> — aucun paiement pour ce token).
        /// </returns>
        /// <exception cref="SenePayApiException">
        /// Lancée sur timeout / 5xx / réseau : l'appelant garde le Payment en
        /// Pending et réessaiera (jamais de transition terminale sur ambigu).
        /// </exception>
        Task<SenePayPayinStatusResponse?> GetPayinStatusAsync(
            string token,
            CancellationToken ct = default);

        /// <summary>
        /// `GET /api/v1/merchant/wallet/balance` — solde réserve du compte
        /// marchand Idara. Utilisé par le job de réconciliation quotidien.
        /// </summary>
        /// <exception cref="SenePayApiException">Lancée sur 4xx/5xx/timeout.</exception>
        Task<SenePayMerchantBalanceResponse> GetMerchantBalanceAsync(
            CancellationToken ct = default);
    }
}
