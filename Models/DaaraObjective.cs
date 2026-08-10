using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Un objectif du daara : ce que le directeur notait dans son carnet à côté
    /// des événements (« augmenter le mur », « atteindre 200 élèves »,
    /// « acheter 30 tablettes »).
    ///
    /// Distinct d'un <see cref="DaaraEvent"/> : un événement est daté et révolu,
    /// un objectif a une direction et une progression. Les fondre donnerait une
    /// liste où l'on ne saurait plus ce qui est fait et ce qui reste à faire.
    ///
    /// Soft-delete comme le journal (§39) : toute lecture DOIT filtrer
    /// <c>!IsDeleted</c>.
    /// </summary>
    public class DaaraObjective
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }

        public ObjectiveStatus Status { get; set; } = ObjectiveStatus.InProgress;

        public ObjectiveMeasureMode MeasureMode { get; set; } = ObjectiveMeasureMode.Simple;

        /// <summary>
        /// Cible à atteindre. Sens selon le mode : nombre d'unités
        /// (<see cref="ObjectiveMeasureMode.Manual"/>), montant en FCFA
        /// (<see cref="ObjectiveMeasureMode.Amount"/>), nombre d'élèves
        /// (<see cref="ObjectiveMeasureMode.StudentCount"/>). Ignorée en mode
        /// simple.
        ///
        /// ⚠️ <c>long</c> et non <c>int</c> : un objectif de collecte se compte
        /// en FCFA, et toute la comptabilité du projet est en <c>long</c> (§55).
        /// </summary>
        public long TargetValue { get; set; }

        /// <summary>
        /// Avancement saisi par l'école. Non utilisé en mode
        /// <see cref="ObjectiveMeasureMode.StudentCount"/>, où la valeur est
        /// LUE à chaque affichage (l'écrire en base la ferait vieillir en
        /// silence dès qu'un élève est inscrit ou retiré).
        /// </summary>
        public long CurrentValue { get; set; }

        /// <summary>Unité affichée en mode manuel : « m », « sacs », « tablettes ».</summary>
        public string? Unit { get; set; }

        /// <summary>Échéance souhaitée (UTC, minuit). Facultative : un objectif de daara n'a pas toujours de date.</summary>
        public DateTime? TargetDate { get; set; }

        /// <summary>
        /// Qui peut lire. Seules <c>Direction</c> et <c>School</c> sont
        /// proposées : il n'existe pas d'écran parent pour les objectifs, et
        /// offrir un niveau « Parents » sans lecteur serait un faux bouton.
        /// </summary>
        public EventVisibility Visibility { get; set; } = EventVisibility.School;

        public ICollection<DaaraObjectiveStep> Steps { get; set; } = new List<DaaraObjectiveStep>();

        public int CreatedById { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        /// <summary>Date à laquelle l'école a marqué l'objectif atteint.</summary>
        public DateTime? AchievedAt { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public int? DeletedById { get; set; }
    }

    /// <summary>
    /// Étape d'un objectif : « acheter le ciment », « payer le maçon ».
    /// C'est la forme sous laquelle ces choses s'écrivent réellement sur un
    /// carnet, et en mode simple ce sont les étapes qui portent l'avancement.
    /// </summary>
    public class DaaraObjectiveStep
    {
        public int Id { get; set; }

        public int DaaraObjectiveId { get; set; }
        public DaaraObjective DaaraObjective { get; set; } = null!;

        public string Label { get; set; } = string.Empty;

        public bool IsDone { get; set; }
        public DateTime? DoneAt { get; set; }

        /// <summary>Rang d'affichage. Les étapes d'un chantier ont un ordre.</summary>
        public int SortOrder { get; set; }
    }
}
