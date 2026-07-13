using System.Text.Json.Serialization;

namespace Idara.API.DTOs.AppVersion
{
    /// <summary>
    /// Statut de version renvoyé à l'app mobile ET au site (public). L'app compare
    /// <see cref="LatestVersionCode"/> à SON versionCode local pour décider s'il y
    /// a une MàJ ; <see cref="ForcedForMyRole"/> (peuplé seulement si l'appelant est
    /// authentifié) dit si le modal doit être BLOQUANT pour son rôle.
    /// </summary>
    public class AppVersionStatusDto
    {
        /// <summary>true si un manifest a été publié par le CI (sinon rien à proposer).</summary>
        public bool UpdateAvailable { get; set; }

        public int LatestVersionCode { get; set; }
        public string LatestVersionName { get; set; } = string.Empty;

        /// <summary>URL stable de l'APK (Idara.apk) — toujours la dernière version.</summary>
        public string ApkUrl { get; set; } = string.Empty;

        public string? Changelog { get; set; }

        /// <summary>
        /// true si le rôle de l'appelant (authentifié) est dans la liste des rôles
        /// forcés (modal bloquant). false pour un appel anonyme (site) ou un rôle
        /// non forcé (bandeau doux côté app).
        /// </summary>
        public bool ForcedForMyRole { get; set; }

        public DateTime? PublishedAt { get; set; }
    }

    /// <summary>Config SuperAdmin : quels rôles sont forcés à installer la dernière version.</summary>
    public class AppUpdateConfigDto
    {
        public List<string> ForcedRoles { get; set; } = new();

        /// <summary>Rôles sélectionnables (informatif, rempli côté GET).</summary>
        public List<string> AvailableRoles { get; set; } = new();
    }

    /// <summary>Corps du PUT SuperAdmin (liste des rôles forcés).</summary>
    public class UpdateAppConfigDto
    {
        public List<string> ForcedRoles { get; set; } = new();
    }

    /// <summary>
    /// Modèle interne de désérialisation du manifest écrit par le CI. Champs en
    /// camelCase côté JSON (lecture insensible à la casse de toute façon).
    /// </summary>
    public class AppVersionManifest
    {
        [JsonPropertyName("versionCode")] public int VersionCode { get; set; }
        [JsonPropertyName("versionName")] public string? VersionName { get; set; }
        [JsonPropertyName("apkUrl")] public string? ApkUrl { get; set; }
        [JsonPropertyName("apkUrlStable")] public string? ApkUrlStable { get; set; }
        [JsonPropertyName("changelog")] public string? Changelog { get; set; }
        [JsonPropertyName("publishedAt")] public DateTime? PublishedAt { get; set; }
    }
}
