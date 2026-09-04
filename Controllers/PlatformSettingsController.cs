using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Options;
using Microsoft.Extensions.Options;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Réglages globaux de la plateforme (montants minimums + frais %),
    /// éditables uniquement par le SuperAdmin. Une seule ligne singleton —
    /// s'applique à toutes les écoles. Permet d'ajuster la tarification sans
    /// toucher au code source ni redéployer.
    /// </summary>
    [ApiController]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    [Route("api/platform-settings")]
    public class PlatformSettingsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly UploadSettings _uploads;
        private readonly ILogger<PlatformSettingsController> _logger;

        public PlatformSettingsController(
            AppDbContext context,
            IWebHostEnvironment env,
            IOptions<UploadSettings> uploads,
            ILogger<PlatformSettingsController> logger)
        {
            _context = context;
            _env = env;
            _uploads = uploads.Value;
            _logger = logger;
        }

        /// <summary>
        /// `GET /api/platform-settings/landing-image` — l'image d'illustration de
        /// la page d'accueil. **Public** : la landing est vue par des visiteurs
        /// sans compte.
        /// </summary>
        /// <remarks>
        /// Endpoint séparé et volontairement minuscule : le reste des réglages
        /// plateforme (tarifs, plafonds SMS, coupe-circuit) n'a rien à faire
        /// dans une réponse publique.
        /// </remarks>
        [HttpGet("landing-image")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<LandingImageDto>>> GetLandingImage(
            CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            return Ok(ApiResponse<LandingImageDto>.Ok(
                new LandingImageDto { ImageUrl = s.LandingHeroImagePath }));
        }

        /// <summary>
        /// `PUT /api/platform-settings/landing-image` — remplacer l'image
        /// (base64), ou la retirer pour revenir à celle livrée avec l'app.
        /// </summary>
        [HttpPut("landing-image")]
        public async Task<ActionResult<ApiResponse<LandingImageDto>>> SetLandingImage(
            [FromBody] SetLandingImageDto dto, CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            var previous = s.LandingHeroImagePath;

            if (dto.Remove)
            {
                s.LandingHeroImagePath = null;
            }
            else
            {
                var decoded = FileUploadValidator.DecodeAndValidate(
                    dto.ImageBase64 ?? string.Empty,
                    _uploads.MaxPhotoSizeMb, _uploads.AllowedPhotoMimeTypes);
                if (decoded == null)
                    return BadRequest(ApiResponse<LandingImageDto>.Fail("Image invalide ou trop lourde."));

                var folder = Path.Combine(_env.WebRootPath, "uploads", "landing");
                Directory.CreateDirectory(folder);
                var fileName = $"{Guid.NewGuid():N}{decoded.Extension}";
                await System.IO.File.WriteAllBytesAsync(
                    Path.Combine(folder, fileName), decoded.Bytes, ct);
                s.LandingHeroImagePath = $"/uploads/landing/{fileName}";
            }

            await _context.SaveChangesAsync(ct);

            // L'ancienne image part APRÈS l'enregistrement : si celui-ci échoue,
            // on n'a pas détruit celle qui s'affiche encore.
            if (previous != null && previous != s.LandingHeroImagePath)
            {
                try
                {
                    var root = Path.GetFullPath(_env.WebRootPath);
                    var full = Path.GetFullPath(Path.Combine(_env.WebRootPath, previous.TrimStart('/')));
                    if (full.StartsWith(root) && System.IO.File.Exists(full))
                        System.IO.File.Delete(full);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[platform] Ancienne image d'accueil non supprimée (non bloquant)");
                }
            }

            _logger.LogInformation("[platform] Image d'accueil {Action}",
                dto.Remove ? "retirée" : "remplacée");
            return Ok(ApiResponse<LandingImageDto>.Ok(
                new LandingImageDto { ImageUrl = s.LandingHeroImagePath },
                dto.Remove ? "Image d'origine rétablie." : "Image d'accueil remplacée."));
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<PlatformSettingsDto>>> Get(CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            return Ok(ApiResponse<PlatformSettingsDto>.Ok(Map(s)));
        }

        [HttpPut]
        public async Task<ActionResult<ApiResponse<PlatformSettingsDto>>> Update(
            [FromBody] UpdatePlatformSettingsDto dto, CancellationToken ct)
        {
            // Garde-fou métier : le minimum SenePay absolu est de 200 FCFA, on
            // ne laisse pas descendre en dessous (un payin < 200 serait rejeté
            // par SenePay → l'école croirait pouvoir facturer moins).
            if (dto.MinPayinFcfa < 200)
                return BadRequest(ApiResponse<PlatformSettingsDto>.Fail(
                    "Le montant minimum de paiement ne peut pas être inférieur à 200 FCFA (contrainte SenePay)."));

            var s = await _context.GetPlatformSettingsAsync(ct);

            s.MinPayinFcfa = dto.MinPayinFcfa;
            s.MinWithdrawalFcfa = dto.MinWithdrawalFcfa;
            s.ParentFeePercent = dto.ParentFeePercent;
            s.PayoutFeePercent = dto.PayoutFeePercent;
            s.SmsBilingual = dto.SmsBilingual;
            s.SubscriptionEnforcementEnabled = dto.SubscriptionEnforcementEnabled;
            s.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[platform-settings] MAJ par SuperAdmin {UserId} : minPayin={MinPayin}, minWithdraw={MinWithdraw}, parentFee={ParentFee}%, payoutFee={PayoutFee}%",
                User.GetUserId(), s.MinPayinFcfa, s.MinWithdrawalFcfa, s.ParentFeePercent, s.PayoutFeePercent);

            return Ok(ApiResponse<PlatformSettingsDto>.Ok(Map(s), "Réglages mis à jour."));
        }

        private static PlatformSettingsDto Map(Models.PlatformSettings s) => new()
        {
            MinPayinFcfa = s.MinPayinFcfa,
            MinWithdrawalFcfa = s.MinWithdrawalFcfa,
            ParentFeePercent = s.ParentFeePercent,
            PayoutFeePercent = s.PayoutFeePercent,
            SmsBilingual = s.SmsBilingual,
            SubscriptionEnforcementEnabled = s.SubscriptionEnforcementEnabled,
            UpdatedAt = s.UpdatedAt
        };
    }

    /// <summary>L'image d'illustration de la page d'accueil. `null` = celle de l'app.</summary>
    public class LandingImageDto
    {
        public string? ImageUrl { get; set; }
    }

    public class SetLandingImageDto
    {
        /// <summary>Image encodée en base64 (préfixe `data:` accepté).</summary>
        public string? ImageBase64 { get; set; }

        /// <summary>`true` = revenir à l'image livrée avec l'application.</summary>
        public bool Remove { get; set; }
    }
}
