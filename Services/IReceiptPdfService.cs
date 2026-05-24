using Idara.API.Models;

namespace Idara.API.Services
{
    public interface IReceiptPdfService
    {
        /// <summary>
        /// Génère le reçu PDF pour un paiement complété et retourne le chemin
        /// relatif servi par nginx (ex: <c>/uploads/receipts/receipt-42.pdf</c>).
        /// Idempotent : un appel sur un Payment déjà généré écrase le fichier.
        /// </summary>
        Task<string> GenerateAsync(Payment payment, School school, Student? student, Invoice? invoice);
    }
}
