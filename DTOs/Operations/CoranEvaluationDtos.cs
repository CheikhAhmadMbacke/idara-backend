using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Operations
{
    // ===== Lecture =====

    public class CoranEvaluationIncidentDto
    {
        public CoranIncidentKind Kind { get; set; }
        public CoranFaultType? FaultType { get; set; }
        public int? Surah { get; set; }
        public int? Ayah { get; set; }
        public int? WordIndex { get; set; }
        public string? WordText { get; set; }
        public string? Detail { get; set; }
    }

    public class CoranEvaluationDto
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string? EvaluatorName { get; set; }
        public DateTime Date { get; set; }
        public int? HizbNumber { get; set; }
        public int? FromSurah { get; set; }
        public int? FromAyah { get; set; }
        public int? FromWordIndex { get; set; }
        public string? FromWordText { get; set; }
        public int? ToSurah { get; set; }
        public int? ToAyah { get; set; }
        public int? ToWordIndex { get; set; }
        public string? ToWordText { get; set; }
        public int? RecitationScore { get; set; }
        public int? TajwidScore { get; set; }
        public int? MemorizationScore { get; set; }
        public string? Comment { get; set; }
        public List<CoranEvaluationIncidentDto> Incidents { get; set; } = new();
    }

    // ===== Écriture =====

    public class CoranEvaluationIncidentInputDto
    {
        [Required] public CoranIncidentKind Kind { get; set; }
        public CoranFaultType? FaultType { get; set; }

        [Range(1, 114)] public int? Surah { get; set; }
        [Range(1, 286)] public int? Ayah { get; set; }
        [Range(0, 200)] public int? WordIndex { get; set; }
        // Peut contenir une PLAGE de mots (pas un mot unique) → limite élargie.
        [StringLength(1000)] public string? WordText { get; set; }
        [StringLength(500)] public string? Detail { get; set; }
    }

    public class CoranEvaluationCreateDto
    {
        [Required] public int StudentId { get; set; }
        [Required] public DateTime Date { get; set; } = DateTime.UtcNow;

        [Range(1, 60)] public int? HizbNumber { get; set; }

        [Range(1, 114)] public int? FromSurah { get; set; }
        [Range(1, 286)] public int? FromAyah { get; set; }
        [Range(0, 200)] public int? FromWordIndex { get; set; }
        // Peut contenir une PLAGE de mots (pas un mot unique) → limite élargie.
        [StringLength(1000)] public string? FromWordText { get; set; }

        [Range(1, 114)] public int? ToSurah { get; set; }
        [Range(1, 286)] public int? ToAyah { get; set; }
        [Range(0, 200)] public int? ToWordIndex { get; set; }
        [StringLength(1000)] public string? ToWordText { get; set; }

        [Range(1, 5)] public int? RecitationScore { get; set; }
        [Range(1, 5)] public int? TajwidScore { get; set; }
        [Range(1, 5)] public int? MemorizationScore { get; set; }

        [StringLength(1000)] public string? Comment { get; set; }

        public List<CoranEvaluationIncidentInputDto> Incidents { get; set; } = new();
    }
}
