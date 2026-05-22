namespace Idara.API.Models
{
    /// <summary>
    /// Compte-rendu quotidien d'un enseignant sur un élève — typique des daara
    /// où chaque maître note ce que son élève a appris dans la journée.
    /// Plusieurs entrées possibles par élève par jour (une par enseignant et matière).
    /// </summary>
    public class DailyJournalEntry
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        public int TeacherId { get; set; }
        public User Teacher { get; set; } = null!;

        /// <summary>Optionnel : matière concernée (Coran, Fiqh, Arabe, etc.).</summary>
        public int? SubjectId { get; set; }
        public Subject? Subject { get; set; }

        /// <summary>Date du jour (heure ignorée).</summary>
        public DateTime Date { get; set; }

        /// <summary>Texte multi-ligne : ce que l'élève a appris/travaillé aujourd'hui.</summary>
        public string LearnedToday { get; set; } = string.Empty;

        /// <summary>Note de comportement /5 (optionnel).</summary>
        public int? BehaviorScore { get; set; }

        /// <summary>Note d'effort/concentration /5 (optionnel).</summary>
        public int? EffortScore { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public int? DeletedById { get; set; }
    }
}
