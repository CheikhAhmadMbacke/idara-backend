using System.ComponentModel.DataAnnotations;
using Idara.API.Common.Validation;

namespace Idara.API.DTOs.Auth
{
    /// <summary>
    /// Soumission des informations KYC de l'école (documents transmis en base64).
    /// </summary>
    public class SubmitKycRequest : IValidatableObject
    {
        /// <summary>
        /// Nom en français. Plus obligatoire seul : la règle est « au moins l'un
        /// des deux noms » (cf. <see cref="SchoolNameRule"/>), certains daara
        /// n'ayant de nom officiel qu'en arabe.
        /// </summary>
        [OptionalStringLength(200, MinimumLength = 2, ErrorMessage = "Le nom en français doit faire au moins 2 caractères.")]
        public string? SchoolName { get; set; }

        /// <summary>Nom en arabe. Même règle que <see cref="SchoolName"/>.</summary>
        [OptionalStringLength(200, MinimumLength = 2, ErrorMessage = "Le nom en arabe doit faire au moins 2 caractères.")]
        public string? SchoolNameAr { get; set; }

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

        /// <summary>Le daara doit porter un nom dans au moins une écriture.</summary>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
            SchoolNameRule.Validate(SchoolName, SchoolNameAr, nameof(SchoolName), nameof(SchoolNameAr));
    }
}
