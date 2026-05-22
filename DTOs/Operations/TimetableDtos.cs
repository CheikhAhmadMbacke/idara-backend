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
}
