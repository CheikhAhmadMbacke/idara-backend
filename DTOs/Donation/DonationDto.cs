using Idara.API.Enums;

namespace Idara.API.DTOs.Donation
{
    /// <summary>Vue d'un don côté DONATEUR (« mes dons » + poll de statut).</summary>
    public class DonationDto
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public string? SchoolName { get; set; }
        /// <summary>Nom du daara en arabe (affiché sous le nom français). Null si absent.</summary>
        public string? SchoolNameAr { get; set; }
        /// <summary>Logo du daara (chemin relatif /uploads/...) pour l'afficher sur chaque ligne « mes dons ».</summary>
        public string? SchoolLogoUrl { get; set; }

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
