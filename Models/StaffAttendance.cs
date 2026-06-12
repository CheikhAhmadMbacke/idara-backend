using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Pointage journalier du personnel et des enseignants (1 entrée par jour
    /// par membre du personnel). Pendant de <see cref="Attendance"/> côté élèves,
    /// introduit pour le rôle Surveillant. Réutilise <see cref="AttendanceStatus"/>.
    /// </summary>
    public class StaffAttendance
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        /// <summary>FK vers le User pointé (Teacher / SchoolStaff / Surveillant).</summary>
        public int StaffId { get; set; }
        public User Staff { get; set; } = null!;

        /// <summary>Le jour concerné (heure ignorée, tronquée à minuit UTC).</summary>
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
