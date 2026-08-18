namespace Idara.API.Services.Notifications
{
    /// <summary>
    /// Demande d'envoi d'une notification SMS (+ push en fan-out).
    /// </summary>
    /// <param name="UserId">Destinataire (si connu), pour l'audit + le push.</param>
    /// <param name="RawPhone">Numéro brut (sera normalisé en E.164 +221).</param>
    /// <param name="PreferredLanguage">Langue de REPLI ("fr"/"ar") si le compte du
    /// destinataire est introuvable — la langue réellement utilisée est RELUE
    /// depuis le compte du RÉCEPTEUR au moment de l'envoi (règle d'or 2026-08-18),
    /// jamais déduite du contexte de l'envoyeur.</param>
    /// <param name="Message">Le message dans ses deux versions FR/AR.</param>
    /// <param name="Bilingual">Réglage plateforme : true = FR+AR dans le même corps.
    /// Le service force de toute façon le bilingue pour un destinataire JAMAIS
    /// connecté (sa préférence stockée n'est qu'un héritage de l'admin créateur).</param>
    /// <param name="TemplateCode">Code template (INVOICE_DUE, PAYMENT_RECEIVED, INVOICE_OVERDUE, INVITE…).</param>
    /// <param name="RelatedEntityId">Id métier lié (InvoiceId / PaymentId…), pour dédup des rappels.</param>
    /// <param name="PushRoute">Route in-app ouverte au clic de la notif push (ex. "/guardian/invoices"). Optionnel.</param>
    public record NotificationSmsRequest(
        int? UserId,
        string? RawPhone,
        string PreferredLanguage,
        BilingualMessage Message,
        bool Bilingual,
        string TemplateCode,
        int? RelatedEntityId = null,
        string? PushRoute = null);

    /// <summary>
    /// Demande d'envoi d'une notification PUSH UNIQUEMENT (pas de SMS) à un
    /// utilisateur — pour les canaux où le SMS n'est pas voulu (notifs école,
    /// suivi de l'enfant : gratuit, et le SMS quotidien coûterait trop cher).
    /// </summary>
    public record PushOnlyRequest(
        int UserId,
        string PreferredLanguage,
        BilingualMessage Message,
        string TemplateCode,
        int? RelatedEntityId = null,
        string? PushRoute = null);

    /// <summary>
    /// Contenu d'un broadcast (notification personnalisée SuperAdmin) : texte
    /// libre (pas de template bilingue), avec lien externe optionnel (ex. Play
    /// Store) ouvert au clic.
    /// </summary>
    public record BroadcastContent(
        string Title,
        string Body,
        string? Url = null,
        string TemplateCode = "BROADCAST");

    public interface INotificationService
    {
        /// <summary>
        /// Envoie un SMS + un push (fan-out) et trace le résultat. NE LÈVE JAMAIS
        /// (best-effort) : à appeler après le commit de la transaction métier.
        /// Renvoie true si le SMS est parti (accepté par le fournisseur) — les
        /// appelants best-effort ignorent le retour ; l'envoi d'identifiants
        /// piloté par le modal (bouton SMS) s'en sert pour dire à l'école si
        /// l'envoi automatique a réussi ou s'il faut repasser en manuel.
        /// </summary>
        Task<bool> SendSmsAsync(NotificationSmsRequest req, CancellationToken ct = default);

        /// <summary>
        /// Envoie UNIQUEMENT un push à un utilisateur (aucun SMS). Best-effort,
        /// ne lève jamais. No-op si l'utilisateur n'a pas de token enregistré.
        /// </summary>
        Task SendPushOnlyAsync(PushOnlyRequest req, CancellationToken ct = default);

        /// <summary>
        /// Envoie un push (texte libre) à une liste d'utilisateurs (broadcast).
        /// Un seul scope DB pour tout le lot. Best-effort. Retourne le nombre
        /// d'appareils notifiés avec succès.
        /// </summary>
        Task<int> SendBroadcastAsync(
            IReadOnlyCollection<int> userIds, BroadcastContent content, CancellationToken ct = default);

        /// <summary>
        /// Vrai si un envoi de ce template a déjà été TENTÉ (succès ou non) pour
        /// cette entité. Dédup des rappels (une seule tentative par entité).
        /// </summary>
        Task<bool> HasAttemptedAsync(string templateCode, int relatedEntityId, CancellationToken ct = default);

        /// <summary>
        /// Vrai si un envoi de ce template pour cette entité a été tenté DEPUIS
        /// <paramref name="sinceUtc"/>. Sert au plafond « 1 notif/enfant/jour »
        /// (relatedEntityId = StudentId, since = minuit UTC du jour).
        /// </summary>
        Task<bool> HasAttemptedSinceAsync(
            string templateCode, int relatedEntityId, DateTime sinceUtc, CancellationToken ct = default);

        /// <summary>
        /// Notifie (push uniquement) les responsables liés à un élève d'un
        /// événement de suivi (journal, bulletin, absence). Compose une seule
        /// langue par responsable. Si <paramref name="oncePerDay"/>, plafonne à
        /// UNE notif par élève par jour pour ce template (anti-spam : plusieurs
        /// maîtres / re-sauvegardes ne re-notifient pas). Best-effort, ne lève
        /// jamais. <paramref name="message"/> doit déjà contenir le nom de l'élève.
        /// </summary>
        Task NotifyGuardiansOfStudentAsync(
            int studentId, BilingualMessage message, string templateCode,
            string? pushRoute, bool oncePerDay, CancellationToken ct = default);
    }
}
