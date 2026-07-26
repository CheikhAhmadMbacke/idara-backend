namespace Idara.API.Enums
{
    /// <summary>
    /// Nature d'un transfert sortant du wallet école (retrait simple ou paiement
    /// d'une charge). Liste fixe pour cohérence des stats — "Other" couvre les
    /// cas rares. Sérialisé en string (PascalCase) via JsonStringEnumConverter.
    /// </summary>
    /// <remarks>
    /// ⚠️ Les valeurs sont PERSISTÉES en base (colonne integer) : ne JAMAIS
    /// réordonner ni réutiliser un numéro. Toute nouvelle nature s'ajoute à la
    /// suite (≥ 7). Les libellés affichés, eux, peuvent changer librement
    /// (i18n côté app, <see cref="Common.Utilities.FinanceLabels"/> côté PDF).
    /// </remarks>
    public enum TransferCategory
    {
        /// <summary>Retrait simple (vers soi / non catégorisé). Valeur par défaut, rétro-compatible.</summary>
        Withdrawal = 0,
        TeacherSalary = 1,
        StaffSalary = 2,
        Rent = 3,

        /// <summary>Achat auprès d'un fournisseur (libellé affiché : « Achat / fournisseur »).</summary>
        Supplier = 4,

        /// <summary>Eau, électricité, internet, téléphone (libellé : « Factures et abonnements »).</summary>
        Utilities = 5,

        Other = 6,

        // --- Ajouts 2026-07-22 : natures propres au quotidien d'un daara, qui
        // tombaient toutes dans « Autre » faute de catégorie adaptée. ---

        /// <summary>Nourriture / repas des talibés (marché, riz, huile, cuisine).</summary>
        Food = 7,

        /// <summary>Avance sur salaire (distincte d'un salaire complet).</summary>
        SalaryAdvance = 8,

        /// <summary>Matériel et équipement durable (tablettes, nattes, livres, tableaux).</summary>
        Equipment = 9,

        /// <summary>Travaux et entretien des bâtiments (réparations, construction).</summary>
        Maintenance = 10,

        /// <summary>Transport et carburant.</summary>
        Transport = 11,

        /// <summary>Santé et soins des talibés (pharmacie, consultations).</summary>
        Health = 12,

        /// <summary>Frais administratifs (taxes, papiers, frais officiels).</summary>
        AdminFees = 13
    }
}
