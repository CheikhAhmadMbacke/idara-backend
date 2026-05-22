using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Une session de travail / évaluation Coran (mémorisation, révision, récitation, tajwid).
    /// Historique : on peut en créer plusieurs par élève.
    /// </summary>
    public class CoranSession
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        public DateTime Date { get; set; }
        public CoranSessionKind Kind { get; set; }

        // Plage travaillée
        public int? FromSurah { get; set; }
        public int? FromVerse { get; set; }
        public int? ToSurah { get; set; }
        public int? ToVerse { get; set; }

        // Notation /5
        public int? RecitationScore { get; set; }
        public int? TajwidScore { get; set; }
        public int? MemorizationScore { get; set; }

        public string? Comment { get; set; }

        public int? TeacherId { get; set; }
        public User? Teacher { get; set; }
    }
}
