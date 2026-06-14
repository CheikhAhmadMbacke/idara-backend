using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Subscription;
using Idara.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Gestion des plans d'abonnement plateforme (SuperAdmin uniquement). Les 4
    /// plans publics (Daara/Standard/Pro/Grand) sont seedés ; on peut éditer
    /// leur prix/quota et créer des deals custom par école. Modifier un plan
    /// n'impacte JAMAIS les abos en cours (prix snapshoté), seulement les
    /// nouveaux et les renouvellements.
    /// </summary>
    [ApiController]
    [Route("api/subscription-plans")]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    public class SubscriptionPlansController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SubscriptionPlansController> _logger;

        public SubscriptionPlansController(AppDbContext context, ILogger<SubscriptionPlansController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>Liste tous les plans (publics d'abord, puis deals custom).</summary>
        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<SubscriptionPlanDto>>>> GetAll(CancellationToken ct)
        {
            var plans = await _context.SubscriptionPlans
                .Include(p => p.School)
                .OrderBy(p => p.IsCustom)
                .ThenBy(p => p.MonthlyPriceFcfa)
                .Select(p => Map(p))
                .ToListAsync(ct);

            return Ok(ApiResponse<List<SubscriptionPlanDto>>.Ok(plans));
        }

        /// <summary>Édite un plan existant (public ou custom).</summary>
        [HttpPut("{id:int}")]
        public async Task<ActionResult<ApiResponse<SubscriptionPlanDto>>> Update(
            int id, [FromBody] UpdateSubscriptionPlanDto dto, CancellationToken ct)
        {
            var plan = await _context.SubscriptionPlans.Include(p => p.School)
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            if (plan == null) return NotFound(ApiResponse<SubscriptionPlanDto>.Fail("Plan introuvable."));

            plan.Name = dto.Name.Trim();
            plan.MonthlyPriceFcfa = dto.MonthlyPriceFcfa;
            plan.AnnualPriceFcfa = dto.AnnualPriceFcfa;
            plan.NotificationQuota = dto.NotificationQuota;
            plan.IsActive = dto.IsActive;
            plan.StudentMin = dto.StudentMin;
            plan.StudentMax = dto.StudentMax;
            plan.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("[subscription] Plan {Id} édité par SuperAdmin.", id);
            return Ok(ApiResponse<SubscriptionPlanDto>.Ok(Map(plan), "Plan mis à jour."));
        }

        /// <summary>Crée un plan PUBLIC (choisissable par toutes les écoles).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiResponse<SubscriptionPlanDto>>> Create(
            [FromBody] CreateSubscriptionPlanDto dto, CancellationToken ct)
        {
            // Code unique parmi les plans publics (l'index DB ne couvre que les
            // non-custom). On génère un slug depuis le nom si absent, puis on
            // dé-doublonne en suffixant.
            var baseCode = string.IsNullOrWhiteSpace(dto.Code) ? Slugify(dto.Name) : Slugify(dto.Code);
            if (string.IsNullOrEmpty(baseCode)) baseCode = "plan";

            var existingCodes = await _context.SubscriptionPlans
                .Where(p => !p.IsCustom)
                .Select(p => p.Code)
                .ToListAsync(ct);
            var code = baseCode;
            var i = 2;
            while (existingCodes.Contains(code))
                code = $"{baseCode}-{i++}";

            var plan = new SubscriptionPlan
            {
                Code = code,
                Name = dto.Name.Trim(),
                StudentMin = dto.StudentMin,
                StudentMax = dto.StudentMax,
                MonthlyPriceFcfa = dto.MonthlyPriceFcfa,
                AnnualPriceFcfa = dto.AnnualPriceFcfa,
                NotificationQuota = dto.NotificationQuota,
                IsActive = true,
                IsCustom = false,
                SchoolId = null,
                CreatedAt = DateTime.UtcNow
            };
            _context.SubscriptionPlans.Add(plan);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("[subscription] Plan public « {Code} » créé par SuperAdmin.", code);
            return Ok(ApiResponse<SubscriptionPlanDto>.Ok(Map(plan), "Plan créé."));
        }

        /// <summary>
        /// « Supprime » un plan = soft-delete (IsActive=false). On ne hard-delete
        /// JAMAIS : des Subscription référencent PlanId, et le prix est snapshoté
        /// dans l'abo (un abo en cours continue donc de fonctionner). Désactiver
        /// masque simplement le plan des nouveaux choix.
        /// </summary>
        [HttpDelete("{id:int}")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(int id, CancellationToken ct)
        {
            var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (plan == null) return NotFound(ApiResponse<bool>.Fail("Plan introuvable."));

            if (!plan.IsActive)
                return Ok(ApiResponse<bool>.Ok(true, "Plan déjà désactivé."));

            plan.IsActive = false;
            plan.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("[subscription] Plan {Id} désactivé (soft-delete) par SuperAdmin.", id);
            return Ok(ApiResponse<bool>.Ok(true, "Plan désactivé. Les abonnements en cours ne sont pas affectés."));
        }

        /// <summary>Transforme un texte en slug ASCII minuscule (a-z0-9-).</summary>
        private static string Slugify(string input)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ch in input.Trim().ToLowerInvariant())
            {
                if (ch >= 'a' && ch <= 'z' || ch >= '0' && ch <= '9') sb.Append(ch);
                else if (ch == ' ' || ch == '-' || ch == '_') sb.Append('-');
                // les autres caractères (accents, ponctuation) sont ignorés
            }
            return sb.ToString().Trim('-');
        }

        /// <summary>Crée un deal custom rattaché à une école précise.</summary>
        [HttpPost("custom")]
        public async Task<ActionResult<ApiResponse<SubscriptionPlanDto>>> CreateCustom(
            [FromBody] CreateCustomPlanDto dto, CancellationToken ct)
        {
            var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == dto.SchoolId, ct);
            if (school == null) return NotFound(ApiResponse<SubscriptionPlanDto>.Fail("École introuvable."));

            var plan = new SubscriptionPlan
            {
                Code = $"custom-{dto.SchoolId}",
                Name = dto.Name.Trim(),
                MonthlyPriceFcfa = dto.MonthlyPriceFcfa,
                AnnualPriceFcfa = dto.AnnualPriceFcfa,
                NotificationQuota = dto.NotificationQuota,
                IsActive = true,
                IsCustom = true,
                SchoolId = dto.SchoolId,
                CreatedAt = DateTime.UtcNow
            };
            _context.SubscriptionPlans.Add(plan);
            await _context.SaveChangesAsync(ct);

            await _context.Entry(plan).Reference(p => p.School).LoadAsync(ct);
            _logger.LogInformation("[subscription] Deal custom créé pour École {SchoolId}.", dto.SchoolId);
            return Ok(ApiResponse<SubscriptionPlanDto>.Ok(Map(plan), "Deal custom créé."));
        }

        private static SubscriptionPlanDto Map(SubscriptionPlan p) => new()
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            StudentMin = p.StudentMin,
            StudentMax = p.StudentMax,
            MonthlyPriceFcfa = p.MonthlyPriceFcfa,
            AnnualPriceFcfa = p.AnnualPriceFcfa,
            NotificationQuota = p.NotificationQuota,
            IsActive = p.IsActive,
            IsCustom = p.IsCustom,
            SchoolId = p.SchoolId,
            SchoolName = p.School?.Name
        };
    }
}
