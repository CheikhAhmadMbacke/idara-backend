using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Auth
{
    public class RegisterRequest
    {
        [Required(ErrorMessage = "L'email est requis.")]
        [EmailAddress(ErrorMessage = "Format d'email invalide.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le code OTP est requis.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Le code OTP doit contenir 6 chiffres.")]
        public string OtpCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le mot de passe est requis.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Le mot de passe doit contenir au moins 8 caractères.")]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Langue préférée pour les emails ("fr" ou "ar"). Si non précisée,
        /// "fr" par défaut.
        /// </summary>
        public string? PreferredLanguage { get; set; }
    }
}
