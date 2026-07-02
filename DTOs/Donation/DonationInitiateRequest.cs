using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Donation
{
    /// <summary>
    /// Corps de `POST /api/donations/initiate` : un donateur envoie un don libre
    /// à un daara. Même mécanique payin que le paiement parent (Wave/Orange, OTP
    /// Orange), FeesPayer=Parent (le donateur porte les frais +8 %, le daara
    /// reçoit le montant plein).
    /// </summary>
    public class DonationInitiateRequest
    {
        [Range(1, int.MaxValue, ErrorMessage = "Daara invalide.")]
        public int SchoolId { get; set; }

        /// <summary>Montant du don (ce que reçoit le daara). Min = MinPayinFcfa (200).</summary>
        [Range(1, long.MaxValue, ErrorMessage = "Le montant doit être positif.")]
        public long Amount { get; set; }

        /// <summary>"wave" ou "orange".</summary>
        [Required(ErrorMessage = "L'opérateur est obligatoire.")]
        public string Operator { get; set; } = string.Empty;

        /// <summary>Numéro national du donateur (9 chiffres, sans indicatif).</summary>
        [Required(ErrorMessage = "Le numéro de téléphone est obligatoire.")]
        public string CustomerPhone { get; set; } = string.Empty;

        /// <summary>OTP Orange (généré via #144#391# avant l'appel). Ignoré pour Wave.</summary>
        public string? OtpCode { get; set; }
    }
}
