using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Operations
{
    public class DaaraObjectiveStepDto
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public bool IsDone { get; set; }
        public DateTime? DoneAt { get; set; }
        public int SortOrder { get; set; }
    }

    public class DaaraObjectiveDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public ObjectiveStatus Status { get; set; }
        public ObjectiveMeasureMode MeasureMode { get; set; }
        public long TargetValue { get; set; }

        /// <summary>
        /// Avancement RÉEL au moment de la lecture. En mode automatique, c'est
        /// l'effectif d'élèves lu à l'instant — jamais une valeur stockée, qui
        /// vieillirait en silence dès la première inscription.
        /// </summary>
        public long CurrentValue { get; set; }

        public string? Unit { get; set; }
        public DateTime? TargetDate { get; set; }
        public EventVisibility Visibility { get; set; }

        public List<DaaraObjectiveStepDto> Steps { get; set; } = new();

        /// <summary>Nombre d'événements du journal rattachés à cet objectif.</summary>
        public int LinkedEventCount { get; set; }

        /// <summary>
        /// Avancement de 0 à 1, calculé PAR LE SERVEUR pour que l'écran, un
        /// futur export et un futur rapport donnent le même chiffre.
        /// </summary>
        public double Progress { get; set; }

        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? AchievedAt { get; set; }
    }

    public class CreateDaaraObjectiveDto : IValidatableObject
    {
        [Required(ErrorMessage = "Le titre est requis.")]
        [StringLength(200, MinimumLength = 1)]
        public string Title { get; set; } = string.Empty;

        [StringLength(4000)]
        public string? Description { get; set; }

        public ObjectiveMeasureMode MeasureMode { get; set; } = ObjectiveMeasureMode.Simple;

        [Range(0, 1_000_000_000_000)]
        public long TargetValue { get; set; }

        [Range(0, 1_000_000_000_000)]
        public long CurrentValue { get; set; }

        [StringLength(30)]
        public string? Unit { get; set; }

        public DateTime? TargetDate { get; set; }

        public EventVisibility Visibility { get; set; } = EventVisibility.School;

        /// <summary>Étapes initiales, dans l'ordre de saisie.</summary>
        public List<string> Steps { get; set; } = new();

        /// <summary>
        /// Une cible à zéro sur un mode chiffré donnerait une barre
        /// d'avancement qui ne bouge jamais et une division par zéro à
        /// calculer : autant le refuser à la saisie plutôt que d'afficher un
        /// objectif inerte que personne ne comprendrait.
        /// </summary>
        public IEnumerable<ValidationResult> Validate(ValidationContext ctx)
        {
            if (MeasureMode != ObjectiveMeasureMode.Simple && TargetValue <= 0)
            {
                yield return new ValidationResult(
                    "Indiquez la cible à atteindre.", new[] { nameof(TargetValue) });
            }
        }
    }

    public class UpdateDaaraObjectiveDto : IValidatableObject
    {
        [Required] public int Id { get; set; }

        [Required(ErrorMessage = "Le titre est requis.")]
        [StringLength(200, MinimumLength = 1)]
        public string Title { get; set; } = string.Empty;

        [StringLength(4000)]
        public string? Description { get; set; }

        public ObjectiveStatus Status { get; set; }
        public ObjectiveMeasureMode MeasureMode { get; set; }

        [Range(0, 1_000_000_000_000)]
        public long TargetValue { get; set; }

        [StringLength(30)]
        public string? Unit { get; set; }

        public DateTime? TargetDate { get; set; }
        public EventVisibility Visibility { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext ctx)
        {
            if (MeasureMode != ObjectiveMeasureMode.Simple && TargetValue <= 0)
            {
                yield return new ValidationResult(
                    "Indiquez la cible à atteindre.", new[] { nameof(TargetValue) });
            }
        }
    }

    /// <summary>
    /// Mise à jour du seul avancement — le geste courant, qui ne doit pas
    /// obliger à rouvrir tout le formulaire.
    /// </summary>
    public class UpdateObjectiveProgressDto
    {
        [Range(0, 1_000_000_000_000)]
        public long CurrentValue { get; set; }
    }

    public class CreateObjectiveStepDto
    {
        [Required(ErrorMessage = "Le libellé de l'étape est requis.")]
        [StringLength(200, MinimumLength = 1)]
        public string Label { get; set; } = string.Empty;
    }

    public class ToggleObjectiveStepDto
    {
        public bool IsDone { get; set; }
    }
}
