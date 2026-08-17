using Idara.API.Enums;
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
    public record ReceiptConsolidatedLine(string StudentName, string PeriodLabel, long AmountFcfa)
    {
        /// <summary>
        /// Fabrique UNIQUE de la ligne à partir d'une allocation (le webhook, le reçu
        /// école et le reçu parent construisaient chacun la leur — trois copies qui
        /// auraient divergé au premier libellé dépendant du type de facture, §118).
        /// Le libellé est DÉRIVÉ du type : « Frais d'inscription » pour une
        /// inscription — imprimer « aout 2026 » dessus la ferait passer pour la
        /// mensualité du mois. Sans accents : GSM-7, même règle que les SMS (§88).
        /// </summary>
        public static ReceiptConsolidatedLine For(PaymentInvoiceAllocation a) => new(
            $"{a.Invoice.Student.FirstName} {a.Invoice.Student.LastName}".Trim(),
            a.Invoice.Type == InvoiceType.Registration
                ? "Frais d'inscription"
                : $"{FrMonths[a.Invoice.PeriodStart.Month - 1]} {a.Invoice.PeriodStart.Year}",
            a.AmountFcfa);

        private static readonly string[] FrMonths =
        {
            "janvier", "fevrier", "mars", "avril", "mai", "juin",
            "juillet", "aout", "septembre", "octobre", "novembre", "decembre"
        };
    }
}
