namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Statut de paiement d'un élève pour un mois donné, façon « cahier d'appel
    /// des paiements ».
    /// </summary>
    public enum RosterPaymentStatus
    {
        Paid = 0,       // Facture du mois entièrement payée
        Pending = 1,    // Facture émise, en attente, échéance non dépassée
        Overdue = 2,    // Facture en retard (échéance dépassée, non soldée)
        NoInvoice = 3   // Aucune facture pour ce mois (pas de tarif, ou cron non passé)
    }

    /// <summary>Une ligne du roster : un élève et son statut de paiement du mois.</summary>
    public class PaymentRosterEntryDto
    {
        public int StudentId { get; set; }
        public string StudentFirstName { get; set; } = string.Empty;
        public string StudentLastName { get; set; } = string.Empty;
        public string? StudentNumber { get; set; }
        public string? ClassName { get; set; }
        public RosterPaymentStatus Status { get; set; }
        public long AmountDueFcfa { get; set; }
        public long AmountPaidFcfa { get; set; }
        public int? InvoiceId { get; set; }
        public System.DateTime? DueDate { get; set; }
    }

    /// <summary>Roster complet d'un mois + compteurs par statut.</summary>
    public class PaymentRosterResponseDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int PaidCount { get; set; }
        public int PendingCount { get; set; }
        public int OverdueCount { get; set; }
        public int NoInvoiceCount { get; set; }
        public List<PaymentRosterEntryDto> Entries { get; set; } = new();
    }
}
