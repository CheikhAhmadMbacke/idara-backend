using Idara.API.Enums;

namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Vue Invoice utilisée côté Guardian (mes mensualités) et côté école
    /// (factures émises). Embarque les infos d'affichage pour éviter au
    /// client de croiser elle-même les tables.
    /// </summary>
    public class InvoiceDto
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public string? SchoolName { get; set; }

        public int StudentId { get; set; }
        public string StudentFirstName { get; set; } = string.Empty;
        public string StudentLastName { get; set; } = string.Empty;
        public string? StudentNumber { get; set; }
        public string? ClassName { get; set; }

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public DateTime DueDate { get; set; }

        public long AmountDueFcfa { get; set; }
        public long AmountPaidFcfa { get; set; }
        public long RemainingFcfa => Math.Max(0, AmountDueFcfa - AmountPaidFcfa);

        public InvoiceStatus Status { get; set; }
        public bool IsOverdue => Status == InvoiceStatus.Pending && DateTime.UtcNow.Date > DueDate.Date;

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
