using Idara.API.Enums;

namespace Idara.API.DTOs.Student
{
    public class StudentDocumentResponseDto
    {
        public int Id { get; set; }
        public StudentDocumentType Type { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime UploadedAt { get; set; }
        public string? Notes { get; set; }
    }
}
