using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Finance
{
    /// <summary>Catégorie de caisse + statut de budget du mois courant.</summary>
    public class CashCategoryDto
    {
        public int Id { get; set; }
        public CashEntryType Type { get; set; }
        public string Name { get; set; } = string.Empty;
        public long? MonthlyBudgetFcfa { get; set; }

        /// <summary>Dépensé/reçu sur cette catégorie ce mois-ci.</summary>
        public long SpentThisMonthFcfa { get; set; }

        /// <summary>true si un budget est défini ET dépassé ce mois-ci.</summary>
        public bool IsOverBudget { get; set; }
    }

    public class CreateCashCategoryDto
    {
        [Required]
        public CashEntryType Type { get; set; }

        [Required, StringLength(80, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        [Range(0, 100_000_000_000)]
        public long? MonthlyBudgetFcfa { get; set; }
    }
}
