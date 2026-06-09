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
}
