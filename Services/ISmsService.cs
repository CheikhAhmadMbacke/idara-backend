namespace Idara.API.Services
{
    /// <summary>
    /// Résultat d'un envoi SMS. Ne lève jamais — un échec (transport, clé
    /// manquante, refus opérateur) revient en <see cref="Success"/> = false avec
    /// le détail dans <see cref="Error"/>, pour que l'appelant (best-effort
    /// post-commit) log et continue sans casser la transaction métier.
    /// </summary>
    public record SmsSendResult(
        bool Success,
        string? MessageId = null,
        string? Status = null,
        string? Error = null,
        string? Cost = null);

    public interface ISmsService
    {
        /// <summary>
        /// Envoie un SMS à un numéro déjà normalisé en E.164 (<c>+221...</c>).
        /// </summary>
        Task<SmsSendResult> SendAsync(string toE164, string message, CancellationToken ct = default);
    }
}
