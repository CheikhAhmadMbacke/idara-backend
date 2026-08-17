using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Subscription;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Abonnements plateforme : vue/assignation par le SuperAdmin, vue/changement
    /// de plan par l'école. Le changement de plan re-snapshote le prix immédiatement
    /// (il ne sera prélevé qu'à la prochaine échéance NextBillingAt — donc effet
    /// « au prochain renouvellement » sans re-facturer le cycle courant).
    /// </summary>
    [ApiController]
    [Route("api/subscriptions")]
    [Authorize]
    public class SubscriptionsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly SubscriptionBillingJob _billingJob;
        private readonly ILogger<SubscriptionsController> _logger;

        public SubscriptionsController(
            AppDbContext context, SubscriptionBillingJob billingJob, ILogger<SubscriptionsController> logger)
        {
            _context = context;
            _billingJob = billingJob;
            _logger = logger;
        }

        /// <summary>
        /// `POST /api/subscriptions/cron/run` — déclenche manuellement le cycle de
        /// facturation (SuperAdmin). Pour rejouer / tester sans attendre 02:15 UTC.
        /// </summary>
        [HttpPost("cron/run")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<ApiResponse<SubscriptionBillingReport>>> RunBilling(CancellationToken ct)
        {
            var report = await _billingJob.RunOnceAsync(DateTime.UtcNow, ct);
            return Ok(ApiResponse<SubscriptionBillingReport>.Ok(report, "Cycle de facturation exécuté."));
        }

        /// <summary>Liste tous les abonnements (SuperAdmin).</summary>
        [HttpGet]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<ApiResponse<List<SubscriptionDto>>>> GetAll(CancellationToken ct)
        {
            var subs = await _context.Subscriptions
                .Include(s => s.School)
                .Include(s => s.Plan)
                .OrderBy(s => s.SchoolId)
                .Select(s => Map(s))
                .ToListAsync(ct);
            return Ok(ApiResponse<List<SubscriptionDto>>.Ok(subs));
        }

        /// <summary>Assigne / change le plan d'une école (SuperAdmin) — y compris un deal custom.</summary>
        [HttpPost("{schoolId:int}/assign-plan")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<ApiResponse<SubscriptionDto>>> AssignPlan(
            int schoolId, [FromBody] AssignPlanDto dto, CancellationToken ct)
        {
            await _context.EnsureSubscriptionAsync(schoolId, ct);

            var sub = await _context.Subscriptions
                .Include(s => s.School)
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);
            if (sub == null) return NotFound(ApiResponse<SubscriptionDto>.Fail("Abonnement introuvable."));

            var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == dto.PlanId, ct);
            if (plan == null) return NotFound(ApiResponse<SubscriptionDto>.Fail("Plan introuvable."));

            // Un deal custom ne peut être assigné qu'à son école.
            if (plan.IsCustom && plan.SchoolId != schoolId)
                return BadRequest(ApiResponse<SubscriptionDto>.Fail("Ce deal custom appartient à une autre école."));

            ApplyPlan(sub, plan, dto.BillingCycle);
            await _context.SaveChangesAsync(ct);
            await _context.Entry(sub).Reference(s => s.Plan).LoadAsync(ct);

            _logger.LogInformation("[subscription] École {SchoolId} → plan {PlanId} ({Cycle}).", schoolId, plan.Id, dto.BillingCycle);
            return Ok(ApiResponse<SubscriptionDto>.Ok(Map(sub), "Plan assigné."));
        }

        /// <summary>Plans publics actifs (pour l'écran « changer de plan » côté école).</summary>
        [HttpGet("available-plans")]
        [Authorize(Roles = UserRoles.SchoolAdmin + "," + UserRoles.SchoolStaff)]
        public async Task<ActionResult<ApiResponse<List<SubscriptionPlanDto>>>> AvailablePlans(CancellationToken ct)
        {
            var plans = await _context.SubscriptionPlans
                .Where(p => p.IsActive && !p.IsCustom)
                .OrderBy(p => p.MonthlyPriceFcfa)
                .Select(p => new SubscriptionPlanDto
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
                    SchoolId = p.SchoolId
                })
                .ToListAsync(ct);
            return Ok(ApiResponse<List<SubscriptionPlanDto>>.Ok(plans));
        }

        /// <summary>Abonnement de l'école connectée (SchoolAdmin / SchoolStaff).</summary>
        [HttpGet("me")]
        [Authorize(Roles = UserRoles.SchoolAdmin + "," + UserRoles.SchoolStaff)]
        public async Task<ActionResult<ApiResponse<SubscriptionDto>>> GetMine(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return BadRequest(ApiResponse<SubscriptionDto>.Fail("École introuvable."));

            await _context.EnsureSubscriptionAsync(schoolId.Value, ct);
            var sub = await _context.Subscriptions
                .Include(s => s.School).Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId.Value, ct);
            if (sub == null) return NotFound(ApiResponse<SubscriptionDto>.Fail("Abonnement introuvable."));

            return Ok(ApiResponse<SubscriptionDto>.Ok(Map(sub)));
        }

        /// <summary>
        /// Adéquation effectif ↔ plan (pour prévenir l'école quand elle dépasse
        /// le plafond de son plan, sans la bloquer — appelé après ajout d'élève).
        /// </summary>
        [HttpGet("me/capacity")]
        [Authorize(Roles = UserRoles.SchoolAdmin + "," + UserRoles.SchoolStaff)]
        public async Task<ActionResult<ApiResponse<SubscriptionCapacityDto>>> MyCapacity(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return BadRequest(ApiResponse<SubscriptionCapacityDto>.Fail("École introuvable."));

            await _context.EnsureSubscriptionAsync(schoolId.Value, ct);
            var sub = await _context.Subscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId.Value, ct);
            // Enrolled() : un daara ne paie pas pour ses anciens élèves. ⚠️ Doit
            // rester STRICTEMENT identique au comptage de change-plan et à celui
            // du prélèvement (SubscriptionBillingService) — sinon l'avertissement
            // affiché et le palier réellement facturé divergent.
            var studentCount = await _context.Students
                .Where(s => s.SchoolId == schoolId.Value).Enrolled()
                .CountAsync(ct);

            var dto = new SubscriptionCapacityDto { StudentCount = studentCount };
            var plan = sub?.Plan;
            if (plan != null)
            {
                dto.PlanName = plan.Name;
                dto.StudentMin = plan.StudentMin;
                dto.StudentMax = plan.StudentMax;
                dto.PlanIsCustom = plan.IsCustom;
                // Dépassement uniquement sur un plan PUBLIC borné (les deals custom
                // ne sont jamais auto-ajustés — cf. §101).
                if (!plan.IsCustom && plan.StudentMax.HasValue && studentCount > plan.StudentMax.Value)
                {
                    dto.ExceedsCap = true;
                    // Tri déterministe identique à SubscriptionBillingService (prix
                    // croissant, puis plus petite tranche, puis Id) → l'avertissement
                    // de capacité correspond toujours au plan réellement facturé.
                    var publicPlans = (await _context.SubscriptionPlans
                        .Where(p => p.IsActive && !p.IsCustom)
                        .ToListAsync(ct))
                        .OrderBy(p => p.MonthlyPriceFcfa)
                        .ThenBy(p => p.StudentMax ?? int.MaxValue)
                        .ThenBy(p => p.Id)
                        .ToList();
                    var correct = publicPlans.FirstOrDefault(
                        p => !p.StudentMax.HasValue || studentCount <= p.StudentMax.Value);
                    if (correct != null && correct.Id != plan.Id)
                    {
                        dto.SuggestedPlanName = correct.Name;
                        dto.SuggestedPlanMonthlyFcfa = correct.MonthlyPriceFcfa;
                    }
                }
            }
            return Ok(ApiResponse<SubscriptionCapacityDto>.Ok(dto));
        }

        /// <summary>L'école change elle-même de plan (plans PUBLICS actifs uniquement). SchoolAdmin.</summary>
        [HttpPost("me/change-plan")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<SubscriptionDto>>> ChangeMine(
            [FromBody] AssignPlanDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return BadRequest(ApiResponse<SubscriptionDto>.Fail("École introuvable."));

            await _context.EnsureSubscriptionAsync(schoolId.Value, ct);
            var sub = await _context.Subscriptions
                .Include(s => s.School)
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId.Value, ct);
            if (sub == null) return NotFound(ApiResponse<SubscriptionDto>.Fail("Abonnement introuvable."));

            // L'école ne peut choisir qu'un plan public actif (pas un deal custom d'une autre).
            var plan = await _context.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.Id == dto.PlanId && p.IsActive && !p.IsCustom, ct);
            if (plan == null) return NotFound(ApiResponse<SubscriptionDto>.Fail("Plan indisponible."));

            // Garde-fou palier (décision produit 2026-06-11) : une école ne peut pas
            // choisir elle-même un plan dont le plafond d'élèves est dépassé par son
            // effectif réel (anti-downgrade abusif). Le SuperAdmin, lui, reste libre
            // (AssignPlan) pour gérer les exceptions / deals custom.
            // Enrolled() : même comptage que me/capacity et que le prélèvement.
            var studentCount = await _context.Students
                .Where(s => s.SchoolId == schoolId.Value).Enrolled()
                .CountAsync(ct);
            if (plan.StudentMax.HasValue && studentCount > plan.StudentMax.Value)
                return BadRequest(ApiResponse<SubscriptionDto>.Fail(
                    $"Le plan « {plan.Name} » est limité à {plan.StudentMax} élèves, or votre école en compte {studentCount}. Choisissez un plan adapté à votre effectif."));

            ApplyPlan(sub, plan, dto.BillingCycle);
            await _context.SaveChangesAsync(ct);
            await _context.Entry(sub).Reference(s => s.Plan).LoadAsync(ct);

            _logger.LogInformation("[subscription] École {SchoolId} a choisi le plan {PlanId} ({Cycle}).", schoolId.Value, plan.Id, dto.BillingCycle);
            return Ok(ApiResponse<SubscriptionDto>.Ok(Map(sub), "Plan mis à jour. Le nouveau tarif s'applique dès le prochain prélèvement."));
        }

        /// <summary>
        /// Applique un plan à un abonnement : re-snapshote prix + quota. Le
        /// montant ne sera prélevé qu'à NextBillingAt (donc pas de re-facturation
        /// du cycle courant). On NE remet PAS NotificationUsedThisCycle à 0 ici
        /// (c'est le rôle du renouvellement).
        /// </summary>
        private static void ApplyPlan(Subscription sub, SubscriptionPlan plan, BillingCycle cycle)
        {
            sub.PlanId = plan.Id;
            sub.BillingCycle = cycle;
            sub.AmountFcfa = cycle == BillingCycle.Annual ? plan.AnnualPriceFcfa : plan.MonthlyPriceFcfa;
            sub.NotificationQuota = plan.NotificationQuota;
            sub.UpdatedAt = DateTime.UtcNow;
        }

        private static SubscriptionDto Map(Subscription s) => new()
        {
            Id = s.Id,
            SchoolId = s.SchoolId,
            SchoolName = s.School?.Name,
            PlanId = s.PlanId,
            PlanName = s.Plan?.Name,
            BillingCycle = s.BillingCycle,
            Status = s.Status,
            AmountFcfa = s.AmountFcfa,
            NotificationQuota = s.NotificationQuota,
            NotificationUsedThisCycle = s.NotificationUsedThisCycle,
            TrialEndsAt = s.TrialEndsAt,
            NextBillingAt = s.NextBillingAt,
            GracePeriodEndsAt = s.GracePeriodEndsAt,
            ReadOnlyEndsAt = s.ReadOnlyEndsAt,
            ActivatedAt = s.ActivatedAt,
            SuspendedAt = s.SuspendedAt
        };
    }
}
