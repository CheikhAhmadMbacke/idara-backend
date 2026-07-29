namespace Idara.API.Options
{
    /// <summary>
    /// Réglages du journal structuré et de la réception des incidents client
    /// (lot 1 du chantier observabilité, 2026-07-29).
    ///
    /// <para><b>Aucune variable d'environnement n'est obligatoire</b> : tout a un
    /// défaut sûr. En production on posera seulement
    /// <c>Observability__LogDirectory</c> pour que les journaux survivent aux
    /// déploiements (le dossier <c>api/</c> est écrasé à chaque <c>scp</c>,
    /// cf. CLAUDE.md §31).</para>
    /// </summary>
    public class ObservabilitySettings
    {
        public const string SectionName = "Observability";

        /// <summary>
        /// Répertoire des journaux JSON quotidiens. Vide = <c>&lt;ContentRoot&gt;/logs</c>.
        ///
        /// <para>⚠️ Ne JAMAIS pointer vers un répertoire servi par nginx : ces
        /// fichiers contiennent des messages d'erreur applicatifs, donc
        /// potentiellement des données personnelles (cf. §4.7 du plan).</para>
        /// </summary>
        public string LogDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Nombre de fichiers quotidiens conservés. 30 jours, aligné sur la
        /// rétention annoncée dans la politique de confidentialité.
        /// </summary>
        public int RetainedDays { get; set; } = 30;

        /// <summary>
        /// Plafond de taille par fichier quotidien. Garde-fou de dernier
        /// recours : à ~430 requêtes/jour on écrit ~200 Ko/jour, mais une boucle
        /// d'erreur ne doit pas pouvoir remplir les 33 Go libres du VPS.
        /// </summary>
        public long FileSizeLimitBytes { get; set; } = 64L * 1024 * 1024;

        /// <summary>
        /// Rétention des chronologies d'incident en base, purgées au démarrage
        /// (motif <c>IdempotencyRecord</c>).
        /// </summary>
        public int IncidentRetentionDays { get; set; } = 30;

        /// <summary>
        /// Signalements acceptés par utilisateur et par jour. Sans plafond, un
        /// bug en boucle inonderait la base — l'emballement du 2026-07-02 (7 push
        /// d'échec en 1 h 30, §111) a montré que ce n'est pas théorique.
        /// </summary>
        public int MaxIncidentsPerUserPerDay { get; set; } = 5;

        /// <summary>
        /// Plafond global journalier, toutes écoles confondues : protège le
        /// disque même si des dizaines d'appareils rencontrent le même bug.
        /// </summary>
        public int MaxIncidentsPerDay { get; set; } = 500;
    }
}
