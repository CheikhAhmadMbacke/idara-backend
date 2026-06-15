namespace Idara.API.Models
{
    /// <summary>
    /// Un cycle de suivi quotidien Coran = jusqu'à 22 jours OUVRÉS de travail
    /// (pas un mois calendaire — un daara se repose ~2 jours/semaine, souvent
    /// jeudi+vendredi, et n'a pas de « grandes vacances »). On compte simplement
    /// les jours réellement saisis ; au 22ᵉ, le cycle se clôt et les parents
    /// reçoivent le récapitulatif. Un nouveau jour saisi ouvre le cycle suivant.
    /// </summary>
    public class CoranCycle
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        /// <summary>Numéro de cycle pour cet élève (1, 2, 3…).</summary>
        public int Number { get; set; }

        /// <summary>Date du 1er jour saisi du cycle.</summary>
        public DateTime StartDate { get; set; }

        /// <summary>Date du 22ᵉ jour (clôture). Null tant que le cycle est ouvert.</summary>
        public DateTime? CompletedDate { get; set; }

        public bool IsComplete { get; set; }

        public ICollection<CoranDailyRecord> Records { get; set; } = new List<CoranDailyRecord>();
    }
}
