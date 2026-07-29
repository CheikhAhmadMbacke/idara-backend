namespace Idara.API.DTOs.Observability
{
    /// <summary>Ligne de la liste des incidents (page SuperAdmin).</summary>
    public class IncidentListItemDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public int Kind { get; set; }
        public string KindLabel { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        public int? UserId { get; set; }
        public string? UserName { get; set; }
        public string Role { get; set; } = string.Empty;
        public int? SchoolId { get; set; }
        public string? SchoolName { get; set; }

        public string Platform { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsResolved { get; set; }
    }

    /// <summary>Détail d'un incident, avec la pile d'appels et la trace serveur.</summary>
    public class IncidentDetailDto : IncidentListItemDto
    {
        public string Device { get; set; } = string.Empty;
        public string LocaleCode { get; set; } = string.Empty;
        public string ExceptionType { get; set; } = string.Empty;
        public string StackTrace { get; set; } = string.Empty;
        public string? RequestTrace { get; set; }
        public string? UserComment { get; set; }

        /// <summary>Chronologie (lot 2). Toujours <c>null</c> pour l'instant.</summary>
        public string? Timeline { get; set; }
    }

    /// <summary>Résultat d'un signalement, renvoyé à l'application.</summary>
    public class IncidentAcceptedDto
    {
        /// <summary>Code retenu — c'est celui qu'il faut afficher à l'utilisateur.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// <c>false</c> quand le plafond journalier est atteint. L'application ne
        /// doit surtout pas présenter ça comme une erreur à l'utilisateur : son
        /// problème est réel, c'est seulement la énième copie du même rapport.
        /// </summary>
        public bool Stored { get; set; }

        /// <summary>
        /// Identifiant en base, pour déclencher l'alerte e-mail après coup.
        /// <b>Usage interne au serveur</b> : sans intérêt pour l'application, qui
        /// n'affiche que le code.
        /// </summary>
        public int? IncidentId { get; set; }
    }

    /// <summary>
    /// Une ligne du journal serveur, lue dans les fichiers JSON quotidiens.
    /// </summary>
    public class ServerLogEntryDto
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Trace { get; set; }
        public int? UserId { get; set; }
        public int? SchoolId { get; set; }
        public string? Role { get; set; }
        public string? RequestPath { get; set; }
        public string? RequestMethod { get; set; }
        public int? StatusCode { get; set; }
        public double? ElapsedMs { get; set; }

        /// <summary>Exception éventuelle, telle qu'écrite par Serilog.</summary>
        public string? Exception { get; set; }
    }

    /// <summary>Réponse de la recherche dans les journaux serveur.</summary>
    public class ServerLogSearchResultDto
    {
        public List<ServerLogEntryDto> Entries { get; set; } = new();

        /// <summary>Fichiers réellement parcourus (transparence du périmètre lu).</summary>
        public List<string> FilesScanned { get; set; } = new();

        /// <summary>
        /// <c>true</c> si le plafond de lignes a été atteint : le résultat est
        /// alors partiel, et il vaut mieux le dire que de laisser croire à
        /// l'exhaustivité.
        /// </summary>
        public bool Truncated { get; set; }

        /// <summary>Répertoire effectivement utilisé, utile au diagnostic prod.</summary>
        public string Directory { get; set; } = string.Empty;
    }
}
