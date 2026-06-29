using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Operations
{
    // ===== Lecture =====

    public class CoranDailyPortionDto
    {
        public CoranPortionKind Kind { get; set; }
        public int? FromSurah { get; set; }
        public int? FromAyah { get; set; }
        public int? FromWordIndex { get; set; }
        public string? FromWordText { get; set; }
        public int? ToSurah { get; set; }
        public int? ToAyah { get; set; }
        public int? ToWordIndex { get; set; }
        public string? ToWordText { get; set; }
        public CoranPortionStatus Status { get; set; }
    }

    public class CoranDailyRecordDto
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public int CycleId { get; set; }
        public int CycleNumber { get; set; }
        /// <summary>Position du jour dans le cycle (1..22), calculée par ordre de date.</summary>
        public int DayIndex { get; set; }
        public DateTime Date { get; set; }
        public string? Remarks { get; set; }
        public List<CoranDailyPortionDto> Portions { get; set; } = new();
    }

    public class CoranCycleDto
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int Number { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? CompletedDate { get; set; }
        public bool IsComplete { get; set; }
        public int DayCount { get; set; }
    }

    /// <summary>Récap d'un cycle pour le parent : le cycle + ses jours.</summary>
    public class CoranCycleDetailDto
    {
        public CoranCycleDto Cycle { get; set; } = new();
        public List<CoranDailyRecordDto> Records { get; set; } = new();
    }

    // ===== Écriture (upsert d'une journée) =====

    public class CoranDailyPortionInputDto
    {
        [Required] public CoranPortionKind Kind { get; set; }

        [Range(1, 114)] public int? FromSurah { get; set; }
        [Range(1, 286)] public int? FromAyah { get; set; }
        [Range(0, 200)] public int? FromWordIndex { get; set; }
        // Peut contenir une PLAGE de mots (pas un mot unique) → limite élargie.
        [StringLength(1000)] public string? FromWordText { get; set; }

        [Range(1, 114)] public int? ToSurah { get; set; }
        [Range(1, 286)] public int? ToAyah { get; set; }
        [Range(0, 200)] public int? ToWordIndex { get; set; }
        [StringLength(1000)] public string? ToWordText { get; set; }

        public CoranPortionStatus Status { get; set; } = CoranPortionStatus.NotSet;
    }

    public class CoranDailyUpsertDto
    {
        [Required] public int StudentId { get; set; }
        [Required] public DateTime Date { get; set; } = DateTime.UtcNow;
        [StringLength(2000)] public string? Remarks { get; set; }
        public List<CoranDailyPortionInputDto> Portions { get; set; } = new();
    }
}
