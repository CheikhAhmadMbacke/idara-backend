using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Payment
{
    /// <summary>Demande de lien : depuis un élève (le responsable est résolu) ou un responsable précis.</summary>
    public class CreatePaymentLinkRequestDto
    {
        [Required]
        public int StudentId { get; set; }

        /// <summary>
        /// Responsable choisi quand l'élève en a plusieurs. Null = si l'élève n'a
        /// qu'un responsable, lui ; sinon le serveur renvoie la liste à choisir
        /// (<see cref="PaymentLinkResponseDto.NeedsGuardianChoice"/>).
        /// </summary>
        public int? GuardianId { get; set; }
    }

    public class PaymentLinkGuardianChoiceDto
    {
        public int GuardianId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public bool IsPrimary { get; set; }
    }

    /// <summary>Une ligne de la dette actuelle (un enfant, une facture).</summary>
    public class PaymentLinkDueLineDto
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        /// <summary>Libellé dérivé du type : « Frais d'inscription » ou « août 2026 ».</summary>
        public string Label { get; set; } = string.Empty;
        public long AmountFcfa { get; set; }
    }

    public class PaymentLinkResponseDto
    {
        /// <summary>True = l'élève a plusieurs responsables, l'école doit choisir (aucun lien créé).</summary>
        public bool NeedsGuardianChoice { get; set; }
        public List<PaymentLinkGuardianChoiceDto> Guardians { get; set; } = new();

        public int? LinkId { get; set; }
        public string? Url { get; set; }
        public int? GuardianId { get; set; }
        public string? GuardianFullName { get; set; }
        /// <summary>E.164 (+221…) — sert à ouvrir wa.me.</summary>
        public string? GuardianPhone { get; set; }
        /// <summary>Message WhatsApp prêt à envoyer (français, composé par le serveur).</summary>
        public string? Message { get; set; }

        public bool HasOutstanding { get; set; }
        public bool IsFreeAmount { get; set; }
        public long TotalDueFcfa { get; set; }
        public long AmountToChargeFcfa { get; set; }
        public List<PaymentLinkDueLineDto> Lines { get; set; } = new();

        public DateTime? CreatedAt { get; set; }
        public DateTime? LastOpenedAt { get; set; }
        public int OpenCount { get; set; }
        public int PaidViaLinkCount { get; set; }
    }
}
