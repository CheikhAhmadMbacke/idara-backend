namespace Idara.API.Options
{
    /// <summary>
    /// Configuration du fournisseur SMS Africa's Talking.
    /// Secrets (ApiKey) déposés via user-secrets en dev et /etc/idara/idara.env
    /// en prod (clé <c>AfricasTalking__ApiKey</c>), jamais commités.
    /// </summary>
    public class AfricasTalkingSettings
    {
        public const string SectionName = "AfricasTalking";

        /// <summary>
        /// Base URL de l'API. Sandbox : https://api.sandbox.africastalking.com
        /// (bac à sable gratuit, simulateur). Prod : https://api.africastalking.com
        /// </summary>
        public string BaseUrl { get; set; } = "https://api.sandbox.africastalking.com";

        /// <summary>
        /// Nom d'utilisateur du compte AT. Vaut littéralement <c>"sandbox"</c>
        /// pour le bac à sable ; en prod c'est le username de l'app de production.
        /// </summary>
        public string Username { get; set; } = "sandbox";

        /// <summary>Clé API AT (générée dans Settings → API Key). Secret.</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Sender ID alphanumérique affiché à la réception (ex. "Idara").
        /// VIDE en sandbox (ignoré) et tant que les opérateurs sénégalais n'ont
        /// pas validé la demande — dans ce cas les SMS partent d'un numéro court
        /// partagé. Une fois "Idara" approuvé, le poser ici (prod).
        /// </summary>
        public string SenderId { get; set; } = string.Empty;
    }
}
