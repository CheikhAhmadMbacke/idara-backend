using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.School
{
    /// <summary>
    /// Personnalisation de la carte d'accueil d'un daara, partagée par TOUS ses
    /// utilisateurs (le titre affiché = <see cref="Name"/>).
    /// </summary>
    public class SchoolBrandingDto
    {
        public int SchoolId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public string? WelcomeSubtitle { get; set; }
        public string? CoverColor { get; set; }
        public string? CoverImageUrl { get; set; }
    }

    /// <summary>
    /// Mise à jour du branding par le SchoolAdmin. Champs texte : null = inchangé.
    /// Images : fournir le base64 pour (re)définir ; poser le flag Remove* pour
    /// effacer. Le nom (titre) s'édite via <c>PUT /api/school/my-info</c>.
    /// </summary>
    public class UpdateSchoolBrandingDto
    {
        [StringLength(120, ErrorMessage = "Le sous-titre ne doit pas dépasser 120 caractères.")]
        public string? WelcomeSubtitle { get; set; }

        /// <summary>Couleur hex "#RRGGBB" (ou "" pour revenir au dégradé par défaut). Null = inchangé.</summary>
        [RegularExpression(@"^(#([0-9a-fA-F]{6})|)$", ErrorMessage = "Couleur invalide (format #RRGGBB).")]
        public string? CoverColor { get; set; }

        /// <summary>Nouveau logo (data base64). Ignoré si RemoveLogo = true.</summary>
        public string? LogoBase64 { get; set; }
        public bool RemoveLogo { get; set; }

        /// <summary>Nouvelle image de couverture (data base64). Ignorée si RemoveCoverImage = true.</summary>
        public string? CoverImageBase64 { get; set; }
        public bool RemoveCoverImage { get; set; }
    }
}
