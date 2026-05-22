namespace Idara.API.Models
{
    /// <summary>Bulletin d'un élève pour une période académique.</summary>
    public class ReportCard
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        public int AcademicPeriodId { get; set; }
        public AcademicPeriod AcademicPeriod { get; set; } = null!;

        public int? ClassId { get; set; }
        public Class? Class { get; set; }

        public DateTime GeneratedAt { get; set; }
        public double GeneralAverage { get; set; }
        public int? Rank { get; set; }
        public int? TotalStudents { get; set; }
        public string? Mention { get; set; }
        public string? Appreciation { get; set; }
        public string? FilePath { get; set; }

        public ICollection<ReportCardLine> Lines { get; set; } = new List<ReportCardLine>();
    }

    public class ReportCardLine
    {
        public int Id { get; set; }
        public int ReportCardId { get; set; }
        public ReportCard ReportCard { get; set; } = null!;

        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;

        public string SubjectName { get; set; } = string.Empty;
        public double Average { get; set; }
        public double Coefficient { get; set; }
        public double MaxValue { get; set; } = 20.0;
        public int? RankInClass { get; set; }
        public string? Appreciation { get; set; }
    }
}
