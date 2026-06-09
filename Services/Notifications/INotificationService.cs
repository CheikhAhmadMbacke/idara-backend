namespace Idara.API.Services.Notifications
{
    /// <summary>
    /// Demande d'envoi d'un SMS de notification.
    /// </summary>
    /// <param name="UserId">Destinataire (si connu), pour l'audit.</param>
    /// <param name="RawPhone">Numéro brut (sera normalisé en E.164 +221).</param>
    /// <param name="PreferredLanguage">Langue de l'utilisateur ("fr"/"ar") — utilisée si mode non bilingue.</param>
    /// <param name="Message">Le message dans ses deux versions FR/AR.</param>
    /// <param name="Bilingual">true = FR+AR dans le même corps ; false = une seule langue.</param>
    /// <param name="TemplateCode">Code template (INVOICE_DUE, PAYMENT_RECEIVED, INVOICE_OVERDUE, INVITE).</param>
    /// <param name="RelatedEntityId">Id métier lié (InvoiceId / PaymentId…), pour dédup des rappels.</param>
    public record NotificationSmsRequest(
        int? UserId,
        string? RawPhone,
        string PreferredLanguage,
        BilingualMessage Message,
        bool Bilingual,
        string TemplateCode,
        int? RelatedEntityId = null);

    public interface INotificationService
    {
        /// <summary>
        /// Envoie un SMS et trace le résultat. NE LÈVE JAMAIS (best-effort) :
        /// à appeler après le commit de la transaction métier.
        /// </summary>
        Task SendSmsAsync(NotificationSmsRequest req, CancellationToken ct = default);

        /// <summary>
        /// Vrai si un SMS de ce template a DÉJÀ été envoyé avec succès pour cette
        /// entité (dédup des rappels — ex. ne pas renvoyer 2× le rappel retard
        /// de la même facture).
        /// </summary>
        Task<bool> HasSentSuccessfullyAsync(string templateCode, int relatedEntityId, CancellationToken ct = default);
    }
}
