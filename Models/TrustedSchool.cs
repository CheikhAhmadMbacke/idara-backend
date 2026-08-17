namespace Idara.API.Models
{
    /// <summary>
    /// École/daara mise en avant dans la section « Ils nous font confiance » de
    /// la landing page. Gérée par le SuperAdmin (CRUD), lue publiquement.
    ///
    /// Deux formes possibles :
    ///  - <b>rattachée</b> (<see cref="SchoolId"/> posé) : le nom et le logo sont
    ///    LUS EN DIRECT sur la fiche du daara à chaque requête. Un daara qui change
    ///    son logo voit la landing suivre. Les champs <see cref="Name"/>,
    ///    <see cref="NameAr"/> et <see cref="LogoUrl"/> ne servent alors que de
    ///    repli si sa fiche est incomplète.
    ///  - <b>manuelle</b> (<see cref="SchoolId"/> null) : partenaire hors Idara,
    ///    saisi à la main comme avant.
    ///
    /// On ne recopie JAMAIS l'identité du daara dans cette table : une copie prise
    /// à l'ajout divergerait en silence de la fiche réelle.
    /// </summary>
    public class TrustedSchool
    {
        public int Id { get; set; }

        /// <summary>Daara Idara dont ce partenaire reprend l'identité. Null = saisie manuelle.</summary>
        public int? SchoolId { get; set; }
        public School? School { get; set; }

        /// <summary>Nom français (saisie manuelle, ou repli d'un daara sans nom français).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Nom arabe (saisie manuelle, ou repli). Null = pas de nom arabe.</summary>
        public string? NameAr { get; set; }

        /// <summary>Chemin relatif du logo (ex. <c>/uploads/partners/xxx.png</c>).</summary>
        public string? LogoUrl { get; set; }

        /// <summary>Ordre d'affichage croissant.</summary>
        public int DisplayOrder { get; set; }

        /// <summary>Masque l'entrée sans la supprimer.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }
    }
}
