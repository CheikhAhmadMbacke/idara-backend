namespace Idara.API.Enums
{
    /// <summary>
    /// Moyen de paiement d'un Payment (ou opérateur d'un Withdrawal).
    /// Mobile Money Sénégal : Wave + Orange Money uniquement (Free Money /
    /// Expresso volontairement exclus), plus les espèces encaissées au daara.
    /// </summary>
    public enum PaymentOperator
    {
        Wave = 0,
        Orange = 1,

        /// <summary>
        /// 💵 <b>Espèces encaissées au daara</b> — jamais un appel au prestataire.
        /// </summary>
        /// <remarks>
        /// <para>La plupart des inscriptions se règlent sur place, en liquide, le
        /// jour où l'enfant arrive. Sans ce moyen, la facture restait impayée :
        /// l'élève apparaissait « en retard » et sa famille recevait un SMS de
        /// relance <b>alors qu'elle avait payé</b>.</para>
        ///
        /// <para>🔴 Un paiement en espèces solde la facture et alimente la
        /// <b>caisse</b> ; il ne crédite <b>JAMAIS</b> le wallet du prestataire —
        /// cet argent n'y est pas. L'y ajouter ferait croire à l'école qu'elle
        /// peut le retirer, et le virement échouerait faute de réserve (§111),
        /// en cassant au passage l'invariant de réconciliation R = D + P (§112).</para>
        ///
        /// <para>⚠️ Valeur persistée : ne jamais réordonner.</para>
        /// </remarks>
        Cash = 2
    }
}
