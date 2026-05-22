using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Student
{
    public class LinkGuardianRequest
    {
        [StringLength(50)]
        public string? Relationship { get; set; }

        public bool IsPrimaryGuardian { get; set; }
    }
}
