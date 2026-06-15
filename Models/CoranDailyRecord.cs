namespace Idara.API.Models
{
    /// <summary>
    /// Le suivi d'UNE journée pour UN élève (une ligne du tableau « جدول
    /// المتابعة اليومية »). Contient les 3 portions du jour (nouvelle leçon /
    /// révision récente / ancienne révision) + des remarques. Saisi par
    /// l'enseignant. Unicité (StudentId, Date) : ré-enregistrer le même jour
    /// met à jour la ligne (pas de doublon).
    /// </summary>
    public class CoranDailyRecord
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        public int CycleId { get; set; }
        public CoranCycle Cycle { get; set; } = null!;

        /// <summary>Jour civil réel (tronqué à minuit UTC).</summary>
        public DateTime Date { get; set; }

        public string? Remarks { get; set; }

        public int? RecordedById { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ICollection<CoranDailyPortion> Portions { get; set; } = new List<CoranDailyPortion>();
    }
}
