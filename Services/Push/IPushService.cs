namespace Idara.API.Services.Push
{
    /// <summary>
    /// Résultat d'un envoi push. Ne lève jamais — un échec (token mort, transport,
    /// non configuré) revient en <see cref="Success"/> = false. Si
    /// <see cref="TokenInvalid"/> est vrai, l'appelant DOIT purger ce token de la
    /// base (FCM l'a marqué comme non enregistré : appareil désinstallé / token
    /// expiré). C'est le seul cas où l'on supprime un token.
    /// </summary>
    public record PushSendResult(
        bool Success,
        string? MessageId = null,
        string? Error = null,
        bool TokenInvalid = false);

    public interface IPushService
    {
        /// <summary>
        /// Vrai si un compte de service FCM est configuré. Permet à l'appelant
        /// d'éviter une requête DB inutile (lookup des tokens) quand le push est
        /// en no-op.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Envoie une notification push à un appareil. NE LÈVE JAMAIS (best-effort).
        /// </summary>
        /// <param name="token">Jeton d'enregistrement FCM de l'appareil cible.</param>
        /// <param name="title">Titre de la notification (ex. "Idara").</param>
        /// <param name="body">Corps de la notification.</param>
        /// <param name="data">Données silencieuses (routage côté app), optionnel.</param>
        /// <param name="link">URL ouverte au clic (web), optionnel.</param>
        Task<PushSendResult> SendAsync(
            string token, string title, string body,
            IReadOnlyDictionary<string, string>? data = null,
            string? link = null,
            CancellationToken ct = default);
    }
}
