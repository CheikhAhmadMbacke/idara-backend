namespace Idara.API.Models
{
    /// <summary>
    /// Ligne d'allocation d'un <see cref="Payment"/> consolidé (« paiement
    /// global » d'un parent) vers une <see cref="Invoice"/> précise.
    ///
    /// Un parent ayant N enfants dans une même école paie en UNE fois le total
    /// de leurs mensualités (décision produit 2026-07-07). Le socle par élève
    /// (1 Invoice par élève) reste INCHANGÉ : c'est uniquement la couche
    /// paiement qui regroupe. Le webhook répartit ensuite le crédit sur chaque
    /// facture via ces lignes.
    ///
    /// <para><see cref="AmountFcfa"/> = montant CIBLE alloué à cette facture
    /// (son reste dû figé à l'initiation). C'est ce qu'on crédite à la facture
    /// à la complétion — jamais le net SenePay (cf. §106 : sinon facture zombie).</para>
    ///
    /// Append-only comme tout le socle financier : une correction = nouvelle
    /// écriture, jamais de mutation/suppression (hors purge d'école).
    /// </summary>
    public class PaymentInvoiceAllocation
    {
        public int Id { get; set; }

        public int PaymentId { get; set; }
        public Payment Payment { get; set; } = null!;

        public int InvoiceId { get; set; }
        public Invoice Invoice { get; set; } = null!;

        /// <summary>Reste dû de la facture figé à l'initiation (montant cible alloué).</summary>
        public long AmountFcfa { get; set; }
    }
}
