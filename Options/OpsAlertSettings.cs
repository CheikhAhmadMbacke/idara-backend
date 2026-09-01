namespace Idara.API.Options
{
    /// <summary>
    /// Réglages des alertes d'exploitation (dépense SMS, échecs de retrait).
    ///
    /// <para><b>Aucune variable d'environnement n'est obligatoire</b> : tout a un
    /// défaut sûr et le destinataire se déduit tout seul de la base. C'est
    /// délibéré — un dispositif d'alerte qui exige une configuration est un
    /// dispositif qui reste éteint (même raisonnement que
    /// <see cref="ObservabilitySettings"/>).</para>
    /// </summary>
    public class OpsAlertSettings
    {
        public const string SectionName = "OpsAlerts";

        /// <summary>Coupe toutes les alertes d'exploitation d'un coup.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Destinataire. Vide = repli sur les comptes SuperAdmin de la base, puis
        /// sur <c>SuperAdmin:Email</c>.
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Fenêtre de regroupement : une même clé n'envoie qu'un e-mail par
        /// tranche. 30 minutes et non 60 comme les incidents clients — une
        /// réserve de décaissement à sec ou une dépense SMS qui s'emballe se
        /// règlent à l'heure près, pas à la demi-journée.
        /// </summary>
        public int GroupingMinutes { get; set; } = 30;

        /// <summary>
        /// Plafond d'e-mails par jour. Garde-fou de dernier recours : se faire
        /// limiter par Gmail ferait perdre AUSSI les e-mails métier (identifiants,
        /// factures d'abonnement).
        /// </summary>
        public int MaxEmailsPerDay { get; set; } = 30;

        /// <summary>
        /// Rétention du journal d'alertes, purgé au démarrage. Plus long que les
        /// incidents clients : une alerte de dépense SMS doit pouvoir se relire
        /// en face de la facture Sonatel du mois suivant.
        /// </summary>
        public int RetentionDays { get; set; } = 120;
    }
}
