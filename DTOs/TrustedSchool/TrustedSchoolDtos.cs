using System.ComponentModel.DataAnnotations;
using Idara.API.Common.Validation;

namespace Idara.API.DTOs.TrustedSchool
{
    public class TrustedSchoolDto
    {
        public int Id { get; set; }

        /// <summary>Nom français résolu (fiche du daara si rattaché, sinon saisie manuelle).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Nom arabe résolu. Null = ce partenaire n'a pas de nom arabe.</summary>
        public string? NameAr { get; set; }

        public string? LogoUrl { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }

        /// <summary>Daara rattaché, null si saisie manuelle. Exposé au back-office uniquement.</summary>
        public int? SchoolId { get; set; }

        /// <summary>Vrai si le nom et le logo viennent de la fiche du daara (non éditables ici).</summary>
        public bool IsLinked { get; set; }
    }

    /// <summary>Daara Idara proposable comme partenaire (back-office).</summary>
    public class TrustedSchoolCandidateDto
    {
        public int SchoolId { get; set; }
        public string? Name { get; set; }
        public string? NameAr { get; set; }
        public string? LogoUrl { get; set; }
        public int StudentCount { get; set; }
    }

    /// <summary>
    /// Ajout d'un partenaire. Exactement l'un des deux modes :
    ///  - <see cref="SchoolId"/> : rattachement à un daara Idara (nom et logo récupérés) ;
    ///  - <see cref="Name"/> (+ logo facultatif) : saisie manuelle.
    /// </summary>
    public class CreateTrustedSchoolDto : IValidatableObject
    {
        /// <summary>Daara Idara à rattacher. Renseigné = les autres champs d'identité sont ignorés.</summary>
        public int? SchoolId { get; set; }

        [OptionalStringLength(150, MinimumLength = 2)]
        public string? Name { get; set; }

        [OptionalStringLength(150, MinimumLength = 2)]
        public string? NameAr { get; set; }

        /// <summary>Logo en base64 (data URI ou brut). Facultatif, saisie manuelle uniquement.</summary>
        public string? LogoBase64 { get; set; }

        public int DisplayOrder { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var manual = !string.IsNullOrWhiteSpace(Name);

            if (SchoolId.HasValue && manual)
            {
                yield return new ValidationResult(
                    "Choisissez un daara OU saisissez un nom, pas les deux.",
                    new[] { nameof(SchoolId), nameof(Name) });
            }
            else if (!SchoolId.HasValue && !manual)
            {
                yield return new ValidationResult(
                    "Choisissez un daara Idara ou saisissez le nom de l'école.",
                    new[] { nameof(SchoolId), nameof(Name) });
            }
        }
    }

    public class UpdateTrustedSchoolDto
    {
        /// <summary>Ignoré sur un partenaire rattaché (le nom vient de la fiche du daara).</summary>
        [OptionalStringLength(150, MinimumLength = 2)]
        public string? Name { get; set; }

        /// <summary>Ignoré sur un partenaire rattaché.</summary>
        [OptionalStringLength(150, MinimumLength = 2)]
        public string? NameAr { get; set; }

        /// <summary>Nouveau logo en base64. Null = inchangé. Ignoré si rattaché.</summary>
        public string? LogoBase64 { get; set; }

        public int? DisplayOrder { get; set; }
        public bool? IsActive { get; set; }
    }
}
