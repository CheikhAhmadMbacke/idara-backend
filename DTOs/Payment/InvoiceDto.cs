using System.ComponentModel.DataAnnotations;
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
        /// <summary>Nom du daara en arabe (affiché sous le nom français). Null si absent.</summary>
        public string? SchoolNameAr { get; set; }
        /// <summary>Logo du daara (chemin relatif /uploads/...) pour l'afficher sur chaque ligne côté parent.</summary>
        public string? SchoolLogoUrl { get; set; }

        public int StudentId { get; set; }
        public string StudentFirstName { get; set; } = string.Empty;
        public string StudentLastName { get; set; } = string.Empty;
        public string? StudentNumber { get; set; }
        public string? ClassName { get; set; }

        /// <summary>Nature (mensualité / frais d'inscription). Le libellé affiché est
        /// TOUJOURS dérivé de ce champ, jamais stocké en texte libre.</summary>
        public InvoiceType Type { get; set; } = InvoiceType.MonthlyFee;

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

    /// <summary>💵 Encaissement en espèces au guichet du daara.</summary>
    public class CollectCashDto
    {
        /// <summary>
        /// Montant réellement remis, en FCFA. Peut être <b>inférieur</b> au reste
        /// dû : un acompte est courant dans un daara, et refuser les paiements
        /// partiels renverrait le personnel à son cahier.
        /// </summary>
        [Range(1, 100_000_000, ErrorMessage = "Le montant doit être compris entre 1 et 100 000 000 FCFA.")]
        public long AmountFcfa { get; set; }

        /// <summary>Précision libre (« versé par l'oncle », « acompte »…).</summary>
        [StringLength(300)]
        public string? Note { get; set; }

        /// <summary>Jour de l'encaissement. Absent = aujourd'hui.</summary>
        public DateTime? OccurredAt { get; set; }
    }

    /// <summary>Annulation d'un encaissement en espèces saisi par erreur.</summary>
    public class CancelCashDto
    {
        [StringLength(300)]
        public string? Reason { get; set; }
    }
}
