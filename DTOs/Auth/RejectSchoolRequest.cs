using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Auth
{
    public class RejectSchoolRequest
    {
        [Required(ErrorMessage = "Le motif de rejet est requis.")]
        [StringLength(1000, MinimumLength = 5)]
        public string RejectionReason { get; set; } = string.Empty;
    }
}
