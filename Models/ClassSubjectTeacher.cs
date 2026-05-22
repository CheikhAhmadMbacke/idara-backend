namespace Idara.API.Models
{
    /// <summary>
    /// Affectation : un enseignant donne une matière à une classe.
    /// (Une classe peut avoir plusieurs matières, chaque matière peut avoir un enseignant différent.)
    /// </summary>
    public class ClassSubjectTeacher
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public int ClassId { get; set; }
        public Class Class { get; set; } = null!;

        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;

        public int TeacherId { get; set; }
        public User Teacher { get; set; } = null!;

        public DateTime AssignedAt { get; set; }
    }
}
