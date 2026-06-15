namespace Idara.API.Models
{
    /// <summary>
    /// Évaluation qualité d'un élève : quand il termine un hizb, il vient réciter
    /// devant l'ADMINISTRATION (contrôle qualité de l'enseignement, en amont des
    /// examinateurs externes). On note récitation / tajwid / mémorisation /5 sur
    /// une plage (Début → Fin), et on consigne chaque faute et chaque blocage
    /// (cf. <see cref="CoranEvaluationIncident"/>).
    /// </summary>
    public class CoranEvaluation
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        /// <summary>Qui a fait passer l'évaluation (SchoolAdmin).</summary>
        public int? EvaluatorId { get; set; }
        public User? Evaluator { get; set; }

        public DateTime Date { get; set; }

        /// <summary>Hizb terminé / évalué (1..60), optionnel.</summary>
        public int? HizbNumber { get; set; }

        // Plage récitée (Début → Fin), précision au mot possible.
        public int? FromSurah { get; set; }
        public int? FromAyah { get; set; }
        public int? FromWordIndex { get; set; }
        public string? FromWordText { get; set; }
        public int? ToSurah { get; set; }
        public int? ToAyah { get; set; }
        public int? ToWordIndex { get; set; }
        public string? ToWordText { get; set; }

        // Notation /5
        public int? RecitationScore { get; set; }
        public int? TajwidScore { get; set; }
        public int? MemorizationScore { get; set; }

        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; }

        public ICollection<CoranEvaluationIncident> Incidents { get; set; }
            = new List<CoranEvaluationIncident>();
    }
}
