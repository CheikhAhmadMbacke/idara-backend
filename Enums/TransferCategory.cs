namespace Idara.API.Enums
{
    /// <summary>
    /// Nature d'un transfert sortant du wallet école (retrait simple ou paiement
    /// d'une charge). Liste fixe pour cohérence des stats — "Other" couvre les
    /// cas rares. Sérialisé en string (PascalCase) via JsonStringEnumConverter.
    /// </summary>
    public enum TransferCategory
    {
        /// <summary>Retrait simple (vers soi / non catégorisé). Valeur par défaut, rétro-compatible.</summary>
        Withdrawal = 0,
        TeacherSalary = 1,
        StaffSalary = 2,
        Rent = 3,
        Supplier = 4,
        Utilities = 5,
        Other = 6
    }
}
