using Idara.API.Models;

namespace Idara.API.Services
{
    public interface IReceiptPdfService
    {
        /// <summary>
        /// Chemin relatif attendu du reçu d'un paiement, à utiliser quand
        /// <c>Payment.ReceiptPdfPath</c> est vide (paiements antérieurs à sa mise en
        /// place). Ne touche pas au disque : le nom contient un suffixe HMAC, il ne
        /// peut donc plus être reconstitué à la main sur un site d'appel.
        /// </summary>
        string RelativePathFor(int paymentId);

        /// <summary>
        /// Génère le reçu PDF pour un paiement complété et retourne le chemin
        /// relatif servi par nginx (ex:
        /// <c>/uploads/receipts/receipt-42-Xk3p_9QeR1sTuVwZ.pdf</c>).
        /// Idempotent : un appel sur un Payment déjà généré écrase le fichier.
        /// <paramref name="donor"/> est fourni pour un DON (Purpose=Donation) →
        /// en-tête « Reçu de don » + bloc donateur (nom + Particulier/Organisation).
        /// <paramref name="consolidatedLines"/> est fourni pour un paiement GLOBAL
        /// (un parent règle plusieurs enfants) → tableau « élèves concernés » à la
        /// place du bloc élève unique.
        /// </summary>
        Task<string> GenerateAsync(
            Payment payment, School school, Student? student, Invoice? invoice, User? donor = null,
            IReadOnlyList<ReceiptConsolidatedLine>? consolidatedLines = null,
            User? payer = null);
    }

    /// <summary>Une ligne du reçu consolidé : un enfant réglé, sa période et son montant.</summary>
    public record ReceiptConsolidatedLine(string StudentName, string PeriodLabel, long AmountFcfa);
}
