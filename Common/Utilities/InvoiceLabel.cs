using Idara.API.Enums;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Libellé lisible d'une facture, DÉRIVÉ de sa nature (jamais stocké) :
    /// « Frais d'inscription » pour une inscription, « août 2026 » pour une
    /// mensualité. Utilisé par le lien de paiement (page publique + modal école).
    /// </summary>
    public static class InvoiceLabel
    {
        private static readonly string[] FrMonths =
        {
            "janvier", "février", "mars", "avril", "mai", "juin",
            "juillet", "août", "septembre", "octobre", "novembre", "décembre"
        };

        public static string Fr(InvoiceType type, DateTime periodStart) =>
            type == InvoiceType.Registration
                ? "Frais d'inscription"
                : $"Mensualité {FrMonths[periodStart.Month - 1]} {periodStart.Year}";
    }
}
