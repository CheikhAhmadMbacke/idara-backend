using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Admin
{
    /// <summary>
    /// Retrait des GAINS PLATEFORME vers un compte Mobile Money (SenePay Payout).
    /// Saisie manuelle des coordonnées (comme les écoles). Step-up : mot de passe
    /// SuperAdmin. Garde-fou serveur : ne peut pas faire passer la réserve sous
    /// D × (1 + marge) — ne touche jamais l'argent des écoles.
    /// </summary>
    public class PlatformWithdrawRequestDto : IValidatableObject
    {
        [Range(1, long.MaxValue)]
        public long Amount { get; set; }

        /// <summary>Mot de passe SuperAdmin (step-up de sécurité).</summary>
        [Required]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string RecipientName { get; set; } = string.Empty;

        /// <summary>Numéro bénéficiaire sans indicatif (7XXXXXXXX).</summary>
        [Required]
        [RegularExpression(@"^7\d{8}$", ErrorMessage = "Numéro invalide (format 7XXXXXXXX).")]
        public string RecipientPhone { get; set; } = string.Empty;

        [Required]
        public string RecipientPhoneConfirm { get; set; } = string.Empty;

        /// <summary>"wave" ou "orange".</summary>
        [Required]
        public string Operator { get; set; } = string.Empty;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (RecipientPhone != RecipientPhoneConfirm)
                yield return new ValidationResult(
                    "Les deux numéros ne correspondent pas.", new[] { nameof(RecipientPhoneConfirm) });

            var op = Operator?.ToLowerInvariant();
            if (op != "wave" && op != "orange")
                yield return new ValidationResult(
                    "Opérateur non supporté (wave ou orange).", new[] { nameof(Operator) });
        }
    }
}
