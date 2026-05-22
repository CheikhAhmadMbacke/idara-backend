namespace Idara.API.DTOs.Student
{
    public class GuardianResponseDto
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Relationship { get; set; }
        public bool IsPrimaryGuardian { get; set; }
    }
}
