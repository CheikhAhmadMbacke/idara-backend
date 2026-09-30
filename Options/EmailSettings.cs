namespace Idara.API.Options
{
    public class EmailSettings
    {
        public const string SectionName = "EmailSettings";

        public string SmtpServer { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 587;

        /// <summary>
        /// Identifiant presente au serveur SMTP. Depuis la bascule vers
        /// Namecheap Private Email (2026-10-01) il DIFFERE de l'expediteur :
        /// seule la boite <c>contact@pyranil.com</c> ouvre une session, alors
        /// que les messages partent de l'alias <c>idara@pyranil.com</c>.
        /// Laisse vide, on retombe sur <see cref="SenderEmail"/> — le
        /// comportement historique (Gmail, ou les deux sont la meme adresse).
        /// </summary>
        public string SmtpUsername { get; set; } = string.Empty;

        public string SenderEmail { get; set; } = string.Empty;
        public string SenderPassword { get; set; } = string.Empty;
        public string SenderName { get; set; } = "Idara";

        /// <summary>
        /// Adresse a laquelle repondre. Vide, c'est <see cref="SenderEmail"/>.
        /// </summary>
        public string ReplyTo { get; set; } = string.Empty;

        /// <summary>Identifiant reellement utilise pour ouvrir la session.</summary>
        public string EffectiveUsername =>
            string.IsNullOrWhiteSpace(SmtpUsername) ? SenderEmail : SmtpUsername;

        /// <summary>Adresse de reponse reellement posee sur le message.</summary>
        public string EffectiveReplyTo =>
            string.IsNullOrWhiteSpace(ReplyTo) ? SenderEmail : ReplyTo;
    }
}
