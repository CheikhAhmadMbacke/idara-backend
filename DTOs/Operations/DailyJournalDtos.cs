using System.ComponentModel.DataAnnotations;
using Idara.API.Common.Extensions;

namespace Idara.API.DTOs.Operations
{
    public class DailyJournalEntryDto
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public int TeacherId { get; set; }
        public string TeacherName { get; set; } = string.Empty;
        public int? SubjectId { get; set; }
        public string? SubjectName { get; set; }
        public DateTime Date { get; set; }
        public string LearnedToday { get; set; } = string.Empty;
        public int? BehaviorScore { get; set; }
        public int? EffortScore { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// Ce rapport est-il encore modifiable par CELUI qui le consulte ?
        /// Calculé par le serveur (§151) : le recalculer dans l'application
        /// laisserait un téléphone à l'heure fausse verrouiller ou
        /// déverrouiller à tort. Toujours vrai pour la direction.
        /// </summary>
        public bool Editable { get; set; } = true;

        /// <summary>Durée du verrou, pour que l'application puisse l'expliquer
        /// sans la coder en dur.</summary>
        public int EditWindowHours { get; set; } = EditWindow.Hours;
    }

    public class DailyJournalEntryCreateDto
    {
        [Required] public int StudentId { get; set; }
        public int? SubjectId { get; set; }
        [Required] public DateTime Date { get; set; }
        [Required, StringLength(2000, MinimumLength = 1)]
        public string LearnedToday { get; set; } = string.Empty;
        // Échelle 1-3 (1=Mauvais, 2=Moyen, 3=Bien). Validation laissée à 1-5
        // (tolérante) pour ne pas rejeter d'éventuelles anciennes valeurs
        // pendant la fenêtre de déploiement — le client n'envoie plus que 1-3,
        // l'affichage mappe toute valeur >2 sur "Bien".
        [Range(1, 5)] public int? BehaviorScore { get; set; }
        [Range(1, 5)] public int? EffortScore { get; set; }
    }

    public class DailyJournalEntryUpdateDto : DailyJournalEntryCreateDto
    {
        [Required] public int Id { get; set; }
    }

    /// <summary>Saisie en lot d'un rapport pour toute une classe.</summary>
    public class DailyJournalBulkDto : IValidatableObject
    {
        [Required] public DateTime Date { get; set; }
        public int? SubjectId { get; set; }
        public int? ClassId { get; set; }

        /// <summary>
        /// Si true, les entrées dont tous les champs (LearnedToday + scores)
        /// sont vides seront SUPPRIMÉES. Sinon (défaut), elles sont ignorées.
        /// </summary>
        public bool DeleteEmpty { get; set; } = false;

        [Required] public List<DailyJournalBulkEntryDto> Entries { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Entries == null || Entries.Count == 0)
                yield return new ValidationResult(
                    "La liste 'Entries' ne peut pas être vide.",
                    new[] { nameof(Entries) });
        }
    }

    public class DailyJournalBulkEntryDto
    {
        [Required] public int StudentId { get; set; }
        [StringLength(2000)] public string? LearnedToday { get; set; }
        // Échelle 1-3 (1=Mauvais, 2=Moyen, 3=Bien). Validation laissée à 1-5
        // (tolérante) pour ne pas rejeter d'éventuelles anciennes valeurs
        // pendant la fenêtre de déploiement — le client n'envoie plus que 1-3,
        // l'affichage mappe toute valeur >2 sur "Bien".
        [Range(1, 5)] public int? BehaviorScore { get; set; }
        [Range(1, 5)] public int? EffortScore { get; set; }
    }
}
