using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Une des 3 portions d'une journée (nouvelle leçon / révision récente /
    /// ancienne révision). Début → Fin exprimés en TEXTE coranique (référence
    /// sourate:verset + mot précis optionnel — une portion peut commencer/finir
    /// au milieu d'un verset). On stocke à la fois la référence (pour le calcul)
    /// et le texte affiché (pour l'historique, robuste au changement de dataset).
    /// </summary>
    public class CoranDailyPortion
    {
        public int Id { get; set; }

        public int DailyRecordId { get; set; }
        public CoranDailyRecord DailyRecord { get; set; } = null!;

        public CoranPortionKind Kind { get; set; }

        // Début
        public int? FromSurah { get; set; }     // 1..114
        public int? FromAyah { get; set; }      // verset dans la sourate
        public int? FromWordIndex { get; set; } // mot dans le verset (0-based), optionnel
        public string? FromWordText { get; set; }

        // Fin
        public int? ToSurah { get; set; }
        public int? ToAyah { get; set; }
        public int? ToWordIndex { get; set; }
        public string? ToWordText { get; set; }

        /// <summary>حالة : acquis / non acquis / non renseigné.</summary>
        public CoranPortionStatus Status { get; set; } = CoranPortionStatus.NotSet;
    }
}
