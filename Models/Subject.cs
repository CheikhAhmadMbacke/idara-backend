using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Matière enseignée par l'école. Couvre Coran (suivi spécifique),
    /// matières religieuses (Fiqh, Hadith, Arabe...) et matières générales.
    /// </summary>
    public class Subject
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        public string Name { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public SubjectKind Kind { get; set; }
        public double DefaultCoefficient { get; set; } = 1.0;
        public double DefaultMaxValue { get; set; } = 20.0;
        public int OrderIndex { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
