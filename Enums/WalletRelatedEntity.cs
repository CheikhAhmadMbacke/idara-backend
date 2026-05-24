namespace Idara.API.Enums
{
    /// <summary>
    /// Type d'entité à l'origine d'une WalletTransaction. RelatedId pointe vers
    /// l'instance correspondante (Payment.Id, Withdrawal.Id, ...). Pas de FK
    /// physique car le type cible varie — résolution applicative à la lecture.
    /// </summary>
    public enum WalletRelatedEntity
    {
        Payment = 0,
        Withdrawal = 1,
        Subscription = 2,
        Topup = 3,
        Refund = 4,
        Adjustment = 5,
        MassPayout = 6
    }
}
