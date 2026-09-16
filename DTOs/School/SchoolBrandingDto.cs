using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.School
{
    /// <summary>
    /// Identité d'un daara telle qu'elle s'affiche sur la carte d'accueil,
    /// partagée par TOUS ses utilisateurs (le titre affiché = <see cref="Name"/>).
    ///
    /// 🎨 <b>La personnalisation se limite au LOGO et à la COULEUR</b> (décision
    /// du 2026-09-16). L'image de couverture et le sous-titre libre ne sont plus
    /// proposés : la carte est devenue une carte claire comme les autres de
    /// l'accueil, et son sous-titre dit l'ANNÉE SCOLAIRE — la seule information
    /// d'identité qui change et que tout le monde a besoin de vérifier.
    /// </summary>
    public class SchoolBrandingDto
    {
        public int SchoolId { get; set; }

        /// <summary>Nom en français (peut être vide si le daara n'a qu'un nom arabe).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Nom en arabe, affiché SOUS le nom français sur la carte d'accueil.</summary>
        public string? NameAr { get; set; }
        public string? LogoUrl { get; set; }

        /// <summary>Couleur hex "#RRGGBB" du pavé qui porte le logo. Null = dégradé vert de la marque.</summary>
        public string? CoverColor { get; set; }

        /// <summary>
        /// Année scolaire en cours (ex. « 2025-2026 »), sous-titre de la carte.
        /// Null tant que le daara n'en a pas ouvert une : la carte affiche alors
        /// le nom seul, sans laisser de trou.
        /// </summary>
        public string? CurrentAcademicYearName { get; set; }

        // ================================================================
        // ⚠️ OBSOLÈTES — servis UNIQUEMENT aux applications antérieures au
        // 2026-09-16, et c'est la seule raison de leur survie ici.
        //
        // Le serveur se déploie avant les téléphones : une application déjà
        // installée continue d'afficher le bandeau coloré avec son image et son
        // sous-titre. Les retirer du jour au lendemain aurait fait disparaître,
        // sans prévenir, la photo de couverture des écoles qui en avaient posé
        // une — pendant les jours, voire les semaines, qui séparent le
        // déploiement de l'API de la livraison de la nouvelle version.
        //
        // La carte refondue les ignore. À supprimer, avec les colonnes, quand
        // plus aucune application antérieure ne sera en circulation.
        // ================================================================

        /// <summary>Obsolète (cf. ci-dessus). Ignoré par la carte refondue.</summary>
        public string? WelcomeSubtitle { get; set; }

        /// <summary>Obsolète (cf. ci-dessus). Ignoré par la carte refondue.</summary>
        public string? CoverImageUrl { get; set; }
    }

    /// <summary>
    /// Mise à jour du branding par le SchoolAdmin : logo et couleur.
    /// Le nom (titre) s'édite via <c>PUT /api/school/my-info</c>.
    /// </summary>
    public class UpdateSchoolBrandingDto
    {
        /// <summary>Couleur hex "#RRGGBB" (ou "" pour revenir au dégradé par défaut). Null = inchangé.</summary>
        [RegularExpression(@"^(#([0-9a-fA-F]{6})|)$", ErrorMessage = "Couleur invalide (format #RRGGBB).")]
        public string? CoverColor { get; set; }

        /// <summary>Nouveau logo (data base64). Ignoré si RemoveLogo = true.</summary>
        public string? LogoBase64 { get; set; }
        public bool RemoveLogo { get; set; }

        // ⚠️ OBSOLÈTES — mêmes raisons que ci-dessus. Une application antérieure
        // au 2026-09-16 propose encore ces deux réglages ; les refuser en
        // silence ferait croire à l'école que sa modification a été prise en
        // compte (§196). Ils restent donc honorés jusqu'à la disparition de ces
        // versions.

        /// <summary>Obsolète : sous-titre libre. Plus proposé par l'application.</summary>
        [StringLength(120, ErrorMessage = "Le sous-titre ne doit pas dépasser 120 caractères.")]
        public string? WelcomeSubtitle { get; set; }

        /// <summary>Obsolète : image de couverture. Plus proposée par l'application.</summary>
        public string? CoverImageBase64 { get; set; }
        public bool RemoveCoverImage { get; set; }
    }
}
