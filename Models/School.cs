using Idara.API.Enums;

namespace Idara.API.Models
{
    public class School
    {
        public int Id { get; set; }
        public KycStatus KycStatus { get; set; } = KycStatus.PendingSubmission;
        public string? Name { get; set; }
        public string? Address { get; set; }
        public string? PhoneNumber { get; set; }
        public string? LegalDocumentsUrl { get; set; }
        public string? RepresentativeFirstName { get; set; }
        public string? RepresentativeLastName { get; set; }
        public string? RepresentativePhone { get; set; }
        public string? RepresentativeIdDocumentUrl { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ValidatedAt { get; set; }
        public int? ValidatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public ICollection<User> Users { get; set; } = new List<User>();
    }
}
