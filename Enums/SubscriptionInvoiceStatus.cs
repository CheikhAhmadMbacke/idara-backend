namespace Idara.API.Enums
{
    /// <summary>État d'une facture d'abonnement plateforme (école → Idara).</summary>
    public enum SubscriptionInvoiceStatus
    {
        Pending = 0,
        Paid = 1,
        Failed = 2,
        Cancelled = 3
    }
}
