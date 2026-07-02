using Idara.API.Enums;

namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Vue Payment exposée côté Guardian (poll de statut, historique) et côté
    /// école (paiements reçus).
    /// </summary>
    public class PaymentDto
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public string? SchoolName { get; set; }

        public int? StudentId { get; set; }
        public string? StudentFirstName { get; set; }
        public string? StudentLastName { get; set; }
        public string? StudentNumber { get; set; }

        public int? GuardianId { get; set; }
        public int? InvoiceId { get; set; }

        /// <summary>Nature (mensualité / recharge / DON) — pour le badge côté école.</summary>
        public PaymentPurpose Purpose { get; set; }

        /// <summary>Nom + type du donateur (renseignés uniquement pour un don).</summary>
        public int? DonorId { get; set; }
        public string? DonorName { get; set; }
        public DonorType? DonorType { get; set; }

        public long AmountFcfa { get; set; }
        public long FeesFcfa { get; set; }
        public long NetCreditedFcfa { get; set; }

        public PaymentOperator Operator { get; set; }
        public FeesPayer FeesPayer { get; set; }
        public PaymentStatus Status { get; set; }

        public string? SenePayTransactionId { get; set; }
        public string? FailureReason { get; set; }

        public DateTime InitiatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? FailedAt { get; set; }

        public string? ReceiptPdfUrl { get; set; }
    }
}
