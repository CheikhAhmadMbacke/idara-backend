namespace Idara.API.Enums
{
    /// <summary>
    /// État de traitement d'un WebhookEvent reçu.
    /// - Received : enregistré, traitement en cours ou différé.
    /// - Processed : traitement terminé sans erreur.
    /// - Duplicate : event déjà connu (ExternalEventId existait), 200 retourné sans rejeu.
    /// - InvalidSignature : signature HMAC invalide, 401 retourné, event archivé pour audit.
    /// - ProcessingFailed : signature OK mais traitement métier a échoué (DB indispo, etc.).
    /// </summary>
    public enum WebhookEventStatus
    {
        Received = 0,
        Processed = 1,
        Duplicate = 2,
        InvalidSignature = 3,
        ProcessingFailed = 4
    }
}
