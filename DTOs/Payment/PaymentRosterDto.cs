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

        /// <summary>
        /// Nom complet du parent à contacter : le responsable LIÉ au compte de
        /// l'app en priorité (c'est lui qui paie), sinon le père puis la mère
        /// renseignés dans le dossier de l'élève. Null si aucun des trois.
        /// </summary>
        public string? GuardianFullName { get; set; }

        /// <summary>Numéro du même parent (même cascade) — sert à relancer par WhatsApp.</summary>
        public string? GuardianPhone { get; set; }

        /// <summary>
        /// Date du dernier paiement COMPLÉTÉ imputé à la facture du mois (paiement
        /// direct ou part d'un paiement global consolidé). Null si non payé.
        /// </summary>
        public System.DateTime? PaidAt { get; set; }

        /// <summary>
        /// True si l'élève a au moins un responsable LIÉ à un compte (condition
        /// pour générer un lien de paiement). Père/mère du dossier ne comptent pas.
        /// </summary>
        public bool HasLinkedGuardian { get; set; }

        /// <summary>Dernier envoi (génération/renvoi) du lien de paiement du responsable lié ; null = jamais.</summary>
        public System.DateTime? PaymentLinkSentAt { get; set; }

        /// <summary>Dernière ouverture du lien par le parent ; null = jamais ouvert.</summary>
        public System.DateTime? PaymentLinkOpenedAt { get; set; }
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
