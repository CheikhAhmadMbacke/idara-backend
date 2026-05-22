using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>Pointage journalier (1 entrée par jour par élève).</summary>
    public class Attendance
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        /// <summary>Le jour concerné (heure ignorée).</summary>
        public DateTime Date { get; set; }

        public AttendanceStatus Status { get; set; }
        public string? Reason { get; set; }

        public int RecordedById { get; set; }
        public DateTime RecordedAt { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public int? DeletedById { get; set; }
    }
}
