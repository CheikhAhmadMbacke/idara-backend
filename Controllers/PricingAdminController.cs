using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Subscription;
using Idara.API.Models;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Édition de la page publique des tarifs (SuperAdmin) : contenu éditorial,
    /// questions fréquentes, et avantages de chaque plan. Tout ce qui apparaît
    /// sur /plans se modifie ici — jamais dans le code.
    /// </summary>
    [ApiController]
    [Route("api/pricing-page")]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    public class PricingAdminController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IPricingPageService _pricing;
        private readonly ILogger<PricingAdminController> _logger;

        public PricingAdminController(
            AppDbContext context, IPricingPageService pricing, ILogger<PricingAdminController> logger)
        {
            _context = context;
            _pricing = pricing;
            _logger = logger;
        }

        // ----- Contenu éditorial -----

        [HttpGet("content")]
        public async Task<ActionResult<ApiResponse<PricingContentDto>>> GetContent(CancellationToken ct)
        {
            var c = await _pricing.GetOrCreateContentAsync(ct);
            return Ok(ApiResponse<PricingContentDto>.Ok(PricingPageService.MapContent(c)));
        }

        [HttpPut("content")]
        public async Task<ActionResult<ApiResponse<PricingContentDto>>> UpdateContent(
            [FromBody] UpdatePricingContentDto dto, CancellationToken ct)
        {
            var c = await _pricing.GetOrCreateContentAsync(ct);
            c.HeroTitle = dto.HeroTitle.Trim();
            c.HeroTitleAr = Norm(dto.HeroTitleAr);
            c.HeroSubtitle = dto.HeroSubtitle?.Trim() ?? string.Empty;
            c.HeroSubtitleAr = Norm(dto.HeroSubtitleAr);
            c.Intro = Norm(dto.Intro);
            c.IntroAr = Norm(dto.IntroAr);
            c.WhatsappNumber = Norm(dto.WhatsappNumber);
            c.ContactEmail = Norm(dto.ContactEmail)?.ToLowerInvariant();
            c.FooterNote = Norm(dto.FooterNote);
            c.FooterNoteAr = Norm(dto.FooterNoteAr);
            c.IsPublished = dto.IsPublished;
            c.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("[pricing] Contenu de la page tarifs mis à jour.");
            return Ok(ApiResponse<PricingContentDto>.Ok(
                PricingPageService.MapContent(c), "Page mise à jour."));
        }

        // ----- Questions fréquentes -----

        [HttpGet("faqs")]
        public async Task<ActionResult<ApiResponse<List<PricingFaqDto>>>> GetFaqs(CancellationToken ct)
        {
            // Y compris les masquées : c'est l'écran d'administration.
            var faqs = await _context.PricingFaqs
                .OrderBy(f => f.DisplayOrder).ThenBy(f => f.Id)
                .ToListAsync(ct);
            return Ok(ApiResponse<List<PricingFaqDto>>.Ok(
                faqs.Select(PricingPageService.MapFaq).ToList()));
        }

        [HttpPost("faqs")]
        public async Task<ActionResult<ApiResponse<PricingFaqDto>>> CreateFaq(
            [FromBody] UpsertPricingFaqDto dto, CancellationToken ct)
        {
            var f = new PricingFaq
            {
                Question = dto.Question.Trim(),
                QuestionAr = Norm(dto.QuestionAr),
                Answer = dto.Answer.Trim(),
                AnswerAr = Norm(dto.AnswerAr),
                DisplayOrder = dto.DisplayOrder,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };
            _context.PricingFaqs.Add(f);
            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<PricingFaqDto>.Ok(PricingPageService.MapFaq(f), "Question ajoutée."));
        }

        [HttpPut("faqs/{id:int}")]
        public async Task<ActionResult<ApiResponse<PricingFaqDto>>> UpdateFaq(
            int id, [FromBody] UpsertPricingFaqDto dto, CancellationToken ct)
        {
            var f = await _context.PricingFaqs.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f == null) return NotFound(ApiResponse<PricingFaqDto>.Fail("Question introuvable."));

            f.Question = dto.Question.Trim();
            f.QuestionAr = Norm(dto.QuestionAr);
            f.Answer = dto.Answer.Trim();
            f.AnswerAr = Norm(dto.AnswerAr);
            f.DisplayOrder = dto.DisplayOrder;
            f.IsActive = dto.IsActive;
            f.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<PricingFaqDto>.Ok(PricingPageService.MapFaq(f), "Question mise à jour."));
        }

        /// <summary>
        /// Suppression DÉFINITIVE : une question n'a ni valeur comptable ni
        /// historique à préserver. Pour la retirer temporairement, il y a
        /// <c>IsActive</c>.
        /// </summary>
        [HttpDelete("faqs/{id:int}")]
        public async Task<ActionResult<ApiResponse<bool>>> DeleteFaq(int id, CancellationToken ct)
        {
            var f = await _context.PricingFaqs.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f == null) return NotFound(ApiResponse<bool>.Fail("Question introuvable."));
            _context.PricingFaqs.Remove(f);
            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<bool>.Ok(true, "Question supprimée."));
        }

        // ----- Avantages d'un plan -----

        [HttpPost("plans/{planId:int}/features")]
        public async Task<ActionResult<ApiResponse<PlanFeatureDto>>> AddFeature(
            int planId, [FromBody] UpsertPlanFeatureDto dto, CancellationToken ct)
        {
            var exists = await _context.SubscriptionPlans.AnyAsync(p => p.Id == planId, ct);
            if (!exists) return NotFound(ApiResponse<PlanFeatureDto>.Fail("Plan introuvable."));

            var f = new SubscriptionPlanFeature
            {
                PlanId = planId,
                Label = dto.Label.Trim(),
                LabelAr = Norm(dto.LabelAr),
                Included = dto.Included,
                DisplayOrder = dto.DisplayOrder,
                CreatedAt = DateTime.UtcNow
            };
            _context.SubscriptionPlanFeatures.Add(f);
            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<PlanFeatureDto>.Ok(MapFeature(f), "Avantage ajouté."));
        }

        [HttpPut("features/{id:int}")]
        public async Task<ActionResult<ApiResponse<PlanFeatureDto>>> UpdateFeature(
            int id, [FromBody] UpsertPlanFeatureDto dto, CancellationToken ct)
        {
            var f = await _context.SubscriptionPlanFeatures.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f == null) return NotFound(ApiResponse<PlanFeatureDto>.Fail("Avantage introuvable."));

            f.Label = dto.Label.Trim();
            f.LabelAr = Norm(dto.LabelAr);
            f.Included = dto.Included;
            f.DisplayOrder = dto.DisplayOrder;
            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<PlanFeatureDto>.Ok(MapFeature(f), "Avantage mis à jour."));
        }

        [HttpDelete("features/{id:int}")]
        public async Task<ActionResult<ApiResponse<bool>>> DeleteFeature(int id, CancellationToken ct)
        {
            var f = await _context.SubscriptionPlanFeatures.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f == null) return NotFound(ApiResponse<bool>.Fail("Avantage introuvable."));
            _context.SubscriptionPlanFeatures.Remove(f);
            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<bool>.Ok(true, "Avantage supprimé."));
        }

        private static PlanFeatureDto MapFeature(SubscriptionPlanFeature f) => new()
        {
            Id = f.Id,
            Label = f.Label,
            LabelAr = f.LabelAr,
            Included = f.Included,
            DisplayOrder = f.DisplayOrder
        };

        /// <summary>Chaîne vide = champ effacé → null en base (pas de « " " »).</summary>
        private static string? Norm(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
