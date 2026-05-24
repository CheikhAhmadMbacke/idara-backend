using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Trace de chaque webhook reçu (SenePay payin/payout, futurs PSP).
    ///
    /// Index UNIQUE (Provider, ExternalEventId) garantit l'idempotence stricte :
    /// si SenePay rejoue le même event (retry 1s/5s/30s), on répond 200 sans
    /// rejouer le traitement métier. Le rejeu est détecté à l'INSERT par une
    /// DbUpdateException (unique violation) → on remonte Status = Duplicate.
    ///
    /// Payload stocké en jsonb pour requêtes ad hoc sur les champs SenePay
    /// (debugging, support utilisateur).
    /// </summary>
    public class WebhookEvent
    {
        public int Id { get; set; }

        /// <summary>Provider source (ex: "SenePay"). Permet d'évoluer multi-PSP plus tard.</summary>
        public string Provider { get; set; } = string.Empty;

        /// <summary>ID stable de l'event côté provider (ex: transaction_id, disbursement_id).</summary>
        public string ExternalEventId { get; set; } = string.Empty;

        /// <summary>Type d'event (ex: "payin.completed", "disbursement.failed"). Free-form.</summary>
        public string EventType { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;

        /// <summary>SHA-256 hex de la signature reçue (audit seulement, pas de rejeu).</summary>
        public string? SignatureHash { get; set; }

        public DateTime ReceivedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public WebhookEventStatus Status { get; set; } = WebhookEventStatus.Received;

        public string? ProcessingError { get; set; }
    }
}
