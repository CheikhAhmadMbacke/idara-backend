namespace Idara.API.Enums
{
    /// <summary>
    /// Qui porte les frais SenePay + opérateur sur un paiement parent → école.
    /// - Parent : majoration sur le montant cible, calibrée pour que l'école
    ///   encaisse ET retire exactement la cible (parent paie 5 378 pour 5 000 au
    ///   taux courant de ~7,55 % — cf. PlatformSettings.ParentFeeMultiplier).
    /// - School : prélèvement à la source, l'école touche net (5 000 brut → ~4 648 net).
    /// </summary>
    public enum FeesPayer
    {
        Parent = 0,
        School = 1
    }
}
