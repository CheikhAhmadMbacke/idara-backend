namespace Idara.API.Enums
{
    /// <summary>
    /// Nature d'une alerte payout persistée dans <c>PayoutAlert</c> (et doublée
    /// d'un <c>LogCritical</c> greppable). Branchable sur WhatsApp/SMS SuperAdmin
    /// en Phase 2.
    /// </summary>
    public enum PayoutAlertType
    {
        /// <summary>Invariant de réconciliation rompu : solde réserve marchand ≠ Σ(wallets + pending) au-delà de l'epsilon.</summary>
        ReconciliationMismatch = 0,

        /// <summary>Webhook/GET `completed` arrivé sur un retrait déjà restitué (Failed) → restitution annulée par re-débit. Double dépense évitée.</summary>
        DoubleSpendCorrected = 1,

        /// <summary>État incohérent : `failed` reçu sur un retrait déjà `Completed` (l'argent est parti) — pas de restitution auto, intervention humaine requise.</summary>
        FailedAfterCompleted = 2,

        /// <summary>Retrait coincé en `UnderVerification` au-delà du seuil (48h) sans état terminal — réconciliation manuelle SenePay/AfribaPay requise.</summary>
        StuckUnderVerification = 3,

        /// <summary>Correction `DoubleSpendCorrected` impossible (solde Available insuffisant pour re-débiter) — perte/dette à régulariser manuellement.</summary>
        CorrectionImpossible = 4
    }
}
