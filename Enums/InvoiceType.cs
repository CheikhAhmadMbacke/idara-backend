namespace Idara.API.Enums
{
    /// <summary>
    /// Nature d'une facture. Valeurs persistées en integer : NE JAMAIS RÉORDONNER.
    ///
    /// Démarre à 1 exprès : les ~centaines de factures antérieures à la migration
    /// (toutes des mensualités) reçoivent la valeur par défaut de la colonne, et un
    /// enum qui démarrerait à 0 laisserait EF générer defaultValue: 0 — c'est-à-dire
    /// une valeur qui ne correspondrait à rien ou, pire, à la mauvaise nature.
    /// </summary>
    public enum InvoiceType
    {
        /// <summary>Mensualité scolaire — tout l'existant, généré par le cron.</summary>
        MonthlyFee = 1,

        /// <summary>Frais d'inscription, facturés une fois à l'arrivée de l'élève.</summary>
        Registration = 2
    }
}
