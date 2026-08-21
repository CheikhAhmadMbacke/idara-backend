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

        /// <summary>
        /// Ce suivi est-il encore modifiable PAR L'APPELANT ? Calculé côté
        /// serveur (§151) : un enseignant perd la main au-delà de
        /// <see cref="EditWindowHours"/> heures après sa saisie, la direction
        /// garde la main sans limite. Défaut <c>true</c> côté client si le
        /// champ manque — une APK antérieure ne doit pas croire à un verrou
        /// inexistant, le serveur reste de toute façon l'autorité.
        /// </summary>
        public bool Editable { get; set; } = true;

        /// <summary>Durée du délai, pour l'expliquer à l'utilisateur.</summary>
        public int EditWindowHours { get; set; }
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

    /// <summary>
    /// Saisie du suivi coranique pour TOUTE UNE CLASSE en une fois.
    /// </summary>
    /// <remarks>
    /// Le suivi coranique se saisissait un élève à la fois — alors que c'est le
    /// geste quotidien du maître, et que le pointage comme le journal se
    /// saisissent déjà par classe. Pour trente élèves, cela faisait trente
    /// allers-retours complets, là où le tableau papier du daara
    /// (« جدول المتابعة اليومية ») est justement une grille classe × jour.
    /// </remarks>
    public class CoranDailyBulkDto : IValidatableObject
    {
        [Required] public DateTime Date { get; set; } = DateTime.UtcNow;

        /// <summary>Classe saisie — informative, le périmètre réel est celui
        /// de l'appelant (§150).</summary>
        public int? ClassId { get; set; }

        [Required] public List<CoranDailyBulkEntryDto> Entries { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext ctx)
        {
            if (Entries == null || Entries.Count == 0)
                yield return new ValidationResult(
                    "La liste 'Entries' ne peut pas être vide.", new[] { nameof(Entries) });
        }
    }

    public class CoranDailyBulkEntryDto
    {
        [Required] public int StudentId { get; set; }
        [StringLength(2000)] public string? Remarks { get; set; }
        public List<CoranDailyPortionInputDto> Portions { get; set; } = new();
    }

    /// <summary>Compte rendu d'une saisie en lot.</summary>
    public class CoranDailyBulkResultDto
    {
        public int Saved { get; set; }

        /// <summary>Élèves dont la journée était déjà verrouillée (48 h, §151).
        /// Comptés pour être DITS : un passage silencieux laisserait croire à
        /// une saisie enregistrée qui ne l'a pas été.</summary>
        public int Locked { get; set; }

        /// <summary>Cycles de 22 jours clôturés par cette saisie.</summary>
        public int CyclesCompleted { get; set; }
    }
}
