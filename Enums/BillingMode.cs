namespace Idara.API.Enums
{
    /// <summary>
    /// Mode de facturation parent dans une école.
    /// - FixedAmount : tarif par classe (avec override possible par élève), Invoice
    ///   mensuelle pré-générée, paiement intégral obligatoire.
    /// - FreeAmount : pas de tarif imposé, pas d'Invoice, le parent saisit le
    ///   montant qu'il veut payer (≥ 200 FCFA minimum SenePay).
    /// </summary>
    public enum BillingMode
    {
        FixedAmount = 0,
        FreeAmount = 1
    }
}
