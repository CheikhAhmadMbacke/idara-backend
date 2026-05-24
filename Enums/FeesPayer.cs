namespace Idara.API.Enums
{
    /// <summary>
    /// Qui porte les frais SenePay + opérateur sur un paiement parent → école.
    /// - Parent : majoration +8 % sur le montant cible (parent paie 5 400 pour 5 000).
    /// - School : prélèvement à la source, l'école touche net (5 000 brut → ~4 648 net).
    /// </summary>
    public enum FeesPayer
    {
        Parent = 0,
        School = 1
    }
}
