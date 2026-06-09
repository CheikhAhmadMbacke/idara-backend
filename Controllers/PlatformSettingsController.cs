using Idara.API.Common.Extensions;
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
        private readonly ILogger<PlatformSettingsController> _logger;

        public PlatformSettingsController(AppDbContext context, ILogger<PlatformSettingsController> logger)
        {
            _context = context;
            _logger = logger;
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
            UpdatedAt = s.UpdatedAt
        };
    }
}
