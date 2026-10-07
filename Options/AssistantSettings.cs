namespace Idara.API.Options
{
    /// <summary>
    /// Configuration technique de l'assistant IA (2026-10-07).
    ///
    /// <para>La clé : <c>Assistant__ApiKey</c> dans <c>/etc/idara/idara.env</c>.
    /// <b>Vide = on reprend celle de la lecture de cahier</b>
    /// (<c>Vision__ApiKey</c>) : c'est le même compte Anthropic, et poser une
    /// seconde clé pour le même fournisseur n'apporterait qu'un secret de plus à
    /// faire tourner. Les deux restent séparables si un jour il faut cloisonner
    /// les dépenses.</para>
    ///
    /// <para>Le PRIX, les commandes offertes et les plafonds sont des réglages
    /// MÉTIER : ils vivent dans <c>PlatformSettings</c>, modifiables sans
    /// redéploiement.</para>
    /// </summary>
    public class AssistantSettings
    {
        public const string SectionName = "Assistant";

        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Modèle employé. Opus 5.5 : les commandes mêlent français, arabe,
        /// noms wolof et montants — la justesse prime, et l'effort bas tient le
        /// coût. À remesurer sur le registre avant de descendre d'un cran.
        /// </summary>
        public string Model { get; set; } = "claude-opus-5-5";

        /// <summary>Plafond de tokens produits par aller-retour.</summary>
        public int MaxTokens { get; set; } = 4096;

        /// <summary>
        /// Allers-retours maximum avec le modèle pour UNE commande. Au-delà,
        /// l'échange s'arrête : une boucle d'outils est de l'argent qui brûle.
        /// </summary>
        public int MaxRounds { get; set; } = 6;

        /// <summary>Délai d'un appel au modèle.</summary>
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>Messages précédents renvoyés au modèle pour suivre la conversation.</summary>
        public int MaxHistoryMessages { get; set; } = 12;
    }
}
