using Idara.API.Enums;

namespace Idara.API.Services
{
    /// <summary>
    /// Issue d'une transition de règlement payout (pour logging / réaction appelant).
    /// </summary>
    public enum PayoutSettlementOutcome
    {
        /// <summary>Rien à faire (déjà dans l'état cible, idempotent).</summary>
        NoOp,
        /// <summary>Retrait clôturé Completed (débit définitif du Pending).</summary>
        SettledCompleted,
        /// <summary>Réservation restituée (Pending → Available), retrait Failed.</summary>
        Restituted,
        /// <summary>Passé en UnderVerification (fonds maintenus réservés).</summary>
        MarkedUnderVerification,
        /// <summary>Restitution erronée ANNULÉE par re-débit (double dépense évitée).</summary>
        Corrected,
        /// <summary>Correction nécessaire mais impossible (solde Available insuffisant) — alerte levée.</summary>
        CorrectionImpossible,
        /// <summary>`failed` reçu sur un retrait déjà Completed — pas de restitution, alerte levée.</summary>
        FailedAfterCompleted
    }

    /// <summary>
    /// Source unique des transitions de solde wallet liées aux décaissements
    /// (SenePay Payout). Tout mouvement Available/Pending passe ici, sous verrou
    /// pessimiste (<c>LockWalletAsync</c>) dans une transaction. Appelé par :
    /// le controller de retrait (réponse synchrone), le webhook payout, le job
    /// de vérification, le job de réconciliation.
    ///
    /// Règle d'or (anti double dépense, cf. PAYOUT_DOUBLE_SPEND_RISK.md) : on ne
    /// restitue QUE sur état terminal explicite. Tout le reste reste réservé.
    /// Invariant comptable préservé : Σ WalletTransaction.AmountFcfa == AvailableBalance.
    /// </summary>
    public interface IPayoutSettlementService
    {
        /// <summary>
        /// Clôt un retrait en Completed (débit définitif du Pending). Idempotent.
        /// Si le retrait est déjà Failed (donc restitué) ET que ce completed est
        /// authentique → re-débit correcteur de l'Available (annule la restitution)
        /// + alerte DoubleSpendCorrected. Si l'Available est insuffisant pour
        /// re-débiter → aucune mutation + alerte CorrectionImpossible.
        /// </summary>
        Task<PayoutSettlementOutcome> SettleCompletedAsync(
            int withdrawalId, string? disbursementId, long feesFcfa, long netReceivedFcfa,
            DateTime? completedAtUtc, string source, CancellationToken ct = default);

        /// <summary>
        /// Restitue la réservation (Pending → Available) et marque Failed.
        /// N'agit QUE si le retrait est encore Initiated ou UnderVerification.
        /// Si déjà Completed → pas de restitution (l'argent est parti) + alerte
        /// FailedAfterCompleted.
        /// </summary>
        Task<PayoutSettlementOutcome> SettleFailedAsync(
            int withdrawalId, string reason, string? disbursementId,
            DateTime? failedAtUtc, string source, CancellationToken ct = default);

        /// <summary>
        /// Passe un retrait Initiated en UnderVerification : aucun mouvement de
        /// solde (le Pending reste réservé), planifie le 1er poll dans 30s.
        /// NoOp si le retrait n'est plus Initiated (déjà tranché entre-temps).
        /// </summary>
        Task<PayoutSettlementOutcome> MarkUnderVerificationAsync(
            int withdrawalId, string? disbursementId, string reason,
            string source, CancellationToken ct = default);

        /// <summary>
        /// Persiste une alerte payout (table PayoutAlert) + LogCritical greppable.
        /// Utilisé par les jobs (réconciliation, retrait coincé) et en interne.
        /// </summary>
        Task RaiseAlertAsync(
            PayoutAlertType type, int? schoolId, int? withdrawalId,
            string message, object? details, CancellationToken ct = default);
    }
}
