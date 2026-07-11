namespace Idara.API.Enums
{
    /// <summary>Nature d'une écriture du livre de caisse (gestion financière daara).</summary>
    public enum CashEntryType
    {
        Income = 0,   // entrée (recette : cash reçu, don en espèces…)
        Expense = 1   // sortie (dépense : salaire, charge, loyer…)
    }
}
