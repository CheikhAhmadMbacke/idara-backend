namespace Idara.API.Options
{
    /// <summary>
    /// Limites côté serveur pour les uploads en base64 (photos et documents).
    /// </summary>
    public class UploadSettings
    {
        public const string SectionName = "Uploads";

        /// <summary>Taille max d'une photo (Mo).</summary>
        public int MaxPhotoSizeMb { get; set; } = 5;

        /// <summary>Taille max d'un document (Mo).</summary>
        public int MaxDocumentSizeMb { get; set; } = 20;

        public string[] AllowedPhotoMimeTypes { get; set; } =
            new[] { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp" };

        public string[] AllowedDocumentMimeTypes { get; set; } =
            new[] { "application/pdf", "image/jpeg", "image/jpg", "image/png", "image/webp" };
    }
}
