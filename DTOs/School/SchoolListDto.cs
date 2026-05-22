using Idara.API.Enums;

namespace Idara.API.DTOs.School
{
    public class SchoolListDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public KycStatus KycStatus { get; set; }
        public AccountStatus AccountStatus { get; set; }
        public DateTime CreatedAt { get; set; }
        public string RepresentativeName { get; set; } = string.Empty;
    }
}
