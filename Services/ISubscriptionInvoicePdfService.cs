using Idara.API.Models;

namespace Idara.API.Services
{
    /// <summary>Génère le PDF d'une facture d'abonnement plateforme (école → Idara).</summary>
    public interface ISubscriptionInvoicePdfService
    {
        /// <summary>Génère/écrase le PDF et retourne son chemin relatif (/uploads/...).</summary>
        Task<string> GenerateAsync(SubscriptionInvoice invoice, School school, string? planName);
    }
}
