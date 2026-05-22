using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Auth
{
    /// <summary>
    /// Soumission des informations KYC de l'école (documents transmis en base64).
    /// </summary>
    public class SubmitKycRequest
    {
        [Required(ErrorMessage = "Le nom de l'école est requis.")]
        [StringLength(200, MinimumLength = 2)]
        public string SchoolName { get; set; } = string.Empty;

        [Required(ErrorMessage = "L'adresse de l'école est requise.")]
        [StringLength(300)]
        public string SchoolAddress { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le téléphone de l'école est requis.")]
        public string SchoolPhone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le prénom du représentant est requis.")]
        [StringLength(100)]
        public string RepFirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le nom du représentant est requis.")]
        [StringLength(100)]
        public string RepLastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le téléphone du représentant est requis.")]
        public string RepPhone { get; set; } = string.Empty;

        public List<string> LegalDocumentsBase64 { get; set; } = new();
        public List<string> LegalDocumentsNames { get; set; } = new();
        public List<string> RepresentativeDocumentsBase64 { get; set; } = new();
        public List<string> RepresentativeDocumentsNames { get; set; } = new();
    }
}
