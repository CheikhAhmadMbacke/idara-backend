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

        // Mois grégoriens en arabe, forme usuelle au Sénégal (يناير…), pas les
        // noms levantins (كانون…).
        private static readonly string[] ArMonths =
        {
            "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
            "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
        };

        public static string Ar(InvoiceType type, DateTime periodStart) =>
            type == InvoiceType.Registration
                ? "رسوم التسجيل"
                : $"الاشتراك الشهري {ArMonths[periodStart.Month - 1]} {periodStart.Year}";
    }
}
