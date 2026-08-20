namespace Idara.API.Models
{
    /// <summary>
    /// Répartition PAR ENFANT d'un paiement en MONTANT LIBRE réglé depuis un lien
    /// de paiement (école hors montant fixe : aucune facture n'existe, donc
    /// <see cref="PaymentInvoiceAllocation"/> ne peut pas porter la ventilation).
    ///
    /// Purement informatif pour l'école (« pour qui l'argent est arrivé ») et
    /// pour le reçu : le crédit wallet reste global (aucune facture à solder).
    /// Append-only comme tout le socle financier.
    /// </summary>
    public class PaymentStudentAllocation
    {
        public int Id { get; set; }

        public int PaymentId { get; set; }
        public Payment Payment { get; set; } = null!;

        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        /// <summary>Part CIBLE (avant majoration) attribuée à cet enfant.</summary>
        public long AmountFcfa { get; set; }
    }
}
