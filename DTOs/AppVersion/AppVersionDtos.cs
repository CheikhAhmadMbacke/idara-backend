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

        /// <summary>
        /// URL stable de l'APK UNIVERSEL (Idara.apk) — toujours la dernière version.
        /// Conservée telle quelle : c'est ce que lisent les apps déjà installées, et
        /// c'est le seul choix possible depuis un navigateur (qui ignore
        /// l'architecture du téléphone).
        /// </summary>
        public string ApkUrl { get; set; } = string.Empty;

        /// <summary>
        /// APK dédié à une architecture (2026-07-28). L'APK universel embarque le
        /// code natif des trois architectures : environ 52 Mo sur 75 sont
        /// téléchargés pour rien à chaque installation et à chaque mise à jour
        /// automatique. Champs ADDITIFS : une app plus ancienne les ignore et
        /// continue d'utiliser <see cref="ApkUrl"/>.
        /// </summary>
        public string ApkUrlArm64 { get; set; } = string.Empty;
        public string ApkUrlArm32 { get; set; } = string.Empty;

        /// <summary>
        /// <c>versionCode</c> PROPRE à chaque APK par architecture.
        ///
        /// <para>Flutter décale le <c>versionCode</c> quand on construit avec
        /// <c>--split-per-abi</c> (+1000 armeabi-v7a, +2000 arm64-v8a). Une app
        /// arm64 porte donc 102014 là où la version publiée est 100014 : comparée
        /// au code de base du build suivant (100015), elle se croirait plus
        /// récente et **ne proposerait plus jamais** de mise à jour. L'app compare
        /// donc au code de l'APK qu'elle téléchargerait réellement.</para>
        /// </summary>
        public int LatestVersionCodeArm64 { get; set; }
        public int LatestVersionCodeArm32 { get; set; }

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
        [JsonPropertyName("apkUrlArm64")] public string? ApkUrlArm64 { get; set; }
        [JsonPropertyName("apkUrlArm32")] public string? ApkUrlArm32 { get; set; }
        [JsonPropertyName("versionCodeArm64")] public int? VersionCodeArm64 { get; set; }
        [JsonPropertyName("versionCodeArm32")] public int? VersionCodeArm32 { get; set; }
        [JsonPropertyName("changelog")] public string? Changelog { get; set; }
        [JsonPropertyName("publishedAt")] public DateTime? PublishedAt { get; set; }
    }
}
