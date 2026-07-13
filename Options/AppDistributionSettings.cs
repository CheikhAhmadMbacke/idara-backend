namespace Idara.API.Options
{
    /// <summary>
    /// Distribution de l'app Android hors Play Store (téléchargement + auto-MàJ).
    /// Le CI GitHub Actions publie l'APK + un manifest JSON sur le VPS ; le
    /// backend lit ce manifest pour renvoyer la version courante à l'app et au site.
    /// </summary>
    public class AppDistributionSettings
    {
        public const string SectionName = "AppDistribution";

        /// <summary>
        /// Chemin disque du manifest publié par le CI (JSON :
        /// versionCode / versionName / apkUrl / apkUrlStable / changelog / publishedAt).
        /// En dev (Windows) ce chemin n'existe pas → aucune MàJ signalée (dégradé
        /// propre). En prod, le CI l'écrit dans le dossier servi par nginx.
        /// </summary>
        public string VersionManifestPath { get; set; } =
            "/var/www/idara/downloads/idara-app-version.json";
    }
}
