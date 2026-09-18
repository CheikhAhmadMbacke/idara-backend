namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Réglages de paiement lisibles par tout utilisateur authentifié — sert au
    /// client pour ANNONCER l'ordre de grandeur des frais avant de payer.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Le pourcentage est INDICATIF, et il ne peut pas être autre chose.</b>
    /// Les frais du prestataire ne sont pas un pourcentage : il arrondit au franc
    /// à chaque étape, si bien que la majoration effective varie avec le montant.
    /// Le montant réellement débité est calculé par le SERVEUR au moment du
    /// paiement, et lui seul fait foi — un client qui recalculerait annoncerait
    /// un chiffre que la page suivante contredirait (§249).
    ///
    /// <para>🔑 <b>D'où le devis.</b> Le taux affiché est rond et lisible
    /// (« 1 % ») ; ce qui est exact, c'est le MONTANT rendu par
    /// <c>GET /api/payment-config/quote</c>. Un écran doit montrer les deux : le
    /// taux se retient, le montant s'engage.</para>
    /// </remarks>
    public class PaymentConfigDto
    {
        /// <summary>
        /// Taux NOMINAL du prestataire à l'encaissement, en % — « 1 ».
        /// Pour une phrase du genre « 1 % de frais ». Jamais pour calculer.
        /// </summary>
        public double ParentFeePercent { get; set; }

        /// <summary>
        /// `false` = les commissions du prestataire ne sont pas renseignées et
        /// tout encaissement « frais au payeur » est refusé. Le client peut
        /// l'annoncer au lieu de laisser l'utilisateur buter sur une erreur.
        /// </summary>
        public bool FeesConfigured { get; set; }

        /// <summary>Montant minimum d'un paiement (FCFA).</summary>
        public long MinPayinFcfa { get; set; }
    }

    /// <summary>
    /// Ce que coûte un encaissement, au franc près. Rendu par
    /// <c>GET /api/payment-config/quote?target=N</c>.
    /// </summary>
    /// <remarks>
    /// 🔴 Les trois montants viennent du serveur, et <see cref="FeesFcfa"/> est
    /// une SOUSTRACTION, jamais l'application d'un taux : c'est ce qui le rend
    /// exact quand le taux affiché, lui, est arrondi pour être lisible.
    /// </remarks>
    public class PaymentQuoteDto
    {
        /// <summary>Ce que l'établissement doit encaisser.</summary>
        public long TargetFcfa { get; set; }

        /// <summary>Ce qui sera débité au payeur.</summary>
        public long ChargeFcfa { get; set; }

        /// <summary>La différence — ce qu'il faut AFFICHER.</summary>
        public long FeesFcfa { get; set; }

        /// <summary>`false` = grille non renseignée, aucun montant à annoncer.</summary>
        public bool FeesConfigured { get; set; }
    }
}
