using System.ComponentModel.DataAnnotations;
using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
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
        private readonly ISubscriptionPaymentLinkService _links;
        private readonly ILogger<SubscriptionsController> _logger;

        public SubscriptionsController(
            AppDbContext context, SubscriptionBillingJob billingJob,
            ISubscriptionPaymentLinkService links, ILogger<SubscriptionsController> logger)
        {
            _context = context;
            _billingJob = billingJob;
            _links = links;
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


        // ================================================================
        //  🆘 LE SURSIS — l'issue de secours du SuperAdmin
        // ================================================================

        public class ReprieveDto
        {
            /// <summary>Jusqu'à quand. Obligatoire — un sursis sans fin s'oublie.</summary>
            [Required]
            public DateTime Until { get; set; }

            /// <summary>Motif, relu dans six mois. Obligatoire.</summary>
            [Required, StringLength(300, MinimumLength = 3)]
            public string Reason { get; set; } = string.Empty;
        }

        /// <summary>Durée maximale d'un sursis. Au-delà, ce n'est plus un secours, c'est un oubli.</summary>
        private const int MaxReprieveDays = 30;

        /// <summary>
        /// `POST /api/subscriptions/{schoolId}/reprieve` — rend l'accès à une
        /// école bloquée, temporairement.
        /// </summary>
        /// <remarks>
        /// <para>🔑 <b>Le compte à rebours est GELÉ</b>, pas seulement masqué :
        /// <see cref="Subscription.ReadOnlyEndsAt"/> est repoussé de la durée du
        /// sursis. Une école bloquée depuis deux jours à qui l'on accorde cinq
        /// jours retrouve donc ses cinq jours restants à l'expiration. Sans ce
        /// décalage, le sursis ne ferait que cacher le blocage : l'école
        /// redécouvrirait un accès coupé le jour même où il expire, sans avoir
        /// rien gagné.</para>
        ///
        /// <para>Ne touche NI au statut, NI à la facture : la dette reste due.
        /// Le sursis suspend la punition, pas le paiement.</para>
        /// </remarks>
        [HttpPost("{schoolId:int}/reprieve")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<ApiResponse<SubscriptionDto>>> GrantReprieve(
            int schoolId, [FromBody] ReprieveDto dto, CancellationToken ct)
        {
            var sub = await _context.Subscriptions
                .Include(s => s.School).Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);
            if (sub == null) return NotFound(ApiResponse<SubscriptionDto>.Fail("Abonnement introuvable."));

            var now = DateTime.UtcNow;
            var until = DateTime.SpecifyKind(dto.Until, DateTimeKind.Utc);
            if (until <= now)
                return BadRequest(ApiResponse<SubscriptionDto>.Fail("La date de fin du sursis doit être dans le futur."));
            if (until > now.AddDays(MaxReprieveDays))
                return BadRequest(ApiResponse<SubscriptionDto>.Fail(
                    $"Un sursis ne peut pas dépasser {MaxReprieveDays} jours. Renouvelez-le si la panne dure."));

            ApplyReprieve(sub, until, dto.Reason.Trim(), now);
            await _context.SaveChangesAsync(ct);

            _logger.LogWarning(
                "[subscription-reprieve] École {SchoolId} : sursis jusqu'au {Until:yyyy-MM-dd} par SuperAdmin {AdminId} — {Reason}",
                schoolId, until, User.GetUserId(), dto.Reason);

            return Ok(ApiResponse<SubscriptionDto>.Ok(Map(sub), "Sursis accordé."));
        }

        /// <summary>
        /// `POST /api/subscriptions/reprieve-blocked` — accorde le même sursis à
        /// TOUTES les écoles actuellement bloquées.
        /// </summary>
        /// <remarks>
        /// 🔑 C'est le geste des jours d'incident : une panne du prestataire ne
        /// frappe pas une école, elle les frappe toutes — et depuis que les
        /// échéances tombent le même jour, elles tombent ensemble. Cliquer école
        /// par école au pire moment, c'est en oublier une.
        /// </remarks>
        [HttpPost("reprieve-blocked")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<ApiResponse<int>>> GrantReprieveToBlocked(
            [FromBody] ReprieveDto dto, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var until = DateTime.SpecifyKind(dto.Until, DateTimeKind.Utc);
            if (until <= now)
                return BadRequest(ApiResponse<int>.Fail("La date de fin du sursis doit être dans le futur."));
            if (until > now.AddDays(MaxReprieveDays))
                return BadRequest(ApiResponse<int>.Fail(
                    $"Un sursis ne peut pas dépasser {MaxReprieveDays} jours."));

            var bloquees = await _context.Subscriptions
                .Where(s => s.Status == SubscriptionStatus.ReadOnly
                            || s.Status == SubscriptionStatus.Suspended
                            || s.Status == SubscriptionStatus.PendingPayment)
                .ToListAsync(ct);

            foreach (var sub in bloquees)
                ApplyReprieve(sub, until, dto.Reason.Trim(), now);

            await _context.SaveChangesAsync(ct);

            _logger.LogWarning(
                "[subscription-reprieve] {Count} école(s) sous sursis jusqu'au {Until:yyyy-MM-dd} par SuperAdmin {AdminId} — {Reason}",
                bloquees.Count, until, User.GetUserId(), dto.Reason);

            return Ok(ApiResponse<int>.Ok(bloquees.Count,
                bloquees.Count == 0
                    ? "Aucune école bloquée : rien à faire."
                    : $"{bloquees.Count} école(s) débloquée(s) jusqu'au {until:dd/MM}."));
        }

        /// <summary>`DELETE /api/subscriptions/{schoolId}/reprieve` — referme le sursis tout de suite.</summary>
        [HttpDelete("{schoolId:int}/reprieve")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<ApiResponse<SubscriptionDto>>> RevokeReprieve(
            int schoolId, CancellationToken ct)
        {
            var sub = await _context.Subscriptions
                .Include(s => s.School).Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);
            if (sub == null) return NotFound(ApiResponse<SubscriptionDto>.Fail("Abonnement introuvable."));

            // ⚠️ On ne REPREND PAS les jours qu'on avait décalés : l'école a bien
            // profité de son sursis jusqu'ici. Les lui reprendre reviendrait à la
            // punir deux fois.
            sub.ReprieveUntil = null;
            sub.ReprieveReason = null;
            sub.ReprieveById = null;
            sub.ReprieveAt = null;
            sub.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            _logger.LogWarning("[subscription-reprieve] École {SchoolId} : sursis levé par SuperAdmin {AdminId}",
                schoolId, User.GetUserId());
            return Ok(ApiResponse<SubscriptionDto>.Ok(Map(sub), "Sursis levé."));
        }

        /// <summary>
        /// Pose le sursis et DÉCALE le compte à rebours de sa durée, pour que les
        /// jours accordés ne soient pas volés à l'école.
        /// </summary>
        private void ApplyReprieve(Subscription sub, DateTime until, string reason, DateTime now)
        {
            // La durée réellement offerte part de MAINTENANT, ou de la fin du
            // sursis en cours quand on le prolonge — sinon prolonger décalerait
            // le compteur deux fois pour les mêmes jours.
            var depart = sub.ReprieveUntil is { } encours && encours > now ? encours : now;
            var duree = until - depart;

            if (duree > TimeSpan.Zero && sub.ReadOnlyEndsAt is { } fin)
                sub.ReadOnlyEndsAt = fin.Add(duree);

            sub.ReprieveUntil = until;
            sub.ReprieveReason = reason;
            sub.ReprieveById = User.GetUserId();
            sub.ReprieveAt = now;
            sub.UpdatedAt = now;
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

            var dto = Map(sub);
            // Le lien de paiement accompagne TOUJOURS l'abonnement de l'école :
            // c'est lui que le mur de paiement propose, et il doit exister avant
            // qu'on en ait besoin — pas au moment où l'accès vient d'être coupé.
            var (link, _) = await _links.EnsureAsync(schoolId.Value, ct);
            dto.PaymentLinkUrl = _links.BuildUrl(link.Token);

            return Ok(ApiResponse<SubscriptionDto>.Ok(dto));
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

        public class ActivateNowDto { public int PlanId { get; set; } }

        /// <summary>
        /// `GET /api/subscriptions/me/activate-now/quote?planId=` — ce que coûterait
        /// le passage immédiat, ce qu'il couvre (jusqu'au prochain 8), et si le
        /// solde suffit. L'écran le montre AVANT tout paiement.
        /// </summary>
        [HttpGet("me/activate-now/quote")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<IActionResult> ActivateNowQuote(
            [FromQuery] int planId, [FromServices] ISubscriptionActivationService activation, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return BadRequest(ApiResponse<bool>.Fail("École introuvable."));
            return Ok(ApiResponse<ActivationQuote>.Ok(await activation.QuoteAsync(schoolId.Value, planId, ct)));
        }

        /// <summary>
        /// `POST /api/subscriptions/me/activate-now` — payer un plan MAINTENANT
        /// (fin d'essai anticipée ou montée). Solde si suffisant, sinon Wave.
        /// </summary>
        [HttpPost("me/activate-now")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<IActionResult> ActivateNow(
            [FromBody] ActivateNowDto dto, [FromServices] ISubscriptionActivationService activation, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return BadRequest(ApiResponse<bool>.Fail("École introuvable."));
            var school = await _context.Schools.AsNoTracking().FirstOrDefaultAsync(s => s.Id == schoolId.Value, ct);
            var r = await activation.ActivateAsync(schoolId.Value, dto.PlanId, SchoolDisplayName.From(school).Primary(), ct);
            if (!r.Ok) return BadRequest(ApiResponse<bool>.Fail(r.Error ?? "Le passage au plan n'a pas pu se faire."));
            return Ok(ApiResponse<object>.Ok(new
            {
                status = r.Status,
                paymentId = r.PaymentId,
                redirectUrl = r.RedirectUrl,
            }, r.Status == "Paid" ? "Plan activé." : "Paiement initié."));
        }

        /// <summary>`GET /api/subscriptions/me/activate-now/{paymentId}` — le webhook fait foi, ceci le lit.</summary>
        [HttpGet("me/activate-now/{paymentId:int}")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<IActionResult> ActivateNowStatus(int paymentId, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return BadRequest(ApiResponse<bool>.Fail("École introuvable."));
            var p = await _context.Payments.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == paymentId && x.SchoolId == schoolId.Value
                     && x.Purpose == PaymentPurpose.Subscription && x.SubscriptionPlanId != null, ct);
            if (p == null) return NotFound(ApiResponse<bool>.Fail("Paiement introuvable."));
            return Ok(ApiResponse<object>.Ok(new { status = p.Status.ToString(), failureReason = p.FailureReason }));
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
            SuspendedAt = s.SuspendedAt,
            ReprieveUntil = s.ReprieveUntil,
            ReprieveReason = s.ReprieveReason,
            ReprieveAt = s.ReprieveAt
        };
    }
}
