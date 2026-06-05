namespace Idara.API.Enums
{
    /// <summary>
    /// Cycle de vie d'un retrait école (SenePay Payout).
    /// - Initiated : Withdrawal créé, montant réservé (Available → Pending),
    ///   payout envoyé à SenePay, en attente du webhook final.
    /// - UnderVerification : issue INDÉTERMINÉE (timeout/5xx du POST, ou statut
    ///   non terminal renvoyé : submitted/processing/pending/pending_verification).
    ///   Les fonds restent réservés (Pending) — on NE restitue PAS. On poll
    ///   `GET /payouts/{id}` (autoritatif) avec back-off jusqu'à un état terminal.
    ///   C'est le garde-fou contre la double dépense (cf. PAYOUT_DOUBLE_SPEND_RISK.md).
    /// - Completed : webhook `disbursement.completed` (ou GET status completed)
    ///   reçu, débit définitif du Pending, bénéficiaire payé. Statut final.
    /// - Failed : SEULEMENT sur état terminal explicite — webhook
    ///   `disbursement.failed`, GET status failed/cancelled, ou rejet 4xx
    ///   synchrone du POST (aucun fonds sorti) → restitution Pending → Available.
    ///   Statut final.
    /// - Cancelled : annulé avant traitement (réservé pour usage futur).
    /// </summary>
    public enum WithdrawalStatus
    {
        Initiated = 0,
        Completed = 1,
        Failed = 2,
        Cancelled = 3,
        UnderVerification = 4
    }
}
