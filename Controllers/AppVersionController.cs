using System.Text.Json;
using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.AppVersion;
using Idara.API.DTOs.Common;
using Idara.API.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Distribution + auto-MàJ de l'app Android (hors Play Store).
    /// - <c>GET /api/app-version</c> (public) : dernière version publiée par le CI
    ///   (+ <c>ForcedForMyRole</c> si l'appelant est authentifié) → l'app décide
    ///   modal bloquant / bandeau doux, le site propose le téléchargement.
    /// - <c>GET/PUT /api/app-version/config</c> (SuperAdmin) : quels rôles sont
    ///   forcés à installer la dernière version.
    /// </summary>
    [ApiController]
    [Route("api/app-version")]
    public class AppVersionController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly AppDistributionSettings _dist;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AppVersionController> _logger;

        // Rôles sélectionnables comme « forcés ». On exclut SuperAdmin (il pilote
        // depuis le web et ne doit pas pouvoir se bloquer lui-même).
        private static readonly string[] ForceableRoles =
        {
            UserRoles.SchoolAdmin,
            UserRoles.SchoolStaff,
            UserRoles.Teacher,
            UserRoles.Surveillant,
            UserRoles.SchoolViewer,
            UserRoles.Guardian,
            UserRoles.Donor,
        };

        public AppVersionController(
            AppDbContext db,
            IOptions<AppDistributionSettings> dist,
            IMemoryCache cache,
            ILogger<AppVersionController> logger)
        {
            _db = db;
            _dist = dist.Value;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>Statut de version — public (le site l'appelle anonyme, l'app authentifié).</summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<AppVersionStatusDto>>> Get(CancellationToken ct)
        {
            var manifest = ReadManifest();
            if (manifest == null)
            {
                // Aucun APK publié → rien à proposer (dégradé propre).
                return Ok(ApiResponse<AppVersionStatusDto>.Ok(
                    new AppVersionStatusDto { UpdateAvailable = false }));
            }

            bool forced = false;
            if (User?.Identity?.IsAuthenticated == true)
            {
                var role = User.GetRole();
                if (!string.IsNullOrWhiteSpace(role))
                {
                    var ps = await _db.GetPlatformSettingsAsync(ct);
                    forced = SplitCsv(ps.AndroidForcedUpdateRoles)
                        .Contains(role, StringComparer.OrdinalIgnoreCase);
                }
            }

            return Ok(ApiResponse<AppVersionStatusDto>.Ok(new AppVersionStatusDto
            {
                UpdateAvailable = true,
                LatestVersionCode = manifest.VersionCode,
                LatestVersionName = manifest.VersionName ?? string.Empty,
                ApkUrl = (!string.IsNullOrWhiteSpace(manifest.ApkUrlStable)
                    ? manifest.ApkUrlStable
                    : manifest.ApkUrl) ?? string.Empty,
                Changelog = manifest.Changelog,
                ForcedForMyRole = forced,
                PublishedAt = manifest.PublishedAt,
            }));
        }

        /// <summary>Config des rôles forcés — lecture SuperAdmin.</summary>
        [HttpGet("config")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<ApiResponse<AppUpdateConfigDto>>> GetConfig(CancellationToken ct)
        {
            var ps = await _db.GetPlatformSettingsAsync(ct);
            return Ok(ApiResponse<AppUpdateConfigDto>.Ok(new AppUpdateConfigDto
            {
                ForcedRoles = SplitCsv(ps.AndroidForcedUpdateRoles),
                AvailableRoles = ForceableRoles.ToList(),
            }));
        }

        /// <summary>Config des rôles forcés — écriture SuperAdmin.</summary>
        [HttpPut("config")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<ApiResponse<AppUpdateConfigDto>>> PutConfig(
            [FromBody] UpdateAppConfigDto dto, CancellationToken ct)
        {
            // On ne garde que des rôles connus/forçables (ignore le bruit et
            // empêche de forcer un rôle inexistant ou SuperAdmin).
            var clean = (dto.ForcedRoles ?? new List<string>())
                .Select(r => r?.Trim() ?? string.Empty)
                .Where(r => ForceableRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
                .Select(r => ForceableRoles.First(fr => fr.Equals(r, StringComparison.OrdinalIgnoreCase)))
                .Distinct()
                .ToList();

            var ps = await _db.GetPlatformSettingsAsync(ct);
            ps.AndroidForcedUpdateRoles = string.Join(",", clean);
            ps.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[app-version] Rôles forcés MAJ par SuperAdmin {UserId} : [{Roles}]",
                User.GetUserId(), ps.AndroidForcedUpdateRoles);

            return Ok(ApiResponse<AppUpdateConfigDto>.Ok(new AppUpdateConfigDto
            {
                ForcedRoles = clean,
                AvailableRoles = ForceableRoles.ToList(),
            }, "Rôles mis à jour."));
        }

        // ---------------------------------------------------------------------

        private static List<string> SplitCsv(string? csv) =>
            string.IsNullOrWhiteSpace(csv)
                ? new List<string>()
                : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .ToList();

        /// <summary>
        /// Lit le manifest publié par le CI (mis en cache 60 s pour éviter l'I/O
        /// disque à chaque requête). Renvoie null si absent OU illisible — un
        /// manifest corrompu ne doit JAMAIS faire planter l'endpoint (500).
        /// </summary>
        private AppVersionManifest? ReadManifest()
        {
            return _cache.GetOrCreate("app_version_manifest", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                try
                {
                    var path = _dist.VersionManifestPath;
                    if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                        return null;
                    var json = System.IO.File.ReadAllText(path);
                    return JsonSerializer.Deserialize<AppVersionManifest>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[app-version] lecture manifest échouée ({Path})",
                        _dist.VersionManifestPath);
                    return null;
                }
            });
        }
    }
}
