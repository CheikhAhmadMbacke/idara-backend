using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Auth
{
    public class SendOtpRequest
    {
        [Required(ErrorMessage = "L'email est requis.")]
        [EmailAddress(ErrorMessage = "Format d'email invalide.")]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Langue préférée pour le contenu de l'email OTP ("fr" ou "ar").
        /// Si non précisée : "fr" pour register, langue du user existant pour reset.
        /// </summary>
        public string? PreferredLanguage { get; set; }
    }
}
