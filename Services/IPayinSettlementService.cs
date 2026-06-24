using Idara.API.Enums;

namespace Idara.API.Services
{
    /// <summary>Issue d'une tentative de règlement d'un payin.</summary>
    public enum PayinSettlementOutcome
    {
        /// <summary>Le Payment était Pending et vient de transiter (Completed/Failed/…).</summary>
        Transitioned,
        /// <summary>Le Payment était déjà non-Pending (webhook/job concurrent) — no-op idempotent.</summary>
        AlreadySettled
    }

    public readonly record struct PayinSettlementResult(
        PayinSettlementOutcome Outcome, PaymentStatus FinalStatus);

    /// <summary>
    /// Règlement d'un payin (transition de statut + crédit wallet/invoice si
    /// Completed), source UNIQUE partagée par le webhook (<c>WebhooksController</c>)
    /// ET le poll de vérification (<c>PayinVerificationJob</c>). Mirror du
    /// <see cref="IPayoutSettlementService"/> (cf. §78) : transaction + verrou
    /// pessimiste wallet + garde de statut → idempotent et concurrence-safe.
    ///
    /// <para><b>Sûreté monétaire</b> : un Payment ne peut transiter que UNE fois
    /// (garde <c>Status == Pending</c> RE-LUE sous le verrou wallet). Webhook et
    /// job se sérialisent sur le verrou : le premier tranche, le second voit
    /// non-Pending et ne fait rien → jamais de double crédit, jamais de perte.</para>
    /// </summary>
    public interface IPayinSettlementService
    {
        /// <summary>
        /// Applique un statut terminal SenePay à un Payment Pending. Crédite le
        /// wallet école + l'invoice si <paramref name="terminalStatus"/> ==
        /// Completed (logique identique au webhook historique, §82/§106). No-op
        /// idempotent si le Payment n'est plus Pending.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Si le Payment est introuvable (webhook reçu avant que l'initiate ait
        /// commité — l'appelant webhook relaie en ProcessingFailed pour rejeu).
        /// </exception>
        Task<PayinSettlementResult> SettleAsync(
            int paymentId,
            PaymentStatus terminalStatus,
            long feesFcfa,
            long netCreditedFcfa,
            string? senePayTransactionId,
            DateTime? eventTime,
            string? failureReason,
            string source,
            CancellationToken ct = default);

        /// <summary>
        /// Effets best-effort POST-complétion (reçu PDF, SMS parent, push école,
        /// retry abonnement). Ne lève JAMAIS. À appeler uniquement après un
        /// <see cref="SettleAsync"/> ayant réellement transité vers Completed.
        /// </summary>
        Task RunPostCompletionEffectsAsync(int paymentId, string source, CancellationToken ct = default);
    }
}
