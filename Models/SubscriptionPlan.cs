namespace Idara.API.Models
{
    /// <summary>
    /// Plan tarifaire d'abonnement plateforme. 4 plans publics seedés au
    /// démarrage (Daara / Standard / Pro / Grand, spec §2.2), éditables par le
    /// SuperAdmin. Un plan « custom » (<see cref="IsCustom"/>) est un deal
    /// négocié assigné à une école précise (<see cref="SchoolId"/>).
    ///
    /// IMPORTANT : le prix et le quota sont SNAPSHOTÉS dans la
    /// <see cref="Subscription"/> à la souscription/renouvellement — modifier un
    /// plan ici n'impacte JAMAIS les abonnements en cours, seulement les
    /// nouveaux et les prochains renouvellements (spec §6.1).
    /// </summary>
    public class SubscriptionPlan
    {
        public int Id { get; set; }

        /// <summary>
        /// Code stable pour le seed idempotent et le lookup (« daara »,
        /// « standard », « pro », « grand »). Pour un deal custom : « custom-{id} »
        /// ou tout code unique. Sert à retrouver le plan par défaut.
        /// </summary>
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>Nom en arabe (facultatif) — la page publique est bilingue.</summary>
        public string? NameAr { get; set; }

        /// <summary>Accroche d'une ligne : « Pour les petits daara qui démarrent ».</summary>
        public string? Tagline { get; set; }
        public string? TaglineAr { get; set; }

        /// <summary>Ordre d'affichage sur la page publique (croissant, puis prix).</summary>
        public int DisplayOrder { get; set; }

        /// <summary>Plan mis en avant sur la page publique (« le plus choisi »).</summary>
        public bool IsHighlighted { get; set; }

        /// <summary>
        /// Visible sur la page publique des tarifs. Distinct de
        /// <see cref="IsActive"/> : un plan peut rester souscriptible sans être
        /// affiché publiquement (offre en extinction, plan de transition).
        /// Un deal custom n'est JAMAIS listé publiquement, quoi qu'il vaille —
        /// publier un prix négocié le rendrait opposable par toutes les écoles.
        /// </summary>
        public bool IsPubliclyListed { get; set; } = true;

        /// <summary>Avantages affichés sur la page publique (ordonnables).</summary>
        public List<SubscriptionPlanFeature> Features { get; set; } = new();

        /// <summary>Borne basse d'élèves indicative (informatif, pas contraignant).</summary>
        public int? StudentMin { get; set; }

        /// <summary>Borne haute d'élèves indicative (null = illimité).</summary>
        public int? StudentMax { get; set; }

        public long MonthlyPriceFcfa { get; set; }
        public long AnnualPriceFcfa { get; set; }

        /// <summary>Quota de notifications inclus par cycle (spec §4.2).</summary>
        public int NotificationQuota { get; set; }

        /// <summary>Plan visible/souscriptible. Désactiver masque le plan des nouveaux abos sans casser les abos en cours.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Deal négocié spécifique à une école (non listé publiquement).</summary>
        public bool IsCustom { get; set; } = false;

        /// <summary>École destinataire d'un deal custom (null pour un plan public).</summary>
        public int? SchoolId { get; set; }
        public School? School { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
