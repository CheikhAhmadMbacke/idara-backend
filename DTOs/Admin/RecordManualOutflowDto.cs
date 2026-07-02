using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Admin
{
    /// <summary>
    /// Enregistre a posteriori un retrait effectué manuellement depuis le
    /// dashboard marchand SenePay (hors Idara), pour garder la réconciliation
    /// exacte. Ne déclenche AUCUN mouvement SenePay — c'est une écriture comptable.
    /// </summary>
    public class RecordManualOutflowDto
    {
        [Range(1, long.MaxValue, ErrorMessage = "Le montant doit être positif.")]
        public long AmountFcfa { get; set; }

        [StringLength(500)]
        public string? Note { get; set; }

        /// <summary>Date effective du retrait (défaut : maintenant).</summary>
        public DateTime? OccurredAt { get; set; }
    }
}
