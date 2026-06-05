using Idara.API.Enums;

namespace Idara.API.DTOs.School
{
    /// <summary>
    /// Utilisateur rattaché à une école pour la gestion côté SchoolAdmin
    /// (enseignants, personnel, et parents liés via leurs enfants). Pour les
    /// parents, <see cref="Children"/> liste les enfants liés (pour la
    /// dissociation ciblée).
    /// </summary>
    public class SchoolUserDto
    {
        public int Id { get; set; }
        public string? FullName { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public AccountStatus AccountStatus { get; set; }
        public string? PhoneNumber { get; set; }
        public List<SchoolUserChildDto> Children { get; set; } = new();
    }

    public class SchoolUserChildDto
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string? Relationship { get; set; }
    }
}
