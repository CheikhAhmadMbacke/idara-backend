using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Alerte payout persistée (append-only). Doublée d'un <c>LogCritical</c>
    /// greppable côté journalctl. Permet à un dashboard SuperAdmin de lister les
    /// anomalies de décaissement (double dépense corrigée, réconciliation rompue,
    /// retrait coincé…) et de les marquer résolues. Branchable WhatsApp en Phase 2.
    /// </summary>
    public class PayoutAlert
    {
        public int Id { get; set; }

        public PayoutAlertType Type { get; set; }

        /// <summary>École concernée (null si l'alerte est globale, ex. réconciliation).</summary>
        public int? SchoolId { get; set; }

        /// <summary>Retrait concerné (null si l'alerte ne cible pas un retrait précis).</summary>
        public int? WithdrawalId { get; set; }

        /// <summary>Message lisible (FR) résumant l'alerte.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Détails structurés (jsonb) : montants, statuts, ids SenePay… pour debug.</summary>
        public string? Details { get; set; }

        public bool Resolved { get; set; }
        public DateTime? ResolvedAt { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
