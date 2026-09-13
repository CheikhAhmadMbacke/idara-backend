namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Réglages de paiement lisibles par tout utilisateur authentifié — sert au
    /// client pour ANNONCER l'ordre de grandeur des frais avant de payer.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Le pourcentage est INDICATIF, et il ne peut pas être autre chose.</b>
    /// Les frais du prestataire ne sont pas un pourcentage : il arrondit au franc
    /// à chaque étape, si bien que la majoration effective varie avec le montant
    /// (mesurée : de 5,4 % à 6,1 % de prélèvement selon la taille). Le montant
    /// réellement débité est calculé par le SERVEUR au moment du paiement, et lui
    /// seul fait foi — un client qui recalculerait annoncerait un chiffre que la
    /// page suivante contredirait (§249).
    /// </remarks>
    public class PaymentConfigDto
    {
        /// <summary>
        /// Majoration évaluée sur <see cref="MarkupReferenceFcfa"/>, en %.
        /// Pour une phrase du genre « environ +7,6 % de frais ». Jamais pour calculer.
        /// </summary>
        public double ParentFeePercent { get; set; }

        /// <summary>Montant sur lequel <see cref="ParentFeePercent"/> a été évalué.</summary>
        public long MarkupReferenceFcfa { get; set; }

        /// <summary>
        /// `false` = les commissions du prestataire ne sont pas renseignées et
        /// tout encaissement « frais au payeur » est refusé. Le client peut
        /// l'annoncer au lieu de laisser l'utilisateur buter sur une erreur.
        /// </summary>
        public bool FeesConfigured { get; set; }

        /// <summary>Montant minimum d'un paiement (FCFA).</summary>
        public long MinPayinFcfa { get; set; }
    }
}
