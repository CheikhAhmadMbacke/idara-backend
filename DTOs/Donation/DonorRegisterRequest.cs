using System.ComponentModel.DataAnnotations;
using Idara.API.Common.Validation;
using Idara.API.Enums;

namespace Idara.API.DTOs.Donation
{
    /// <summary>
    /// Auto-inscription d'un DONATEUR (strict minimum). Identité par téléphone
    /// (comme les comptes école) ; email facultatif. Le donateur choisit son mot
    /// de passe (pas de code SMS — enjeu faible + SMS non prérequis). Auto-login.
    /// </summary>
    public class DonorRegisterRequest
    {
        /// <summary>Nom de la personne (particulier) OU de l'organisation (Dahira, fondation…).</summary>
        [Required(ErrorMessage = "Le nom est obligatoire.")]
        [StringLength(120, MinimumLength = 2, ErrorMessage = "Le nom doit faire entre 2 et 120 caractères.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le numéro de téléphone est obligatoire.")]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Le mot de passe doit faire au moins 6 caractères.")]
        public string Password { get; set; } = string.Empty;

        /// <summary>Particulier (défaut) ou Organisation.</summary>
        public DonorType DonorType { get; set; } = DonorType.Individual;

        /// <summary>Email FACULTATIF (le donateur s'identifie par téléphone).</summary>
        [OptionalEmailAddress(ErrorMessage = "L'adresse email n'est pas valide.")]
        public string? Email { get; set; }

        public string? PreferredLanguage { get; set; }
    }
}
