using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>Note d'un élève dans une matière sur une période.</summary>
    public class Grade
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;

        public int? ClassId { get; set; }
        public Class? Class { get; set; }

        public int AcademicPeriodId { get; set; }
        public AcademicPeriod AcademicPeriod { get; set; } = null!;

        public double Value { get; set; }
        public double MaxValue { get; set; } = 20.0;
        public double Coefficient { get; set; } = 1.0;
        public EvaluationType EvaluationType { get; set; } = EvaluationType.Devoir;
        public DateTime Date { get; set; }
        public string? Comment { get; set; }

        public int RecordedById { get; set; }
        public DateTime RecordedAt { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public int? DeletedById { get; set; }
    }
}
