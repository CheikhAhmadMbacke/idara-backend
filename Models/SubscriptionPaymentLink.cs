namespace Idara.API.Models
{
    /// <summary>
    /// 🔗 Le lien de paiement PERMANENT de l'abonnement d'une école — ce que le
    /// SMS de relance met dans la poche du directeur.
    ///
    /// <para><b>Pourquoi il existe, en un chiffre.</b> Sur les 7 écoles de la
    /// plateforme, <b>4 n'ont jamais encaissé un franc</b> par Idara et une
    /// cinquième n'a encaissé que deux paiements : elles encaissent en espèces.
    /// Leur wallet est vide et le restera, donc le prélèvement automatique ne
    /// peut structurellement pas les atteindre. Pour elles, ce lien n'est pas un
    /// filet de secours — c'est le <b>chemin principal</b>.</para>
    ///
    /// <para>🔴 <b>Le lien PAIE LA FACTURE, il ne recharge pas le wallet</b>
    /// (décision de Cheikh, 2026-09-19). Passer par le wallet aurait deux
    /// défauts : l'école paierait les frais de recharge en plus, et le crédit
    /// pourrait être mangé par autre chose entre son arrivée et le prélèvement.
    /// Le paiement porte donc <see cref="Enums.PaymentPurpose.Subscription"/> et
    /// ne crédite aucun solde — c'est un revenu de la plateforme, il entre
    /// directement dans P (§112), exactement comme l'achat de pages (§233).</para>
    ///
    /// <para><b>Permanent, sans montant figé</b>, comme le lien des parents
    /// (§161) : à chaque ouverture, la page recalcule ce que l'école doit
    /// réellement. Il ne périme donc jamais, se réutilise mois après mois, et
    /// dit « rien à payer » quand l'abonnement est à jour. Un lien Wave figé,
    /// lui, aurait porté un montant périmé dès le mois suivant.</para>
    ///
    /// <para>🔑 <b>Il doit rester ouvrable sans compte.</b> C'est toute sa
    /// raison d'être : une école dont l'accès est bloqué au 15 ne peut plus rien
    /// faire dans l'application, mais elle doit pouvoir payer. Une page publique
    /// est le seul moyen d'y parvenir — d'où le jeton opaque de 128 bits, qui
    /// est la seule authentification.</para>
    ///
    /// <para>UN lien actif par école : régénérer renvoie le même. Révoquer
    /// (<see cref="RevokedAt"/>) le rend mort et permet d'en créer un nouveau.</para>
    /// </summary>
    public class SubscriptionPaymentLink
    {
        public int Id { get; set; }

        /// <summary>Jeton opaque (GUID v4 sans tirets, 32 hexa). Unique.</summary>
        public string Token { get; set; } = string.Empty;

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        public DateTime CreatedAt { get; set; }

        /// <summary>Première ouverture de la page publique (null = jamais ouvert).</summary>
        /// <remarks>
        /// Distincte de <see cref="LastOpenedAt"/>, et elle répond à la seule
        /// question qui dise si la relance fonctionne : combien de temps s'écoule
        /// entre le SMS et le moment où le directeur clique. Même mesure que pour
        /// le lien des familles.
        /// </remarks>
        public DateTime? FirstOpenedAt { get; set; }

        /// <summary>Dernière ouverture de la page publique.</summary>
        public DateTime? LastOpenedAt { get; set; }

        public int OpenCount { get; set; }

        /// <summary>Dernière fois que le lien a été envoyé par SMS.</summary>
        public DateTime? LastSharedAt { get; set; }

        public DateTime? RevokedAt { get; set; }
    }
}
