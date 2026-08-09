using Idara.API.Enums;

namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Représentation d'un retrait pour l'historique école. Le numéro du
    /// bénéficiaire est masqué (`77** ** *45`) — on ne ré-expose jamais le
    /// numéro complet dans une liste (spec §4.2).
    /// </summary>
    public class WithdrawalDto
    {
        public int Id { get; set; }
        public long AmountFcfa { get; set; }
        public long FeesFcfa { get; set; }
        public long NetReceivedFcfa { get; set; }
        public PaymentOperator Operator { get; set; }
        public TransferCategory Category { get; set; }
        /// <summary>Poche puisée (Total / Paiements / Dons) — pour l'affichage.</summary>
        public WithdrawalSource Source { get; set; }
        /// <summary>Nom de la nature quand Category == Other (sinon null).</summary>
        public string? CategoryLabel { get; set; }
        /// <summary>Motif / détails de l'opération (affiché dans le détail, pas dans les listes).</summary>
        public string? Motif { get; set; }
        public string RecipientName { get; set; } = string.Empty;
        /// <summary>Conservé pour les anciennes versions de l'app — préférer <see cref="RecipientPhone"/>.</summary>
        public string RecipientPhoneMasked { get; set; } = string.Empty;
        /// <summary>Numéro complet du bénéficiaire (l'école l'a saisi elle-même).</summary>
        public string RecipientPhone { get; set; } = string.Empty;
        public WithdrawalStatus Status { get; set; }

        /// <summary>
        /// Référence Idara (« RET-000087 ») : identifie ce virement dans notre base.
        /// </summary>
        public string Reference { get; set; } = string.Empty;

        /// <summary>
        /// Référence du décaissement chez SenePay. Null tant que le prestataire ne
        /// l'a pas renvoyée (retrait tout juste initié). C'est elle qui permet de
        /// rapprocher la ligne du tableau de bord SenePay — l'app ne l'exposait
        /// nulle part avant le 2026-08-08, alors qu'elle est en base depuis la
        /// Phase 3.
        /// </summary>
        public string? SenePayReference { get; set; }
        public string? FailureReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? FailedAt { get; set; }
    }
}
