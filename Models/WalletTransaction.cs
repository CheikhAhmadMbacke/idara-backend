using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Journal append-only de tous les mouvements sur un SchoolWallet.
    /// Aucun update / soft-delete : les corrections passent par une nouvelle
    /// transaction (principe comptable — aucune écriture rétroactive).
    ///
    /// AmountFcfa est SIGNÉ : positif pour Credit/Release, négatif pour
    /// Debit/Reservation. BalanceAfter snapshote l'AvailableBalance juste APRÈS
    /// application de cette transaction (sert à l'audit + reconciliation).
    /// </summary>
    public class WalletTransaction
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public SchoolWallet Wallet { get; set; } = null!;

        public WalletTransactionType Type { get; set; }

        /// <summary>
        /// Origine/nature dénormalisée (paiement / don / topup / abo / retrait /
        /// ajustement) — pour le BADGE d'affichage dans l'historique du wallet
        /// (différencier un don d'un paiement parent). Aucun impact financier.
        /// Défaut Payment = rétro-compatible.
        /// </summary>
        public WalletSource Source { get; set; } = WalletSource.Payment;

        public long AmountFcfa { get; set; }
        public long BalanceAfter { get; set; }

        public WalletRelatedEntity RelatedEntity { get; set; }

        /// <summary>Pointe vers Payment.Id, Withdrawal.Id, etc. selon RelatedEntity. NULL pour Adjustment manuel.</summary>
        public int? RelatedId { get; set; }

        public string? Note { get; set; }

        public DateTime OccurredAt { get; set; }
    }
}
