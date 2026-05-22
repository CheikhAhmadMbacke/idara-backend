namespace Idara.API.Models
{
    /// <summary>
    /// Créneau d'emploi du temps : pour une classe, un jour, un horaire,
    /// quelle matière est enseignée par quel enseignant.
    /// </summary>
    public class TimetableSlot
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public int ClassId { get; set; }
        public Class Class { get; set; } = null!;

        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;

        public int? TeacherId { get; set; }
        public User? Teacher { get; set; }

        /// <summary>0 = Dimanche, 1 = Lundi, ..., 6 = Samedi.</summary>
        public int DayOfWeek { get; set; }

        /// <summary>Format "HH:mm".</summary>
        public string StartTime { get; set; } = "08:00";
        public string EndTime { get; set; } = "09:00";

        public string? Room { get; set; }
    }
}
