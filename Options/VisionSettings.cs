namespace Idara.API.Options
{
    /// <summary>
    /// Configuration de la lecture de cahiers par l'IA (API Claude).
    ///
    /// La clé est un secret : user-secrets en dev (<c>Vision:ApiKey</c>),
    /// <c>/etc/idara/idara.env</c> en prod (<c>Vision__ApiKey</c>). Jamais
    /// commitée, jamais journalisée.
    ///
    /// <para>⚠️ Rappel du §190 : la fabrique de <c>HttpClient</c> de .NET
    /// journalise l'URI complète de chaque appel. C'est ce qui avait fait fuiter
    /// le token SMS, parce que l'API Orange porte ses identifiants en query
    /// string. Ici la clé voyage dans un EN-TÊTE, que la fabrique ne journalise
    /// pas — mais la règle reste : ne jamais mettre un secret dans une URL, et
    /// ne jamais journaliser une requête entière.</para>
    /// </summary>
    public class VisionSettings
    {
        public const string SectionName = "Vision";

        /// <summary>Clé API Anthropic. Vide = fonctionnalité NO-OP (§89).</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Modèle employé. Opus 5 par défaut : la lecture porte sur de
        /// l'écriture manuscrite, en français et en arabe, avec des noms wolof
        /// et des numéros de téléphone — c'est exactement là où l'écart de
        /// justesse se paie. Réglable pour pouvoir mesurer un modèle moins cher
        /// sur de vraies pages avant de trancher.
        /// </summary>
        public string Model { get; set; } = "claude-opus-5";

        /// <summary>
        /// Plafond de tokens produits. Une école de 300 élèves fait ~12 000
        /// tokens de sortie ; on laisse de la marge pour ne pas tronquer une
        /// liste au milieu — une réponse coupée est inexploitable ET payée.
        /// </summary>
        public int MaxTokens { get; set; } = 32000;

        /// <summary>
        /// Délai d'attente d'un appel. Douze pages manuscrites demandent du
        /// temps ; couper trop tôt fait payer un appel dont on jette le résultat.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>Vrai si la lecture par IA est réellement configurée.</summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
    }
}
