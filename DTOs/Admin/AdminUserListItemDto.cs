using Idara.API.Enums;

namespace Idara.API.DTOs.Admin
{
    /// <summary>
    /// Élément de la liste globale des utilisateurs côté SuperAdmin
    /// (GET /api/users). Enrichi du nom de l'école pour le contexte —
    /// les Guardian n'ont pas de SchoolId (SchoolId/SchoolName null).
    /// </summary>
    public class AdminUserListItemDto
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string Role { get; set; } = string.Empty;
        public AccountStatus AccountStatus { get; set; }
        public int? SchoolId { get; set; }
        public string? SchoolName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }
}
