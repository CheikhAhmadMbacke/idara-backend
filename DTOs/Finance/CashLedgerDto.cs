using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Finance
{
    /// <summary>Une écriture du livre de caisse (lecture).</summary>
    public class CashLedgerEntryDto
    {
        public int Id { get; set; }
        public CashEntryType Type { get; set; }
        public long AmountFcfa { get; set; }
        public string? Category { get; set; }
        public DateTime OccurredAt { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateCashLedgerEntryDto
    {
        [Required]
        public CashEntryType Type { get; set; }

        [Range(1, 100_000_000_000, ErrorMessage = "Le montant doit être positif.")]
        public long AmountFcfa { get; set; }

        [StringLength(80)]
        public string? Category { get; set; }

        /// <summary>Date du mouvement. Si absente, la date du jour est utilisée.</summary>
        public DateTime? OccurredAt { get; set; }

        [StringLength(500)]
        public string? Note { get; set; }
    }

    /// <summary>Bilan financier consolidé : wallet SenePay + livre de caisse.</summary>
    public class CashBalanceDto
    {
        /// <summary>Solde disponible du wallet SenePay (argent réel encaissé/retirable).</summary>
        public long WalletAvailableFcfa { get; set; }

        /// <summary>Entrées de caisse sur la période sélectionnée.</summary>
        public long CashIncomeFcfa { get; set; }

        /// <summary>Sorties de caisse sur la période sélectionnée.</summary>
        public long CashExpenseFcfa { get; set; }

        /// <summary>Net de caisse sur la période (entrées − sorties).</summary>
        public long CashNetFcfa { get; set; }

        /// <summary>Solde cumulé du livre de caisse (toutes périodes).</summary>
        public long CashBalanceAllTimeFcfa { get; set; }

        /// <summary>Solde GLOBAL du daara = wallet SenePay + solde caisse cumulé.</summary>
        public long GlobalBalanceFcfa { get; set; }
    }
}
