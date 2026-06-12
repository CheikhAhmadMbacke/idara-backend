namespace Idara.API.Models
{
    /// <summary>
    /// École/daara mise en avant dans la section « Ils nous font confiance » de
    /// la landing page. Gérée par le SuperAdmin (CRUD), lue publiquement.
    /// </summary>
    public class TrustedSchool
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>Chemin relatif du logo (ex. <c>/uploads/partners/xxx.png</c>).</summary>
        public string? LogoUrl { get; set; }

        /// <summary>Ordre d'affichage croissant.</summary>
        public int DisplayOrder { get; set; }

        /// <summary>Masque l'entrée sans la supprimer.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }
    }
}
