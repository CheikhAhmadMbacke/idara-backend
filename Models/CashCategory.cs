using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Catégorie de mouvement de caisse par école (gestion financière, F2), avec
    /// budget mensuel optionnel. Sert à classer les entrées/sorties et à alerter
    /// sur les dépassements de budget. Archivable (jamais supprimée : les écritures
    /// passées gardent leur lien).
    /// </summary>
    public class CashCategory
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        /// <summary>Nature à laquelle s'applique la catégorie (entrée ou sortie).</summary>
        public CashEntryType Type { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Budget mensuel en FCFA (null = pas de budget → pas d'alerte).</summary>
        public long? MonthlyBudgetFcfa { get; set; }

        public bool IsArchived { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
