using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.TrustedSchool
{
    public class TrustedSchoolDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
    }

    public class CreateTrustedSchoolDto
    {
        [Required(ErrorMessage = "Le nom est requis.")]
        [StringLength(150, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Logo en base64 (data URI ou brut). Facultatif.</summary>
        public string? LogoBase64 { get; set; }

        public int DisplayOrder { get; set; }
    }

    public class UpdateTrustedSchoolDto
    {
        [StringLength(150, MinimumLength = 2)]
        public string? Name { get; set; }

        /// <summary>Nouveau logo en base64. Null = inchangé.</summary>
        public string? LogoBase64 { get; set; }

        public int? DisplayOrder { get; set; }
        public bool? IsActive { get; set; }
    }
}
