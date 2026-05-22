using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Student
{
    public class GuardianInputDto
    {
        [Required(ErrorMessage = "L'email du responsable est requis.")]
        [EmailAddress(ErrorMessage = "Format d'email invalide.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le prénom du responsable est requis.")]
        [StringLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le nom du responsable est requis.")]
        [StringLength(100)]
        public string LastName { get; set; } = string.Empty;

        public string? PhoneNumber { get; set; }

        [StringLength(50)]
        public string? Relationship { get; set; }

        public bool IsPrimaryGuardian { get; set; }
    }
}
