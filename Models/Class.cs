namespace Idara.API.Models
{
    public class Class
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        // Niveau libre (ex: "CI", "CP", "CE1", "Halaqa débutants"...)
        public string? Level { get; set; }
        // Capacité maximum (informatif)
        public int? Capacity { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;
        public bool IsDeleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ICollection<Student> Students { get; set; } = new List<Student>();
    }
}
