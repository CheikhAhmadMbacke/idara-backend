namespace Idara.API.DTOs.School
{
    public class SchoolStatsDto
    {
        public int TotalStudents { get; set; }
        public int TotalTeachers { get; set; }
        public int TotalGuardians { get; set; }
        public int TotalClasses { get; set; }
        public int TotalSubjects { get; set; }
        public string? CurrentAcademicYearName { get; set; }
        public string? CurrentAcademicPeriodName { get; set; }
    }
}
