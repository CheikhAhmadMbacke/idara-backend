using Idara.API.Enums;

namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Un virement reçu par un bénéficiaire (enseignant / surveillant / personnel)
    /// — vue en LECTURE de son espace « Mes paiements reçus » (F3). C'est un
    /// <see cref="Models.Withdrawal"/> sortant du daara dont le numéro destinataire
    /// correspond au compte connecté. Aucun solde, aucune écriture côté bénéficiaire.
    /// </summary>
    public class BeneficiaryTransferDto
    {
        public int Id { get; set; }

        /// <summary>Daara qui a émis le virement.</summary>
        public string SchoolName { get; set; } = string.Empty;

        /// <summary>Montant net reçu par le bénéficiaire (FCFA).</summary>
        public long AmountFcfa { get; set; }

        public PaymentOperator Operator { get; set; }

        public TransferCategory Category { get; set; }

        /// <summary>Libellé affichable de la nature du virement (catégorie FR ou texte libre).</summary>
        public string CategoryLabel { get; set; } = string.Empty;

        public WithdrawalStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
