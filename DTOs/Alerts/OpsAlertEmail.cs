namespace Idara.API.DTOs.Alerts
{
    /// <summary>
    /// Contenu d'une alerte d'exploitation prêt à mettre en page. Séparé du
    /// modèle en base pour la même raison que <c>IncidentAlertEmail</c> : la
    /// composition de l'e-mail se vérifie alors sans SMTP ni base (§133).
    /// </summary>
    public class OpsAlertEmail
    {
        /// <summary>Gravité, qui décide de la couleur du bandeau : « urgent »
        /// (rouge) quand l'argent est bloqué ou que la dépense dérape, sinon
        /// « attention » (ambre).</summary>
        public bool Urgent { get; set; }

        /// <summary>Intitulé de la nature de l'événement, en clair.</summary>
        public string KindLabel { get; set; } = string.Empty;

        /// <summary>Titre principal du bandeau — une phrase complète.</summary>
        public string Heading { get; set; } = string.Empty;

        /// <summary>Objet de l'e-mail.</summary>
        public string Subject { get; set; } = string.Empty;

        /// <summary>Faits, dans l'ordre de lecture. Le premier est mis en gras :
        /// c'est celui qui déclenche l'action (§177 — nommer QUI est concerné).</summary>
        public List<(string Label, string Value)> Facts { get; set; } = new();

        /// <summary>Ce qu'il y a à faire. Affiché en encadré. Null si le
        /// diagnostic reste ouvert.</summary>
        public string? Advice { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
