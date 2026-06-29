using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Operations
{
    /// <summary>Un membre du personnel pointable (enseignant, personnel, surveillant).</summary>
    public class StaffMemberDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }

        /// <summary>Fonction libre (« Cuisinière »…) pour distinguer le personnel sans appli.</summary>
        public string? JobTitle { get; set; }
    }

    public class StaffAttendanceDto
    {
        public int Id { get; set; }
        public int StaffId { get; set; }
        public string StaffName { get; set; } = string.Empty;
        public string StaffRole { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public AttendanceStatus Status { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>Pointage du personnel en lot pour une journée.</summary>
    public class StaffAttendanceBulkDto : IValidatableObject
    {
        [Required] public DateTime Date { get; set; }
        [Required] public List<StaffAttendanceEntryDto> Entries { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Entries == null || Entries.Count == 0)
                yield return new ValidationResult(
                    "La liste 'Entries' ne peut pas être vide.",
                    new[] { nameof(Entries) });
        }
    }

    public class StaffAttendanceEntryDto
    {
        [Required] public int StaffId { get; set; }
        public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;
        [StringLength(500)] public string? Reason { get; set; }
    }
}
