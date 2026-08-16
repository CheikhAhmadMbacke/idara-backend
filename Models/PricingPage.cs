namespace Idara.API.Models
{
    /// <summary>
    /// Contenu éditorial de la page publique des tarifs (idara.sn/plans), hors
    /// plans eux-mêmes : titre, accroche, coordonnées de contact, note de bas de
    /// page. Singleton (PK figée à 1, même motif que
    /// <see cref="PlatformSettings"/>).
    ///
    /// Tout est éditable depuis le back-office : un numéro WhatsApp périmé sur
    /// une page de tarifs coûte un prospect, et cela ne doit jamais demander un
    /// déploiement.
    /// </summary>
    public class PricingPageContent
    {
        /// <summary>PK figée à 1 (ValueGeneratedNever), comme PlatformSettings.</summary>
        public int Id { get; set; } = 1;

        public string HeroTitle { get; set; } = "Nos formules";
        public string? HeroTitleAr { get; set; }

        public string HeroSubtitle { get; set; } = string.Empty;
        public string? HeroSubtitleAr { get; set; }

        /// <summary>Paragraphe d'introduction sous le titre (facultatif).</summary>
        public string? Intro { get; set; }
        public string? IntroAr { get; set; }

        /// <summary>Numéro WhatsApp affiché en contact (format libre, ex. +221 76 363 53 27).</summary>
        public string? WhatsappNumber { get; set; }

        public string? ContactEmail { get; set; }

        /// <summary>Mention sous les cartes (essai gratuit, sans engagement…).</summary>
        public string? FooterNote { get; set; }
        public string? FooterNoteAr { get; set; }

        /// <summary>
        /// Page en ligne. À <c>false</c>, <c>/plans</c> répond 404 — utile pour
        /// préparer une refonte tarifaire sans qu'un prospect tombe dessus.
        /// </summary>
        public bool IsPublished { get; set; } = true;

        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Une question / réponse de la page publique des tarifs. Ajoutable,
    /// modifiable, supprimable et ordonnable depuis le back-office.
    /// </summary>
    public class PricingFaq
    {
        public int Id { get; set; }

        public string Question { get; set; } = string.Empty;
        public string? QuestionAr { get; set; }

        public string Answer { get; set; } = string.Empty;
        public string? AnswerAr { get; set; }

        public int DisplayOrder { get; set; }

        /// <summary>Masquer sans supprimer (brouillon, question saisonnière).</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
