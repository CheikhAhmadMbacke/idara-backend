namespace Idara.API.Models
{
    /// <summary>
    /// Jeton d'appareil pour les notifications push (Firebase Cloud Messaging).
    /// Un utilisateur peut avoir plusieurs appareils (téléphone Android + onglet
    /// web). Le <see cref="Token"/> est unique : s'il réapparaît pour un autre
    /// utilisateur (appareil partagé, re-login), on le réaffecte au nouveau
    /// propriétaire plutôt que de dupliquer.
    ///
    /// Cycle de vie : créé/rafraîchi quand l'app obtient un token et le pousse via
    /// <c>POST /api/push/register</c> ; supprimé au logout (<c>unregister</c>) ou
    /// automatiquement quand FCM signale le token comme mort (<c>Unregistered</c>)
    /// lors d'un envoi. Pas de soft-delete (donnée technique régénérable).
    /// </summary>
    public class PushDeviceToken
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        /// <summary>Jeton d'enregistrement FCM (unique). Régénéré côté client si invalidé.</summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>Plateforme d'origine : "android" ou "web" (informatif/diagnostic).</summary>
        public string Platform { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Dernière fois que le token a été ré-enregistré ou utilisé avec succès.</summary>
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    }
}
