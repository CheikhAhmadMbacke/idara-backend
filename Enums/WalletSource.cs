namespace Idara.API.Enums
{
    /// <summary>
    /// Origine / nature d'une <see cref="Models.WalletTransaction"/>, dénormalisée
    /// pour afficher un BADGE distinct dans l'historique du wallet école (remarque
    /// utilisateur : différencier un DON d'un paiement parent) sans re-joindre le
    /// Payment à chaque affichage. Purement descriptif — n'a AUCUN impact sur les
    /// montants ni sur les deux poches (Solde paiement / Solde don), qui sont
    /// maintenues directement sur <see cref="Models.SchoolWallet"/>.
    /// </summary>
    public enum WalletSource
    {
        Payment = 0,       // crédit d'un paiement parent → école
        Donation = 1,      // crédit d'un don donateur → école
        Topup = 2,         // recharge wallet par l'école
        Subscription = 3,  // prélèvement abonnement plateforme
        Withdrawal = 4,    // retrait / transfert (réservation, débit, restitution)
        Adjustment = 5,    // correction manuelle / correcteur payout
        Other = 6
    }
}
