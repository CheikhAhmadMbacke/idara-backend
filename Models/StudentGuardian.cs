namespace Idara.API.Models
{
    public class StudentGuardian
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;
        public int GuardianId { get; set; }
        public User Guardian { get; set; } = null!;
        public string? Relationship { get; set; }
        public bool IsPrimaryGuardian { get; set; }
    }
}
