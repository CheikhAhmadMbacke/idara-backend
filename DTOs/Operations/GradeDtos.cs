using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Operations
{
    public class GradeDto
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public int SubjectId { get; set; }
        public string SubjectName { get; set; } = string.Empty;
        public int? ClassId { get; set; }
        public int AcademicPeriodId { get; set; }
        public string AcademicPeriodName { get; set; } = string.Empty;
        public double Value { get; set; }
        public double MaxValue { get; set; }
        public double Coefficient { get; set; }
        public EvaluationType EvaluationType { get; set; }
        public DateTime Date { get; set; }
        public string? Comment { get; set; }
    }

    public class GradeCreateDto : IValidatableObject
    {
        [Required] public int StudentId { get; set; }
        [Required] public int SubjectId { get; set; }
        public int? ClassId { get; set; }
        [Required] public int AcademicPeriodId { get; set; }

        [Range(0, 100)] public double Value { get; set; }
        [Range(1, 100)] public double MaxValue { get; set; } = 20.0;
        [Range(0.1, 100)] public double Coefficient { get; set; } = 1.0;
        public EvaluationType EvaluationType { get; set; } = EvaluationType.Devoir;
        [Required] public DateTime Date { get; set; } = DateTime.UtcNow;
        [StringLength(500)] public string? Comment { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Value > MaxValue)
                yield return new ValidationResult(
                    "La note (Value) ne peut pas dépasser la note maximale (MaxValue).",
                    new[] { nameof(Value), nameof(MaxValue) });
        }
    }

    public class GradeUpdateDto : GradeCreateDto
    {
        [Required] public int Id { get; set; }
    }

    public class StudentGradesSummaryDto
    {
        public int StudentId { get; set; }
        public int AcademicPeriodId { get; set; }
        public List<SubjectAverageDto> Subjects { get; set; } = new();

        /// <summary>Moyenne générale (/20). Null si aucune note enregistrée.</summary>
        public double? GeneralAverage { get; set; }

        /// <summary>True si aucune note pour la période (UI : afficher "—" au lieu de 0).</summary>
        public bool HasNoGrades { get; set; }

        public int? Rank { get; set; }
        public int? TotalStudents { get; set; }
    }

    public class SubjectAverageDto
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; } = string.Empty;
        public double Average { get; set; }
        public double MaxValue { get; set; } = 20.0;
        public double Coefficient { get; set; } = 1.0;
        public int GradeCount { get; set; }
    }
}
