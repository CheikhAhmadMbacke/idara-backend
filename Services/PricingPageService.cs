using Idara.API.Data;
using Idara.API.DTOs.Subscription;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    public interface IPricingPageService
    {
        /// <summary>
        /// Contenu public de la page des tarifs. SOURCE UNIQUE de l'endpoint
        /// JSON et de la page HTML : les deux ne peuvent donc pas diverger.
        /// </summary>
        Task<PublicPricingDto> GetPublicAsync(CancellationToken ct = default);

        /// <summary>Contenu éditorial, créé au premier accès s'il n'existe pas.</summary>
        Task<PricingPageContent> GetOrCreateContentAsync(CancellationToken ct = default);
    }

    public class PricingPageService : IPricingPageService
    {
        private readonly AppDbContext _context;
        public PricingPageService(AppDbContext context) => _context = context;

        public async Task<PricingPageContent> GetOrCreateContentAsync(CancellationToken ct = default)
        {
            var content = await _context.PricingPageContents.FirstOrDefaultAsync(c => c.Id == 1, ct);
            if (content != null) return content;

            content = new PricingPageContent
            {
                Id = 1,
                HeroTitle = "Nos formules",
                HeroSubtitle = "Un tarif par taille d'école. Sans engagement, résiliable à tout moment.",
                IsPublished = true
            };
            _context.PricingPageContents.Add(content);
            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Deux requêtes simultanées sur une page publique : la seconde
                // heurte la PK figée. On relit plutôt que d'échouer.
                _context.Entry(content).State = EntityState.Detached;
                content = await _context.PricingPageContents.FirstAsync(c => c.Id == 1, ct);
            }
            return content;
        }

        public async Task<PublicPricingDto> GetPublicAsync(CancellationToken ct = default)
        {
            var content = await GetOrCreateContentAsync(ct);

            // ⚠️ Un deal négocié (IsCustom) n'est JAMAIS listé publiquement :
            // publier un prix consenti à une école le rendrait opposable par
            // toutes les autres. Le filtre est ici, pas dans l'appelant.
            var plans = await _context.SubscriptionPlans
                .Include(p => p.Features)
                .Where(p => p.IsActive && !p.IsCustom && p.IsPubliclyListed)
                .OrderBy(p => p.DisplayOrder)
                .ThenBy(p => p.MonthlyPriceFcfa)
                .ThenBy(p => p.Id)
                .ToListAsync(ct);

            var faqs = await _context.PricingFaqs
                .Where(f => f.IsActive)
                .OrderBy(f => f.DisplayOrder)
                .ThenBy(f => f.Id)
                .ToListAsync(ct);

            return new PublicPricingDto
            {
                Content = MapContent(content),
                Plans = plans.Select(MapPlan).ToList(),
                Faqs = faqs.Select(MapFaq).ToList()
            };
        }

        public static PricingContentDto MapContent(PricingPageContent c) => new()
        {
            HeroTitle = c.HeroTitle,
            HeroTitleAr = c.HeroTitleAr,
            HeroSubtitle = c.HeroSubtitle,
            HeroSubtitleAr = c.HeroSubtitleAr,
            Intro = c.Intro,
            IntroAr = c.IntroAr,
            WhatsappNumber = c.WhatsappNumber,
            ContactEmail = c.ContactEmail,
            FooterNote = c.FooterNote,
            FooterNoteAr = c.FooterNoteAr,
            IsPublished = c.IsPublished
        };

        public static PricingFaqDto MapFaq(PricingFaq f) => new()
        {
            Id = f.Id,
            Question = f.Question,
            QuestionAr = f.QuestionAr,
            Answer = f.Answer,
            AnswerAr = f.AnswerAr,
            DisplayOrder = f.DisplayOrder,
            IsActive = f.IsActive
        };

        public static SubscriptionPlanDto MapPlan(SubscriptionPlan p) => new()
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            NameAr = p.NameAr,
            Tagline = p.Tagline,
            TaglineAr = p.TaglineAr,
            StudentMin = p.StudentMin,
            StudentMax = p.StudentMax,
            MonthlyPriceFcfa = p.MonthlyPriceFcfa,
            AnnualPriceFcfa = p.AnnualPriceFcfa,
            NotificationQuota = p.NotificationQuota,
            IsActive = p.IsActive,
            IsCustom = p.IsCustom,
            DisplayOrder = p.DisplayOrder,
            IsHighlighted = p.IsHighlighted,
            IsPubliclyListed = p.IsPubliclyListed,
            SchoolId = p.SchoolId,
            SchoolName = p.School?.Name,
            Features = p.Features
                .OrderBy(f => f.DisplayOrder).ThenBy(f => f.Id)
                .Select(f => new PlanFeatureDto
                {
                    Id = f.Id,
                    Label = f.Label,
                    LabelAr = f.LabelAr,
                    Included = f.Included,
                    DisplayOrder = f.DisplayOrder
                }).ToList()
        };
    }
}
