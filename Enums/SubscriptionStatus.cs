namespace Idara.API.Enums
{
    /// <summary>
    /// État du cycle de vie d'un abonnement plateforme d'une école.
    /// Machine à états (spec §2.3) :
    /// Trial 30j → (prélèvement OK) Active → (échec) PendingPayment (grâce 7j)
    /// → (grâce expirée) ReadOnly (14j, parents peuvent toujours payer → retry
    /// auto) → (toujours impayé) Suspended.
    /// Un crédit wallet (topup ou paiement parent) qui survient en
    /// PendingPayment/ReadOnly déclenche un retry du débit → retour Active.
    /// </summary>
    public enum SubscriptionStatus
    {
        Trial = 0,
        Active = 1,
        PendingPayment = 2,
        ReadOnly = 3,
        Suspended = 4
    }
}
