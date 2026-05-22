namespace Idara.API.DTOs.Operations
{
    public class ReportCardDto
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public int AcademicPeriodId { get; set; }
        public string AcademicPeriodName { get; set; } = string.Empty;
        public string? ClassName { get; set; }
        public DateTime GeneratedAt { get; set; }
        public double GeneralAverage { get; set; }
        public int? Rank { get; set; }
        public int? TotalStudents { get; set; }
        public string? Mention { get; set; }
        public string? Appreciation { get; set; }
        public string? FilePath { get; set; }
        public List<ReportCardLineDto> Lines { get; set; } = new();
    }

    public class ReportCardLineDto
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public string SubjectName { get; set; } = string.Empty;
        public double Average { get; set; }
        public double Coefficient { get; set; }
        public double MaxValue { get; set; }
        public int? RankInClass { get; set; }
        public string? Appreciation { get; set; }
    }
}
