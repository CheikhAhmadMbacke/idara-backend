using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Student
{
    public class GuardianInputDto : IValidatableObject
    {
        // Email FACULTATIF (incrément 2) : le responsable s'identifie par numéro.
        [Idara.API.Common.Validation.OptionalEmailAddress]
        public string? Email { get; set; }

        // ⚠️ Ni le prénom ni le nom ne sont [Required] SÉPARÉMENT : au Sénégal,
        // beaucoup de responsables sont connus sous un seul nom (« Sokhna »,
        // « Baay Moor »). Exiger les deux faisait refuser la saisie — et le
        // responsable était alors purement et simplement perdu. La règle est
        // donc « au moins l'un des deux », vérifiée ci-dessous (même principe
        // que SchoolNameRule pour le nom bilingue du daara).
        [StringLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [StringLength(100)]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le numéro de téléphone du responsable est requis.")]
        public string? PhoneNumber { get; set; }

        [StringLength(50)]
        public string? Relationship { get; set; }

        public bool IsPrimaryGuardian { get; set; }

        /// <summary>Nom affiché : les deux parties si elles existent, sinon
        /// celle qui est renseignée. Jamais d'espace en trop.</summary>
        public string ComposeFullName() =>
            $"{FirstName} {LastName}".Trim();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName))
            {
                yield return new ValidationResult(
                    "Le nom du responsable est requis.",
                    new[] { nameof(FirstName), nameof(LastName) });
            }
        }
    }
}
