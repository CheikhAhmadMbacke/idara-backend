namespace Idara.API.Models
{
    /// <summary>
    /// Trace append-only de chaque notification envoyée (SMS pour l'instant ;
    /// extensible à d'autres canaux). Sert (1) à prouver qu'un destinataire a
    /// été notifié, (2) à dédupliquer les rappels (ex. ne pas renvoyer 2× le
    /// même rappel de facture en retard, via <see cref="TemplateCode"/> +
    /// <see cref="RelatedEntityId"/>), (3) au diagnostic (statut provider).
    /// Pas de soft-delete — immuable.
    /// </summary>
    public class NotificationLog
    {
        public int Id { get; set; }

        /// <summary>Utilisateur destinataire, si connu (nullable : envoi possible à un numéro brut).</summary>
        public int? UserId { get; set; }

        /// <summary>Canal : "Sms" (futur : "Email", "WhatsApp").</summary>
        public string Channel { get; set; } = "Sms";

        /// <summary>Destinataire normalisé (E.164 pour un SMS).</summary>
        public string Recipient { get; set; } = string.Empty;

        /// <summary>Code du template (ex. "INVOICE_DUE", "PAYMENT_RECEIVED", "INVOICE_OVERDUE", "INVITE").</summary>
        public string TemplateCode { get; set; } = string.Empty;

        /// <summary>
        /// Id de l'entité métier liée (ex. InvoiceId pour INVOICE_DUE/OVERDUE,
        /// PaymentId pour PAYMENT_RECEIVED). Permet la déduplication des rappels.
        /// </summary>
        public int? RelatedEntityId { get; set; }

        public bool Success { get; set; }

        /// <summary>Id message renvoyé par le provider (ATXid_… pour Africa's Talking).</summary>
        public string? ProviderMessageId { get; set; }

        /// <summary>Détail d'erreur si échec (HTTP 401, timeout, invalid_phone…).</summary>
        public string? Error { get; set; }

        /// <summary>Coût brut renvoyé par le provider (informatif).</summary>
        public string? Cost { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
