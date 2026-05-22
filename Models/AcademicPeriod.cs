namespace Idara.API.Models
{
    /// <summary>
    /// Période d'évaluation au sein d'une année scolaire (trimestre, semestre, mois...).
    /// L'école choisit librement le découpage : nom + dates + ordre.
    /// </summary>
    public class AcademicPeriod
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public int AcademicYearId { get; set; }
        public AcademicYear AcademicYear { get; set; } = null!;

        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int OrderIndex { get; set; }
        public bool IsCurrent { get; set; }
    }
}
