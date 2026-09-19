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

        // ==================== Canal SMS ====================
        // Ajouté le 2026-09-19 à la demande de Cheikh : « ça me permettra de
        // réagir vite ». L'e-mail se lit quand on ouvre sa boîte ; une réserve de
        // décaissement à sec bloque TOUTES les écoles en attendant.
        //
        // 🔴 Le SMS DOUBLE l'e-mail, il ne le remplace jamais. Il ne porte qu'une
        // phrase et une heure : le montant, le bénéficiaire, la référence et le
        // conseil restent dans l'e-mail et sur l'écran du back-office. Un SMS
        // qu'on ne peut pas lire d'un coup d'œil ne vaut pas mieux que l'e-mail
        // qu'il double — et il coûte un segment de plus.

        /// <summary>Coupe le canal SMS sans toucher aux e-mails.</summary>
        public bool SmsEnabled { get; set; } = true;

        /// <summary>
        /// Numéro qui reçoit les alertes. Vide = aucun SMS (les e-mails
        /// continuent). Accepte toutes les formes : « 77 467 72 17 »,
        /// « +221774677217 », « 00221774677217 ».
        ///
        /// <para>🔴 Ce numéro est FIXE et configuré par nous. C'est ce qui
        /// autorise le canal à échapper aux plafonds par destinataire : un tiers
        /// ne peut pas faire sonner un numéro de son choix, donc il n'y a aucune
        /// surface de « SMS pumping » ici — contrairement aux codes de connexion,
        /// où le numéro est choisi par l'appelant (§259).</para>
        /// </summary>
        public string SmsPhone { get; set; } = string.Empty;

        /// <summary>
        /// Bourse quotidienne du canal, en NOMBRE de SMS.
        ///
        /// <para>C'est ce qui remplace les plafonds dont ce canal est exempté.
        /// Dix messages par jour couvrent très largement la réalité mesurée le
        /// 2026-09-19 (environ 14 alertes par MOIS après regroupement) et bornent
        /// le dégât d'une boucle à une quarantaine de francs. Au-delà, tout reste
        /// écrit en base et part par e-mail : on perd la sonnette, jamais
        /// l'information.</para>
        /// </summary>
        public int SmsMaxPerDay { get; set; } = 10;
    }
}
