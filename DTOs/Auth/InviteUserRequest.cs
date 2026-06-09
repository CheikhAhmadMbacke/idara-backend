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
        [RegularExpression("^(Teacher|SchoolStaff|Guardian)$",
            ErrorMessage = "Fonction invalide. Valeurs autorisées : Teacher, SchoolStaff, Guardian.")]
        public string Function { get; set; } = string.Empty;

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
        }
    }
}
