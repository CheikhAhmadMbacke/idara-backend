using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Une ligne du journal du daara : ce que le directeur consignait jusqu'ici
    /// dans un carnet papier (visites, réunions, cérémonies, travaux, dons,
    /// incidents…).
    ///
    /// Soft-delete comme le journal quotidien : une note effacée par erreur doit
    /// pouvoir être retrouvée, et une école ne doit pas hésiter à écrire de peur
    /// de ne pas pouvoir corriger. ⚠️ Toute lecture DOIT filtrer
    /// <c>!IsDeleted</c> (cf. CLAUDE.md §39).
    /// </summary>
    public class DaaraEvent
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        /// <summary>
        /// Jour de l'événement (UTC, minuit). Distinct de <see cref="CreatedAt"/> :
        /// on consigne souvent le lendemain une visite de la veille.
        /// </summary>
        public DateTime Date { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DaaraEventCategory Category { get; set; } = DaaraEventCategory.Other;

        /// <summary>Qui peut lire. Défaut : toute l'équipe de l'école.</summary>
        public EventVisibility Visibility { get; set; } = EventVisibility.School;

        /// <summary>
        /// Objectif que cet événement fait avancer, s'il y en a un : « Don de
        /// 200 000 F » rattaché à « Construire le mur ». C'est ce lien qui rend
        /// le carnet vivant — sur la fiche de l'objectif, on relit tout ce qui
        /// s'est passé pour lui.
        ///
        /// ⚠️ Purement contextuel : il ne fait PAS bouger le compteur de
        /// l'objectif. Un événement ne porte pas de montant, et déduire un
        /// avancement d'un texte libre serait une invention.
        /// </summary>
        public int? DaaraObjectiveId { get; set; }
        public DaaraObjective? DaaraObjective { get; set; }

        public ICollection<DaaraEventPhoto> Photos { get; set; } = new List<DaaraEventPhoto>();

        public int CreatedById { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public int? DeletedById { get; set; }
    }

    /// <summary>
    /// Photo jointe à un événement — ce que le carnet papier ne pouvait pas
    /// faire, et l'une des raisons de l'abandonner.
    /// </summary>
    public class DaaraEventPhoto
    {
        public int Id { get; set; }

        public int DaaraEventId { get; set; }
        public DaaraEvent DaaraEvent { get; set; } = null!;

        /// <summary>Chemin relatif servi par nginx, ex. <c>/uploads/events/xxx.jpg</c>.</summary>
        public string FilePath { get; set; } = string.Empty;

        public string? ContentType { get; set; }
        public long FileSize { get; set; }
        public DateTime UploadedAt { get; set; }
    }
}
