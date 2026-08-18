namespace Idara.API.Options
{
    /// <summary>
    /// Configuration du fournisseur SMS Orange SMS Pro (Sonatel, compte GOLD
    /// MBK03581 — API HTTP, cf. CLAUDE.md §117). Secrets (Token, PrivateKey)
    /// déposés via user-secrets en dev et /etc/idara/idara.env en prod
    /// (<c>OrangeSms__Token</c>, <c>OrangeSms__PrivateKey</c>…), jamais commités.
    /// </summary>
    public class OrangeSmsSettings
    {
        public const string SectionName = "OrangeSms";

        /// <summary>Endpoint d'envoi (POST ou GET, un seul destinataire par appel).</summary>
        public string BaseUrl { get; set; } = "https://api.orangesmspro.sn:8443/api";

        /// <summary>
        /// Login du compte portail (Basic auth <c>login:token</c>). Distinct du
        /// token — c'est l'identifiant utilisateur de orangesmspro.sn.
        /// </summary>
        public string Login { get; set; } = string.Empty;

        /// <summary>Token API (Paramètres → API du portail). Secret, sert aussi de
        /// mot de passe Basic auth.</summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>Clé privée (Paramètres → API). Secret, sert à signer chaque
        /// requête (HMAC-SHA1 → paramètre <c>key</c> à usage unique).</summary>
        public string PrivateKey { get; set; } = string.Empty;

        /// <summary>
        /// Signature = Sender ID affiché à la réception ("Idara"). ⚠️ Elle doit
        /// être créée ET validée dans l'application Alert SMS du portail (la liste
        /// est propre à chaque application) — en attente ⇒ erreur 102 sur TOUS les
        /// envois, ce qui n'est PAS un bug de code.
        /// </summary>
        public string Signature { get; set; } = "Idara";

        /// <summary>Objet du message (paramètre requis par l'API, non affiché au
        /// destinataire). Entre aussi dans la chaîne signée.</summary>
        public string Subject { get; set; } = "Idara";

        /// <summary>Vrai quand tout ce qu'il faut pour envoyer est configuré —
        /// sinon le service no-op proprement (déploiement sûr avant config).</summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Login)
            && !string.IsNullOrWhiteSpace(Token)
            && !string.IsNullOrWhiteSpace(PrivateKey)
            && !string.IsNullOrWhiteSpace(Signature);
    }
}
