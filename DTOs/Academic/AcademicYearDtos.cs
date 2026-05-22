using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Academic
{
    public class AcademicYearDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsCurrent { get; set; }
        public List<AcademicPeriodDto> Periods { get; set; } = new();
    }

    public class AcademicYearCreateDto
    {
        [Required, StringLength(50)]
        public string Name { get; set; } = string.Empty;

        [Required] public DateTime StartDate { get; set; }
        [Required] public DateTime EndDate { get; set; }
        public bool IsCurrent { get; set; }
    }

    public class AcademicYearUpdateDto : AcademicYearCreateDto
    {
        [Required] public int Id { get; set; }
    }

    public class AcademicPeriodDto
    {
        public int Id { get; set; }
        public int AcademicYearId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int OrderIndex { get; set; }
        public bool IsCurrent { get; set; }
    }

    public class AcademicPeriodCreateDto
    {
        [Required] public int AcademicYearId { get; set; }
        [Required, StringLength(50)] public string Name { get; set; } = string.Empty;
        [Required] public DateTime StartDate { get; set; }
        [Required] public DateTime EndDate { get; set; }
        public int OrderIndex { get; set; }
        public bool IsCurrent { get; set; }
    }

    public class AcademicPeriodUpdateDto : AcademicPeriodCreateDto
    {
        [Required] public int Id { get; set; }
    }
}
