using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Facture d'abonnement plateforme (école → Idara), générée par le cron de
    /// facturation à chaque échéance. Append-only (entité de facturation).
    /// </summary>
    public class SubscriptionInvoice
    {
        public int Id { get; set; }

        public int SubscriptionId { get; set; }
        public Subscription Subscription { get; set; } = null!;

        public int SchoolId { get; set; }

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        /// <summary>
        /// Total dû pour la période : l'abonnement PLUS les SMS refacturés.
        /// C'est ce montant qui est prélevé, et c'est lui que la réconciliation
        /// plateforme somme pour obtenir P (§112) — d'où l'importance d'y
        /// inclure la refacturation plutôt que d'en faire une recette à part.
        /// </summary>
        public long AmountFcfa { get; set; }

        /// <summary>
        /// Part de <see cref="AmountFcfa"/> correspondant aux SMS refacturés à
        /// l'école sur la période (notifications de paiement qu'elle a
        /// explicitement demandées). Zéro pour toute école qui n'a rien activé.
        /// </summary>
        /// <remarks>
        /// Stocké en plus du total parce qu'une facture doit se LIRE : « 12 000
        /// d'abonnement + 340 de SMS » se conteste, « 12 340 » ne se conteste
        /// pas, il s'endure.
        /// </remarks>
        public long SmsRefactureFcfa { get; set; }

        /// <summary>Nombre de SMS refacturés — le détail derrière le montant.</summary>
        public int SmsRefactureCount { get; set; }

        public SubscriptionInvoiceStatus Status { get; set; } = SubscriptionInvoiceStatus.Pending;

        public DateTime IssuedAt { get; set; }
        public DateTime? PaidAt { get; set; }

        /// <summary>Id de la WalletTransaction du débit (audit), si payé par prélèvement wallet.</summary>
        public int? WalletTransactionId { get; set; }

        public string? PdfPath { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
