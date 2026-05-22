namespace Idara.API.Models
{
    /// <summary>
    /// État de mémorisation actuel d'un élève (UN enregistrement par élève, mis à jour).
    /// Sourate / verset courant + plage globale mémorisée.
    /// </summary>
    public class CoranProgress
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        // Position courante de mémorisation (où on en est)
        public int? CurrentSurah { get; set; }   // 1..114
        public int? CurrentVerse { get; set; }   // verset au sein de la sourate
        public int? CurrentHizb { get; set; }    // 1..60
        public int? CurrentJuz { get; set; }     // 1..30

        // Plage déjà mémorisée
        public int? MemorizedFromSurah { get; set; }
        public int? MemorizedFromVerse { get; set; }
        public int? MemorizedToSurah { get; set; }
        public int? MemorizedToVerse { get; set; }

        // Évaluation moyenne /5 (mises à jour par les sessions)
        public double? AverageRecitation { get; set; }
        public double? AverageTajwid { get; set; }

        public string? Notes { get; set; }
        public int? UpdatedById { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
