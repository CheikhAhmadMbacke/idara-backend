using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Auth
{
    public class VerifyOtpRequest
    {
        [Required(ErrorMessage = "L'email est requis.")]
        [EmailAddress(ErrorMessage = "Format d'email invalide.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le code OTP est requis.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Le code OTP doit contenir 6 chiffres.")]
        public string OtpCode { get; set; } = string.Empty;
    }
}
