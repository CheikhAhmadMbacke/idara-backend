using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.School
{
    /// <summary>
    /// Édition des informations d'une école par le SuperAdmin (les écoles qui
    /// veulent modifier leurs infos passent par le support avec justificatif).
    /// Ne touche PAS au statut KYC (géré par validate/reject) ni aux documents.
    /// </summary>
    public class UpdateSchoolDto
    {
        [Required(ErrorMessage = "Le nom de l'école est obligatoire.")]
        [StringLength(200, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Address { get; set; }

        [StringLength(30)]
        public string? PhoneNumber { get; set; }

        [StringLength(100)]
        public string? RepresentativeFirstName { get; set; }

        [StringLength(100)]
        public string? RepresentativeLastName { get; set; }

        [StringLength(30)]
        public string? RepresentativePhone { get; set; }
    }
}
