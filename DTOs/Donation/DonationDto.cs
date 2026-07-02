using Idara.API.Enums;

namespace Idara.API.DTOs.Donation
{
    /// <summary>Vue d'un don côté DONATEUR (« mes dons » + poll de statut).</summary>
    public class DonationDto
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public string? SchoolName { get; set; }

        /// <summary>Montant du don (ce que reçoit le daara).</summary>
        public long AmountFcfa { get; set; }

        /// <summary>Montant débité du donateur (don + frais +8 %).</summary>
        public long AmountChargedFcfa { get; set; }

        public PaymentOperator Operator { get; set; }
        public PaymentStatus Status { get; set; }
        public string? FailureReason { get; set; }

        public DateTime InitiatedAt { get; set; }
        public DateTime? PaidAt { get; set; }

        public string? ReceiptPdfUrl { get; set; }
    }
}
