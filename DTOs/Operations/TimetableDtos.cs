using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Operations
{
    public class TimetableSlotDto
    {
        public int Id { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public int SubjectId { get; set; }
        public string SubjectName { get; set; } = string.Empty;
        public int? TeacherId { get; set; }
        public string? TeacherName { get; set; }
        public int DayOfWeek { get; set; }
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public string? Room { get; set; }
    }

    public class TimetableSlotCreateDto
    {
        [Required] public int ClassId { get; set; }
        [Required] public int SubjectId { get; set; }
        public int? TeacherId { get; set; }
        [Range(0, 6)] public int DayOfWeek { get; set; }

        [Required, RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$",
            ErrorMessage = "Format attendu HH:mm (ex: 08:30).")]
        public string StartTime { get; set; } = "08:00";

        [Required, RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$",
            ErrorMessage = "Format attendu HH:mm (ex: 09:30).")]
        public string EndTime { get; set; } = "09:00";

        [StringLength(50)] public string? Room { get; set; }
    }

    public class TimetableSlotUpdateDto : TimetableSlotCreateDto
    {
        [Required] public int Id { get; set; }
    }

    /// <summary>
    /// Duplique l'emploi du temps d'une classe d'un jour source vers un ou
    /// plusieurs jours cibles. Les créneaux en conflit dans un jour cible sont
    /// ignorés (pas d'écrasement). Pratique pour les daara dont plusieurs jours
    /// ont le même emploi du temps.
    /// </summary>
    public class TimetableDuplicateDto : IValidatableObject
    {
        [Required] public int ClassId { get; set; }

        [Range(0, 6)] public int SourceDay { get; set; }

        [Required, MinLength(1, ErrorMessage = "Au moins un jour cible est requis.")]
        public List<int> TargetDays { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            foreach (var d in TargetDays)
            {
                if (d < 0 || d > 6)
                    yield return new ValidationResult(
                        "Les jours cibles doivent être entre 0 et 6.",
                        new[] { nameof(TargetDays) });
            }
            if (TargetDays.Contains(SourceDay))
                yield return new ValidationResult(
                    "Un jour cible ne peut pas être le jour source.",
                    new[] { nameof(TargetDays) });
        }
    }

    /// <summary>Résultat d'une duplication d'emploi du temps.</summary>
    public class TimetableDuplicateResultDto
    {
        public int Copied { get; set; }
        public int SkippedConflicts { get; set; }
        public List<TimetableSlotDto> CreatedSlots { get; set; } = new();
    }
}
