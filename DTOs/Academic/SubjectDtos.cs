using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Academic
{
    public class SubjectDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public SubjectKind Kind { get; set; }
        public double DefaultCoefficient { get; set; }
        public double DefaultMaxValue { get; set; }
        public int OrderIndex { get; set; }
        public bool IsActive { get; set; }
    }

    public class SubjectCreateDto
    {
        [Required, StringLength(100, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        [StringLength(100)]
        public string? NameAr { get; set; }

        public SubjectKind Kind { get; set; } = SubjectKind.General;

        [Range(0.1, 100)]
        public double DefaultCoefficient { get; set; } = 1.0;

        [Range(1, 100)]
        public double DefaultMaxValue { get; set; } = 20.0;

        public int OrderIndex { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class SubjectUpdateDto : SubjectCreateDto
    {
        [Required] public int Id { get; set; }
    }
}
