using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Operations
{
    public class AttendanceDto
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public AttendanceStatus Status { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>Pointage en lot pour une journée d'une classe.</summary>
    public class AttendanceBulkDto : IValidatableObject
    {
        [Required] public DateTime Date { get; set; }
        public int? ClassId { get; set; }
        [Required] public List<AttendanceEntryDto> Entries { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Entries == null || Entries.Count == 0)
                yield return new ValidationResult(
                    "La liste 'Entries' ne peut pas être vide.",
                    new[] { nameof(Entries) });
        }
    }

    public class AttendanceEntryDto
    {
        [Required] public int StudentId { get; set; }
        public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;
        [StringLength(500)] public string? Reason { get; set; }
    }

    public class AttendanceQueryDto
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int? ClassId { get; set; }
        public int? StudentId { get; set; }
    }

    public class AttendanceSummaryDto
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public int Present { get; set; }
        public int Absent { get; set; }
        public int Late { get; set; }
        public int Excused { get; set; }
        public int Total => Present + Absent + Late + Excused;
        public double AttendanceRate => Total == 0 ? 0 : (double)(Present + Late + Excused) / Total * 100;
    }
}
