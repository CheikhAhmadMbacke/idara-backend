namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Ce qu'un retrait coûtera, calculé <b>par le serveur</b>.
    /// </summary>
    /// <remarks>
    /// 🔑 <b>Il existe pour que l'application n'ait aucun calcul de frais à
    /// faire.</b> Les frais ne sont pas un pourcentage (§256) : ce sont deux
    /// arrondis au franc qui s'enchaînent. Une application qui multiplierait par
    /// 1 % tomberait, un franc par-ci un franc par-là, sur un chiffre différent
    /// de celui qui sera réellement débité — et l'écran annoncerait autre chose
    /// que ce qui se passe.
    ///
    /// <para>Les deux champs dépendants de l'écran de retrait (« je retire de
    /// mon solde » / « le bénéficiaire reçoit ») se remplissent donc d'un
    /// aller-retour, pas d'une multiplication.</para>
    /// </remarks>
    public class WithdrawalQuoteDto
    {
        /// <summary>Ce que touchera le bénéficiaire.</summary>
        public long ReceiveFcfa { get; set; }

        /// <summary>Les frais de décaissement, prélevés EN SUS du montant envoyé.</summary>
        public long FeesFcfa { get; set; }

        /// <summary>Ce qui sortira du portefeuille = <see cref="ReceiveFcfa"/> + <see cref="FeesFcfa"/>.</summary>
        public long DebitFcfa { get; set; }

        /// <summary>
        /// Le plus grand montant que le bénéficiaire peut recevoir avec la poche
        /// choisie, frais compris. C'est la réponse du bouton « Tout ».
        /// </summary>
        public long MaxReceivableFcfa { get; set; }

        /// <summary>Solde de la poche interrogée (total, paiements ou dons).</summary>
        public long SourceBalanceFcfa { get; set; }

        /// <summary>Montant minimum d'un retrait, tel que la plateforme le fixe.</summary>
        public long MinReceiveFcfa { get; set; }

        /// <summary>
        /// Le motif tel que le BÉNÉFICIAIRE le lira sur son téléphone
        /// (« Retrait Ecole de demonstration »).
        /// </summary>
        /// <remarks>
        /// 🔑 Il est composé ICI, par <c>PayoutReason</c>, et non recopié dans
        /// l'application : la règle (40 caractères, sans accent, le nom de l'école
        /// et non son numéro interne) a UN seul auteur. L'écran de vérification
        /// le montre parce que c'est souvent la seule chose qu'un bénéficiaire
        /// lit quand l'argent arrive.
        /// </remarks>
        public string PaymentReasonPreview { get; set; } = string.Empty;

        /// <summary>
        /// Le devis tient-il dans la poche choisie ? <c>false</c> n'est pas une
        /// erreur : l'écran s'en sert pour éteindre son bouton sans attendre le
        /// refus du serveur.
        /// </summary>
        public bool Affordable { get; set; }
    }
}
