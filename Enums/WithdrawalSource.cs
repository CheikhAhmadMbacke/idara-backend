namespace Idara.API.Enums
{
    /// <summary>
    /// Poche du wallet école dans laquelle un retrait/transfert puise (décision
    /// produit : le daara CHOISIT au retrait). Le solde disponible est scindé en
    /// deux poches — Solde paiement (mensualités parents) et Solde don
    /// (<see cref="Models.SchoolWallet.DonationBalanceFcfa"/>).
    /// - Total  : puise dans le total ; la part prise sur les dons = ce qui
    ///   dépasse le solde paiement (paiements d'abord, puis dons).
    /// - Fee    : uniquement le solde paiement (refuse si insuffisant).
    /// - Donation : uniquement le solde don (refuse si insuffisant).
    /// Valeur 0 = Total → rétro-compatible : les retraits existants (avant toute
    /// notion de don) puisaient dans le total, DonationAmountFcfa = 0.
    /// </summary>
    public enum WithdrawalSource
    {
        Total = 0,
        Fee = 1,
        Donation = 2
    }
}
