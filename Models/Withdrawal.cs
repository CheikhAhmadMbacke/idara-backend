using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Retrait école → compte Mobile Money via SenePay Payout. Append-only
    /// (entité financière, pas de soft-delete — cf. gotcha §55). Une correction
    /// = une nouvelle écriture wallet, jamais un update rétroactif du montant.
    ///
    /// Modèle de frais (spec §4.4) : le SchoolWallet.AvailableBalance est DÉJÀ
    /// net de payout (prélèvement à la source au payin). L'école retire
    /// <see cref="AmountFcfa"/> = ce qu'elle voit dans son wallet = ce que le
    /// bénéficiaire reçoit. On envoie à SenePay <see cref="SepayAmountFcfa"/> =
    /// AmountFcfa / (1 − 0,0177) majoré, pour que les frais opérateur 1,77 %
    /// soient absorbés et que le bénéficiaire reçoive exactement AmountFcfa.
    /// </summary>
    public class Withdrawal
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        /// <summary>Montant débité du wallet école = net reçu par le bénéficiaire (FCFA).</summary>
        public long AmountFcfa { get; set; }

        /// <summary>Montant majoré réellement envoyé à SenePay (= AmountFcfa / (1 − 0,0177)).</summary>
        public long SepayAmountFcfa { get; set; }

        /// <summary>Frais opérateur prélevés (rempli au webhook depuis fees.provider).</summary>
        public long FeesFcfa { get; set; }

        /// <summary>Net effectivement reçu par le bénéficiaire (webhook net_amount). ≈ AmountFcfa.</summary>
        public long NetReceivedFcfa { get; set; }

        public PaymentOperator Operator { get; set; }

        /// <summary>Coordonnées bénéficiaire saisies manuellement à chaque retrait.</summary>
        public string RecipientName { get; set; } = string.Empty;

        /// <summary>Numéro national 9 chiffres (sans indicatif). On préfixe "221" à l'appel SenePay.</summary>
        public string RecipientPhone { get; set; } = string.Empty;

        public WithdrawalStatus Status { get; set; } = WithdrawalStatus.Initiated;

        /// <summary>Identifiant SenePay du décaissement (rempli après l'appel /payouts).</summary>
        public string? SenePayDisbursementId { get; set; }

        /// <summary>SchoolAdmin qui a initié le retrait (audit).</summary>
        public int InitiatedById { get; set; }

        public string? FailureReason { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? FailedAt { get; set; }
    }
}
