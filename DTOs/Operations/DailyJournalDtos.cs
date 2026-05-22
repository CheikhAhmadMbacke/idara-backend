using System.ComponentModel.DataAnnotations;

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
    }

    public class DailyJournalEntryCreateDto
    {
        [Required] public int StudentId { get; set; }
        public int? SubjectId { get; set; }
        [Required] public DateTime Date { get; set; }
        [Required, StringLength(2000, MinimumLength = 1)]
        public string LearnedToday { get; set; } = string.Empty;
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
        [Range(1, 5)] public int? BehaviorScore { get; set; }
        [Range(1, 5)] public int? EffortScore { get; set; }
    }
}
