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

        /// <summary>DÉPRÉCIÉ (2026-07-07) — ignoré. Wave forcé côté serveur.</summary>
        public string? Operator { get; set; }

        /// <summary>DÉPRÉCIÉ (2026-07-07) — ignoré. Numéro du donateur récupéré en base.</summary>
        public string? CustomerPhone { get; set; }

        /// <summary>DÉPRÉCIÉ (2026-07-07) — ignoré (plus d'Orange).</summary>
        public string? OtpCode { get; set; }
    }
}
