using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Operations
{
    public class CoranProgressDto
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int? CurrentSurah { get; set; }
        public int? CurrentVerse { get; set; }
        public int? CurrentHizb { get; set; }
        public int? CurrentJuz { get; set; }
        public int? MemorizedFromSurah { get; set; }
        public int? MemorizedFromVerse { get; set; }
        public int? MemorizedToSurah { get; set; }
        public int? MemorizedToVerse { get; set; }
        public double? AverageRecitation { get; set; }
        public double? AverageTajwid { get; set; }
        public string? Notes { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class CoranProgressUpsertDto
    {
        [Range(1, 114)] public int? CurrentSurah { get; set; }
        [Range(1, 286)] public int? CurrentVerse { get; set; }
        [Range(1, 60)]  public int? CurrentHizb { get; set; }
        [Range(1, 30)]  public int? CurrentJuz { get; set; }

        [Range(1, 114)] public int? MemorizedFromSurah { get; set; }
        public int? MemorizedFromVerse { get; set; }
        [Range(1, 114)] public int? MemorizedToSurah { get; set; }
        public int? MemorizedToVerse { get; set; }

        [StringLength(2000)] public string? Notes { get; set; }
    }

    public class CoranSessionDto
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public CoranSessionKind Kind { get; set; }
        public int? FromSurah { get; set; }
        public int? FromVerse { get; set; }
        public int? ToSurah { get; set; }
        public int? ToVerse { get; set; }
        public int? RecitationScore { get; set; }
        public int? TajwidScore { get; set; }
        public int? MemorizationScore { get; set; }
        public string? Comment { get; set; }
        public string? TeacherName { get; set; }
    }

    public class CoranSessionCreateDto
    {
        [Required] public int StudentId { get; set; }
        [Required] public DateTime Date { get; set; } = DateTime.UtcNow;
        public CoranSessionKind Kind { get; set; } = CoranSessionKind.Memorization;

        [Range(1, 114)] public int? FromSurah { get; set; }
        public int? FromVerse { get; set; }
        [Range(1, 114)] public int? ToSurah { get; set; }
        public int? ToVerse { get; set; }

        [Range(1, 5)] public int? RecitationScore { get; set; }
        [Range(1, 5)] public int? TajwidScore { get; set; }
        [Range(1, 5)] public int? MemorizationScore { get; set; }

        [StringLength(1000)] public string? Comment { get; set; }
    }
}
