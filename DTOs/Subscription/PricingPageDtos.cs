using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Subscription
{
    /// <summary>Contenu éditorial de la page publique des tarifs.</summary>
    public class PricingContentDto
    {
        public string HeroTitle { get; set; } = string.Empty;
        public string? HeroTitleAr { get; set; }
        public string HeroSubtitle { get; set; } = string.Empty;
        public string? HeroSubtitleAr { get; set; }
        public string? Intro { get; set; }
        public string? IntroAr { get; set; }
        public string? WhatsappNumber { get; set; }
        public string? ContactEmail { get; set; }
        public string? FooterNote { get; set; }
        public string? FooterNoteAr { get; set; }
        public bool IsPublished { get; set; }
    }

    /// <summary>Édition du contenu éditorial (SuperAdmin).</summary>
    public class UpdatePricingContentDto
    {
        [Required, StringLength(120, MinimumLength = 1)]
        public string HeroTitle { get; set; } = string.Empty;

        [StringLength(120)] public string? HeroTitleAr { get; set; }
        [StringLength(300)] public string HeroSubtitle { get; set; } = string.Empty;
        [StringLength(300)] public string? HeroSubtitleAr { get; set; }
        [StringLength(1500)] public string? Intro { get; set; }
        [StringLength(1500)] public string? IntroAr { get; set; }
        [StringLength(40)] public string? WhatsappNumber { get; set; }
        [StringLength(120), EmailAddress] public string? ContactEmail { get; set; }
        [StringLength(400)] public string? FooterNote { get; set; }
        [StringLength(400)] public string? FooterNoteAr { get; set; }
        public bool IsPublished { get; set; } = true;
    }

    public class PricingFaqDto
    {
        public int Id { get; set; }
        public string Question { get; set; } = string.Empty;
        public string? QuestionAr { get; set; }
        public string Answer { get; set; } = string.Empty;
        public string? AnswerAr { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
    }

    public class UpsertPricingFaqDto
    {
        [Required, StringLength(250, MinimumLength = 1)]
        public string Question { get; set; } = string.Empty;

        [StringLength(250)] public string? QuestionAr { get; set; }

        [Required, StringLength(1500, MinimumLength = 1)]
        public string Answer { get; set; } = string.Empty;

        [StringLength(1500)] public string? AnswerAr { get; set; }

        [Range(0, 1000)] public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Charge utile publique de la page des tarifs. Ne contient QUE des plans
    /// publics listés : un deal négocié n'y figure jamais.
    /// </summary>
    public class PublicPricingDto
    {
        public PricingContentDto Content { get; set; } = new();
        public List<SubscriptionPlanDto> Plans { get; set; } = new();
        public List<PricingFaqDto> Faqs { get; set; } = new();
    }
}
