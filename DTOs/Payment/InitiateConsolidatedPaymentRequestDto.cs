using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Requête « paiement global » : un parent règle en UNE fois le total des
    /// mensualités dues de TOUS ses enfants dans une même école (mode
    /// FixedAmount). Le backend est la source de vérité : il recalcule
    /// lui-même l'ensemble des factures impayées (Pending/Overdue) des enfants
    /// liés au gardien dans cette école — le client n'envoie pas la liste, pour
    /// éviter toute falsification.
    ///
    /// Même flow SenePay que le paiement unitaire (§3 gotcha : 1 seul appel,
    /// Orange exige otpCode généré via #144#391#).
    /// </summary>
    public class InitiateConsolidatedPaymentRequestDto
    {
        /// <summary>École ciblée. La consolidation est TOUJOURS par école (un paiement crédite un seul wallet).</summary>
        [Required]
        public int? SchoolId { get; set; }

        /// <summary>DÉPRÉCIÉ (2026-07-07) — ignoré. Wave forcé côté serveur.</summary>
        public string? Operator { get; set; }

        /// <summary>DÉPRÉCIÉ (2026-07-07) — ignoré. Numéro du payeur récupéré en base.</summary>
        public string? CustomerPhone { get; set; }

        /// <summary>DÉPRÉCIÉ (2026-07-07) — ignoré (plus d'Orange).</summary>
        public string? OtpCode { get; set; }
    }
}
