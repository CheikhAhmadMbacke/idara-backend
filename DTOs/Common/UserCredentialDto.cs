namespace Idara.API.DTOs.Common
{
    /// <summary>
    /// Identifiants à communiquer à un utilisateur créé par l'école (parent,
    /// enseignant, personnel) : son numéro + son code à 6 chiffres (= mot de
    /// passe initial) + le message prêt à partager (Copier / WhatsApp / SMS).
    /// Affiché dans un modal côté école — l'envoi SMS automatique est OPTIONNEL,
    /// l'onboarding fonctionne même sans SMS grâce à ce récap.
    /// </summary>
    public class UserCredentialDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;

        /// <summary>Message prêt à l'emploi (même contenu que le SMS de bienvenue).</summary>
        public string Message { get; set; } = string.Empty;
    }
}
