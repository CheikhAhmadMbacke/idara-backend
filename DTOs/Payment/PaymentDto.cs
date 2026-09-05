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

        /// <summary>
        /// Référence Idara de l'encaissement (« PAY-000042 »). Calculée côté
        /// serveur pour que le format ne soit défini qu'à UN endroit : une copie
        /// du gabarit dans l'app finirait par diverger d'une version à l'autre.
        /// </summary>
        public string Reference { get; set; } = string.Empty;
        /// <summary>Nom du daara en arabe (affiché sous le nom français). Null si absent.</summary>
        public string? SchoolNameAr { get; set; }
        /// <summary>Logo du daara (chemin relatif /uploads/...) pour l'afficher sur chaque ligne côté parent.</summary>
        public string? SchoolLogoUrl { get; set; }

        public int? StudentId { get; set; }
        public string? StudentFirstName { get; set; }
        public string? StudentLastName { get; set; }
        public string? StudentNumber { get; set; }

        public int? GuardianId { get; set; }
        /// <summary>Nom du payeur (responsable) — sert de libellé quand le paiement
        /// n'a pas d'élève unique (paiement consolidé multi-enfants).</summary>
        public string? GuardianName { get; set; }
        public int? InvoiceId { get; set; }

        /// <summary>Nature (mensualité / recharge / DON) — pour le badge côté école.</summary>
        public PaymentPurpose Purpose { get; set; }

        /// <summary>Nom + type du donateur (renseignés uniquement pour un don).</summary>
        /// <remarks>
        /// ⚠️ Deux sortes de dons cohabitent. Le don d'un COMPTE donateur remplit
        /// <see cref="DonorId"/> et <see cref="DonorType"/> ; le don par LIEN n'a
        /// aucun compte — son identité vit sur le paiement lui-même, et se lit
        /// dans les quatre champs ci-dessous. Ne renseigner que le premier cas,
        /// c'était laisser l'école devant « Don reçu » sans savoir de qui.
        /// </remarks>
        public int? DonorId { get; set; }
        public string? DonorName { get; set; }
        public DonorType? DonorType { get; set; }

        public string? DonorPhone { get; set; }
        public string? DonorOrganization { get; set; }

        /// <summary>L'anonymat ne vaut QUE pour la page publique : l'école voit toujours qui a donné.</summary>
        public bool DonorAnonymous { get; set; }

        /// <summary>La collecte à l'origine du don — sans elle, on ignore ce qui a été soutenu.</summary>
        public int? DonationCampaignId { get; set; }
        public string? DonationCampaignName { get; set; }

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
