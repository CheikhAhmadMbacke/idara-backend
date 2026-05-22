namespace Idara.API.Models
{
    /// <summary>Année scolaire de l'école (ex: 2025-2026).</summary>
    public class AcademicYear
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public School School { get; set; } = null!;
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsCurrent { get; set; }
        public DateTime CreatedAt { get; set; }

        public ICollection<AcademicPeriod> Periods { get; set; } = new List<AcademicPeriod>();
    }
}
