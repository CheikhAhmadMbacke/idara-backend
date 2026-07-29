namespace Idara.API.DTOs.Observability
{
    /// <summary>
    /// Contenu d'une alerte par e-mail. Rassemble tout ce qu'il faut pour agir
    /// <b>sans ouvrir l'application</b> — y compris le numéro de téléphone de la
    /// personne, pour pouvoir la rappeler.
    ///
    /// <para>C'est ce dernier point qui renverse la démarche : le public d'Idara
    /// ne signale pas les problèmes, il abandonne l'écran. Avec le numéro dans
    /// l'alerte, ce n'est plus lui qui nous contacte, c'est nous qui l'appelons.</para>
    /// </summary>
    public class IncidentAlertEmail
    {
        public string Code { get; set; } = string.Empty;
        public string KindLabel { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        public string PersonName { get; set; } = string.Empty;
        public string RoleLabel { get; set; } = string.Empty;

        /// <summary>Numéro à rappeler. Le renseignement le plus utile de l'alerte.</summary>
        public string PhoneNumber { get; set; } = string.Empty;

        public string SchoolName { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ExceptionType { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public string Device { get; set; } = string.Empty;
        public string LocaleCode { get; set; } = string.Empty;
        public string? UserComment { get; set; }
        public string? RequestTrace { get; set; }
        public string StackTrace { get; set; } = string.Empty;

        /// <summary>
        /// Nombre de personnes touchées par le même défaut dans les dernières
        /// 24 h. « 1 » se lit très différemment de « 14 » : c'est ce chiffre qui
        /// dit s'il faut agir tout de suite.
        /// </summary>
        public int SimilarLast24h { get; set; } = 1;
    }
}
