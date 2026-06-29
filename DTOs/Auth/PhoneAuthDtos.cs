using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Auth
{
    /// <summary>
    /// Demande d'un code par SMS (activation / mot de passe oublié) pour un
    /// compte identifié par téléphone.
    /// </summary>
    public class PhoneRequestCodeRequest
    {
        [Required(ErrorMessage = "Le numéro est requis.")]
        public string Phone { get; set; } = string.Empty;
    }

    /// <summary>
    /// Définition (ou réinitialisation) du mot de passe d'un compte téléphone,
    /// après vérification du code reçu par SMS. Auto-login en retour.
    /// </summary>
    public class PhoneSetPasswordRequest
    {
        [Required(ErrorMessage = "Le numéro est requis.")]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le code est requis.")]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le mot de passe est requis.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Le mot de passe doit contenir au moins 6 caractères.")]
        public string NewPassword { get; set; } = string.Empty;
    }

    /// <summary>
    /// Vérification du mot de passe de l'utilisateur connecté (re-authentification
    /// légère). Sert de verrou à l'écran « Vue d'ensemble Paiement » et de
    /// step-up au retrait (remplace l'OTP retrait).
    /// </summary>
    public class VerifyPasswordRequest
    {
        [Required(ErrorMessage = "Le mot de passe est requis.")]
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// Changement de mot de passe par un utilisateur connecté (vérifie l'ancien).
    /// </summary>
    public class ChangePasswordRequest
    {
        [Required(ErrorMessage = "Le mot de passe actuel est requis.")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le nouveau mot de passe est requis.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Le mot de passe doit contenir au moins 6 caractères.")]
        public string NewPassword { get; set; } = string.Empty;
    }
}
