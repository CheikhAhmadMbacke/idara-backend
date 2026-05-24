using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Payment
{
    public class StudentFeeOverrideDto
    {
        public int StudentId { get; set; }
        public int SchoolId { get; set; }
        public long AmountFcfa { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class UpsertStudentFeeOverrideDto
    {
        [Range(0, 100_000_000, ErrorMessage = "AmountFcfa doit être positif et raisonnable.")]
        public long AmountFcfa { get; set; }

        [StringLength(255, ErrorMessage = "Reason ne peut dépasser 255 caractères.")]
        public string? Reason { get; set; }
    }
}
