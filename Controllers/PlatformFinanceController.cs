using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Admin;
using Idara.API.DTOs.Common;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Gestion financière plateforme (SuperAdmin uniquement) : réconciliation
    /// R = D + P, soldes par école, enregistrement des retraits manuels. Le
    /// retrait des gains plateforme via API arrive en Phase B.
    /// </summary>
    [ApiController]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    [Route("api/admin")]
    public class PlatformFinanceController : ControllerBase
    {
        private readonly IPlatformFinanceService _finance;
        private readonly AppDbContext _context;
        private readonly ILogger<PlatformFinanceController> _logger;

        public PlatformFinanceController(
            IPlatformFinanceService finance, AppDbContext context,
            ILogger<PlatformFinanceController> logger)
        {
            _finance = finance;
            _context = context;
            _logger = logger;
        }

        /// <summary>Réconciliation complète : réserve SenePay vs dette écoles vs gains plateforme.</summary>
        [HttpGet("reconciliation")]
        public async Task<ActionResult<ApiResponse<ReconciliationDto>>> GetReconciliation(CancellationToken ct)
        {
            var dto = await _finance.ComputeReconciliationAsync(ct);
            return Ok(ApiResponse<ReconciliationDto>.Ok(dto));
        }

        /// <summary>Solde de chaque école (Available + Pending), plus gros d'abord.</summary>
        [HttpGet("schools/balances")]
        public async Task<ActionResult<ApiResponse<List<SchoolBalanceDto>>>> GetSchoolBalances(CancellationToken ct)
        {
            var list = await _finance.GetSchoolBalancesAsync(ct);
            return Ok(ApiResponse<List<SchoolBalanceDto>>.Ok(list));
        }

        /// <summary>
        /// Enregistre un retrait manuel effectué depuis le dashboard marchand
        /// SenePay (hors Idara) → réduit les gains plateforme P, remet la
        /// réconciliation à l'équilibre. Écriture comptable pure (aucun mouvement
        /// SenePay déclenché).
        /// </summary>
        [HttpPost("platform/manual-outflow")]
        public async Task<ActionResult<ApiResponse<object>>> RecordManualOutflow(
            [FromBody] RecordManualOutflowDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var outflow = new PlatformOutflow
            {
                Type = PlatformOutflowType.ManualAdjustment,
                AmountFcfa = dto.AmountFcfa,
                Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
                OccurredAt = (dto.OccurredAt ?? DateTime.UtcNow).ToUtcSafe(),
                CreatedById = userId.Value,
                CreatedAt = DateTime.UtcNow
            };

            _context.PlatformOutflows.Add(outflow);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[finance] Retrait manuel enregistré : {Amount} FCFA (outflow #{Id}, par user {User})",
                dto.AmountFcfa, outflow.Id, userId.Value);

            return Ok(ApiResponse<object>.Ok(new { outflow.Id }, "Retrait manuel enregistré."));
        }
    }
}
