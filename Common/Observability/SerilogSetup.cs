using Idara.API.Options;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Idara.API.Common.Observability
{
    /// <summary>
    /// Configuration du journal structuré. Volontairement écrite en C# et non
    /// pilotée par une section <c>Serilog</c> d'<c>appsettings.json</c> : les
    /// niveaux de journal sont un sujet où l'on s'est déjà fait piéger (§112, les
    /// journaux SQL en <c>Information</c> noyaient les lignes applicatives et ont
    /// bloqué le diagnostic trois fois dans la même journée). Les avoir sous les
    /// yeux, commentés, vaut mieux qu'un fichier de configuration muet.
    /// </summary>
    public static class SerilogSetup
    {
        /// <summary>
        /// Résout le répertoire de journaux. Vide dans la configuration =
        /// <c>&lt;ContentRoot&gt;/logs</c>.
        ///
        /// <para>⚠️ En production, <c>&lt;ContentRoot&gt;</c> est
        /// <c>/var/www/idara/api/</c>, <b>écrasé à chaque déploiement</b>
        /// (§31) : poser <c>Observability__LogDirectory=/var/www/idara/logs</c>
        /// dans <c>/etc/idara/idara.env</c> pour que les journaux survivent. Ce
        /// répertoire appartient déjà à l'utilisateur <c>idara</c>, donc aucun
        /// <c>sudo</c> n'est nécessaire — et il n'est pas servi par nginx, ce qui
        /// compte : ces fichiers contiennent des messages d'erreur applicatifs.</para>
        /// </summary>
        public static string ResolveLogDirectory(ObservabilitySettings settings, string contentRoot)
        {
            var fallback = Path.Combine(contentRoot, "logs");
            var configured = settings.LogDirectory?.Trim();
            if (string.IsNullOrEmpty(configured)) return fallback;

            // ⚠️ Garde-fou de sécurité, pas de confort. `wwwroot` est servi
            // publiquement par `UseStaticFiles` (et, en production, directement
            // par nginx). Y écrire les journaux exposerait des messages d'erreur
            // applicatifs — donc potentiellement des noms de parents et
            // d'élèves — à toute personne connaissant l'URL du fichier du jour,
            // qui est prévisible (`idara-20260729.json`). C'est exactement le
            // schéma de la fuite des reçus fermée le 2026-07-28 (§122) : un
            // répertoire servi en statique et des noms de fichiers devinables.
            try
            {
                var wwwroot = Path.GetFullPath(Path.Combine(contentRoot, "wwwroot"));
                var target = Path.GetFullPath(configured);
                if (target.StartsWith(wwwroot, StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine(
                        $"[observability] Observability:LogDirectory pointe sous wwwroot ({target}), " +
                        $"ce qui exposerait les journaux publiquement. Repli sur {fallback}.");
                    return fallback;
                }
            }
            catch
            {
                // Chemin non résolvable : la configuration du sink échouera plus
                // bas et se rabattra sur la console. Rien à décider ici.
            }

            return configured;
        }

        /// <summary>
        /// Journal minimal utilisé le temps que le conteneur d'injection soit prêt
        /// (motif « bootstrap logger » de Serilog). Sans lui, une erreur de
        /// configuration au démarrage — clé JWT trop courte, chaîne de connexion
        /// absente — partirait dans le vide.
        /// </summary>
        public static Serilog.ILogger CreateBootstrapLogger() =>
            new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .CreateBootstrapLogger();

        /// <summary>
        /// Configure le journal définitif. <b>Ne lève jamais</b> : si le répertoire
        /// n'est pas accessible (droits, disque plein, chemin mal configuré), on se
        /// rabat sur la console seule. Un problème de journalisation ne doit pas
        /// empêcher l'API de démarrer — ce serait remplacer un défaut de visibilité
        /// par une panne de service.
        /// </summary>
        public static void Configure(
            LoggerConfiguration config,
            ObservabilitySettings settings,
            string contentRoot,
            bool isDevelopment,
            IHttpContextAccessor httpContextAccessor)
        {
            config
                .MinimumLevel.Information()
                // Ces deux surcharges reprennent EXACTEMENT celles de
                // `Logging:LogLevel` dans appsettings.json. Serilog prend la main
                // sur le filtrage : les omettre ferait revenir les journaux SQL
                // d'EF Core, c'est-à-dire précisément la régression du §112.
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .Enrich.FromLogContext()
                // C'est cet enrichisseur qui comble le « trou n°2 » : sans lui, une
                // ligne de journal ne dit ni qui, ni quelle école, ni quelle requête.
                .Enrich.With(new IdaraLogEnricher(httpContextAccessor));

            // Console → Warning. C'est ce que journald collecte : on n'y garde donc
            // que ce qui mérite un œil. Effet de bord favorable, mesuré le
            // 2026-07-29 : `journald` occupait 320 Mo et va DIMINUER.
            // En développement, la console reste bavarde — c'est là qu'on travaille.
            if (isDevelopment) config.WriteTo.Console();
            else config.WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Warning);

            var directory = ResolveLogDirectory(settings, contentRoot);
            try
            {
                System.IO.Directory.CreateDirectory(directory);

                // RenderedCompactJsonFormatter et non CompactJsonFormatter : il
                // écrit le message DÉJÀ rendu (`@m`) en plus du gabarit. Sans lui,
                // la page SuperAdmin devrait réinterpréter les gabarits à la
                // lecture — du code en plus pour relire nos propres journaux.
                config.WriteTo.File(
                    formatter: new RenderedCompactJsonFormatter(),
                    path: Path.Combine(directory, "idara-.json"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: Math.Max(1, settings.RetainedDays),
                    fileSizeLimitBytes: settings.FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    // Le fichier du jour est lu par la page SuperAdmin pendant que
                    // Serilog y écrit : `shared` garantit un partage propre.
                    shared: true,
                    restrictedToMinimumLevel: LogEventLevel.Information);
            }
            catch (Exception ex)
            {
                // Console seule : dégradé mais fonctionnel. Le message part sur la
                // console immédiatement, avant même que le journal ne soit installé.
                Console.Error.WriteLine(
                    $"[observability] Journal fichier désactivé ({directory}) : {ex.Message}");
            }
        }
    }
}
