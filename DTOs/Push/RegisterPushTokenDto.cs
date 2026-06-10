using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Push
{
    /// <summary>Enregistrement d'un jeton FCM pour l'utilisateur connecté.</summary>
    public class RegisterPushTokenDto
    {
        [Required]
        [StringLength(4096, MinimumLength = 10)]
        public string Token { get; set; } = string.Empty;

        /// <summary>"android" ou "web" (informatif). Optionnel.</summary>
        [StringLength(20)]
        public string Platform { get; set; } = string.Empty;
    }

    /// <summary>Désenregistrement d'un jeton (au logout).</summary>
    public class UnregisterPushTokenDto
    {
        [Required]
        [StringLength(4096, MinimumLength = 10)]
        public string Token { get; set; } = string.Empty;
    }

    /// <summary>
    /// Notification personnalisée envoyée par le SuperAdmin (broadcast).
    /// </summary>
    public class BroadcastPushDto
    {
        /// <summary>Titre (défaut "Idara" si vide).</summary>
        [StringLength(80)]
        public string? Title { get; set; }

        [Required]
        [StringLength(500, MinimumLength = 1)]
        public string Body { get; set; } = string.Empty;

        /// <summary>Cible : "all" | "role" | "school".</summary>
        [Required]
        public string Target { get; set; } = "all";

        /// <summary>Rôle visé si Target="role" (SchoolAdmin, SchoolStaff, Teacher, Guardian).</summary>
        public string? Role { get; set; }

        /// <summary>École visée si Target="school".</summary>
        public int? SchoolId { get; set; }

        /// <summary>Lien externe optionnel ouvert au clic (ex. fiche Play Store).</summary>
        [StringLength(500)]
        public string? Url { get; set; }
    }
}
