namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// 🔗 Les adresses publiques qu'on met dans un SMS ou un WhatsApp adressé à
    /// une famille : le lien de paiement permanent, et la page du reçu.
    /// </summary>
    /// <remarks>
    /// <para>Ce sont les deux seules adresses qu'un parent SANS COMPTE peut
    /// ouvrir. Elles se composent à partir de <c>SenePaySettings.PublicBaseUrl</c>,
    /// et non d'une constante : la recette et la production ne portent pas le
    /// même domaine, et un lien figé y enverrait les familles au mauvais endroit.</para>
    ///
    /// <para>⚠️ La longueur de ces adresses est <b>facturée</b> : elles voyagent
    /// dans des SMS dont chaque segment se paie, et l'ajout du lien de paiement
    /// au message d'inscription s'est joué à 36 caractères près (§224). Les
    /// rallonger n'est jamais gratuit.</para>
    /// </remarks>
    public static class PublicLinks
    {
        /// <summary>Le lien de paiement permanent d'un responsable (§161) — 62 caractères.</summary>
        public static string PaymentLink(string baseUrl, string token) =>
            $"{baseUrl.TrimEnd('/')}/pay/link/{token}";

        /// <summary>
        /// Le lien de paiement permanent de l'ABONNEMENT d'une école — 61
        /// caractères, un de moins que celui des familles.
        /// </summary>
        /// <remarks>
        /// ⚠️ Il voyage dans un SMS de relance qui doit tenir en UN segment
        /// (§224) : « abo » plutôt que « abonnement » n'est pas de la coquetterie,
        /// c'est sept caractères repris sur le message.
        /// </remarks>
        public static string SubscriptionLink(string baseUrl, string token) =>
            $"{baseUrl.TrimEnd('/')}/pay/abo/{token}";

        /// <summary>
        /// La page publique du résultat d'un paiement, qui porte son reçu.
        /// <c>null</c> quand le paiement n'a pas de jeton — les paiements
        /// antérieurs à sa mise en place, et eux seuls.
        /// </summary>
        public static string? Receipt(string baseUrl, int paymentId, string? publicResultToken) =>
            string.IsNullOrEmpty(publicResultToken)
                ? null
                : $"{baseUrl.TrimEnd('/')}/pay/{paymentId}/{publicResultToken}";
    }
}
