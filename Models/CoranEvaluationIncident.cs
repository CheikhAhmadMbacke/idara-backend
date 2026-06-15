using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Une faute ou un blocage relevé pendant une évaluation, avec l'endroit
    /// EXACT (sourate:verset + mot précis) et un détail libre (ex. ce que l'élève
    /// a réellement dit). Pour une faute, <see cref="FaultType"/> précise sa
    /// nature ; pour un blocage il reste null.
    /// </summary>
    public class CoranEvaluationIncident
    {
        public int Id { get; set; }

        public int EvaluationId { get; set; }
        public CoranEvaluation Evaluation { get; set; } = null!;

        public CoranIncidentKind Kind { get; set; }

        /// <summary>Nature de la faute (null pour un blocage).</summary>
        public CoranFaultType? FaultType { get; set; }

        // Endroit exact.
        public int? Surah { get; set; }
        public int? Ayah { get; set; }
        public int? WordIndex { get; set; }
        public string? WordText { get; set; }

        /// <summary>Détail libre (ex. « a dit qîla au lieu de qâla »).</summary>
        public string? Detail { get; set; }
    }
}
