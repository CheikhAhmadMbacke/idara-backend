using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Student
{
    public class GuardianInputDto
    {
        // Email FACULTATIF (incrément 2) : le responsable s'identifie par numéro.
        [Idara.API.Common.Validation.OptionalEmailAddress]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Le prénom du responsable est requis.")]
        [StringLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le nom du responsable est requis.")]
        [StringLength(100)]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le numéro de téléphone du responsable est requis.")]
        public string? PhoneNumber { get; set; }

        [StringLength(50)]
        public string? Relationship { get; set; }

        public bool IsPrimaryGuardian { get; set; }
    }
}
