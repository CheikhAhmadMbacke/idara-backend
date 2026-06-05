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
        public string RecipientName { get; set; } = string.Empty;
        public string RecipientPhoneMasked { get; set; } = string.Empty;
        public WithdrawalStatus Status { get; set; }
        public string? FailureReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? FailedAt { get; set; }
    }
}
