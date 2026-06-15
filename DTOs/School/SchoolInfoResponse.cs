using Idara.API.Enums;

namespace Idara.API.DTOs.School
{
    public class SchoolInfoResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public List<string> LegalDocumentsUrls { get; set; } = new();
        public KycStatus KycStatus { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ValidatedAt { get; set; }
        public string RepresentativeFirstName { get; set; } = string.Empty;
        public string RepresentativeLastName { get; set; } = string.Empty;
        public string RepresentativePhone { get; set; } = string.Empty;
        public List<string> RepresentativeIdDocumentUrls { get; set; } = new();
        public QuranRiwaya QuranRiwaya { get; set; }
        public List<UserInfoDto> Users { get; set; } = new();
    }
}
