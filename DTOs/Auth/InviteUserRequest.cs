using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Auth
{
    public class InviteUserRequest : IValidatableObject
    {
        // Email FACULTATIF depuis l'incrément 2 Phase 2 : les utilisateurs ajoutés
        // par les écoles s'identifient par TÉLÉPHONE. L'email peut être fourni
        // mais n'est plus requis (et n'est plus le canal de notification).
        [Idara.API.Common.Validation.OptionalEmailAddress]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Le numéro de téléphone est requis.")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le nom complet est requis.")]
        [StringLength(150, MinimumLength = 2)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "La fonction est requise.")]
        [RegularExpression("^(Teacher|SchoolStaff|Guardian|Surveillant|SchoolViewer)$",
            ErrorMessage = "Fonction invalide.")]
        public string Function { get; set; } = string.Empty;

        /// <summary>
        /// false = personnel « sans appli » (cuisinière, gardien…) : compte créé
        /// pour le pointage uniquement, pas de login ni de SMS/code. Défaut true.
        /// Réservé aux rôles de personnel pointable (pas Guardian ni SchoolViewer).
        /// </summary>
        public bool CanLogin { get; set; } = true;

        /// <summary>Fonction libre affichée (« Cuisinière », « Comptable »…). Optionnel.</summary>
        [StringLength(80)]
        public string? JobTitle { get; set; }

        // ----- Champs spécifiques à Guardian -----

        /// <summary>
        /// ID de l'élève à lier au Guardian. Obligatoire pour Function = Guardian
        /// (sinon le compte créé n'aurait accès à rien).
        /// </summary>
        public int? StudentId { get; set; }

        [StringLength(80)]
        public string? Relationship { get; set; }

        public bool IsPrimaryGuardian { get; set; } = false;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Function == "Guardian" && (!StudentId.HasValue || StudentId.Value <= 0))
            {
                yield return new ValidationResult(
                    "Pour inviter un Guardian, vous devez fournir le 'StudentId' de l'élève à lui rattacher.",
                    new[] { nameof(StudentId) });
            }

            // Un compte « sans appli » n'a de sens que pour le personnel pointable
            // (il sert au pointage). Un parent et un observateur DOIVENT se
            // connecter → CanLogin=false leur est interdit.
            if (!CanLogin && (Function == "Guardian" || Function == "SchoolViewer"))
            {
                yield return new ValidationResult(
                    "Un compte sans accès à l'application n'est possible que pour le personnel.",
                    new[] { nameof(CanLogin) });
            }
        }
    }
}
