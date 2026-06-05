namespace Idara.API.Enums
{
    /// <summary>
    /// Cycle de vie d'un retrait école (SenePay Payout).
    /// - Initiated : Withdrawal créé, montant réservé (Available → Pending),
    ///   payout envoyé à SenePay, en attente du webhook final.
    /// - Completed : webhook `disbursement.completed` reçu, débit définitif du
    ///   Pending, bénéficiaire payé. Statut final.
    /// - Failed : webhook `disbursement.failed` OU échec de l'appel SenePay à
    ///   l'init → restitution Pending → Available. Statut final.
    /// - Cancelled : annulé avant traitement (réservé pour usage futur).
    /// </summary>
    public enum WithdrawalStatus
    {
        Initiated = 0,
        Completed = 1,
        Failed = 2,
        Cancelled = 3
    }
}
