using System.Text.Json;
using Idara.API.Common.Observability;
using Idara.API.DTOs.Observability;
using Microsoft.Extensions.Options;

namespace Idara.API.Services.Observability
{
    public interface IServerLogSearchService
    {
        /// <summary>Répertoire effectivement utilisé pour les journaux.</summary>
        string Directory { get; }

        /// <summary>
        /// Cherche des lignes de journal, par code de corrélation et/ou par
        /// utilisateur et/ou par école, sur une fenêtre de jours bornée.
        /// </summary>
        Task<ServerLogSearchResultDto> SearchAsync(
            string? traceCode,
            int? userId,
            int? schoolId,
            string? level,
            int days,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Lit les journaux JSON quotidiens écrits par Serilog.
    ///
    /// <para><b>Pourquoi cette page existe.</b> Pour qu'un appel d'un directeur
    /// (« j'ai le code IDR-7K2MQ4 ») ne demande pas d'ouvrir un terminal SSH.
    /// C'est la différence entre un diagnostic fait depuis un téléphone en deux
    /// minutes et un diagnostic remis au lendemain.</para>
    ///
    /// <para><b>Ce service est le SEUL endroit qui touche aux fichiers</b>, et
    /// c'est une exception assumée à la règle « la recherche interroge
    /// PostgreSQL » (§4.8, garde-fou 2). Les garanties qui la rendent acceptable
    /// sur deux cœurs ARM partagés avec l'API et PostgreSQL :</para>
    /// <list type="bullet">
    ///   <item>fenêtre de jours <b>bornée</b> (7 par défaut, 31 au maximum) ;</item>
    ///   <item>lecture <b>en flux</b>, ligne par ligne, jamais le fichier entier
    ///         en mémoire ;</item>
    ///   <item>plafond de lignes retournées, et le résultat annonce lui-même
    ///         qu'il est partiel ;</item>
    ///   <item>plafond d'octets parcourus par fichier, pour qu'un fichier gonflé
    ///         par une boucle d'erreur ne fasse pas tousser le serveur ;</item>
    ///   <item>réservé au SuperAdmin (contrôlé par le contrôleur).</item>
    /// </list>
    /// </summary>
    public class ServerLogSearchService : IServerLogSearchService
    {
        private const int MaxEntries = 300;
        private const int MaxDays = 31;
        private const long MaxBytesPerFile = 48L * 1024 * 1024;

        private readonly ILogger<ServerLogSearchService> _logger;

        public string Directory { get; }

        public ServerLogSearchService(
            IOptions<Options.ObservabilitySettings> settings,
            IHostEnvironment env,
            ILogger<ServerLogSearchService> logger)
        {
            _logger = logger;
            Directory = SerilogSetup.ResolveLogDirectory(settings.Value, env.ContentRootPath);
        }

        public async Task<ServerLogSearchResultDto> SearchAsync(
            string? traceCode,
            int? userId,
            int? schoolId,
            string? level,
            int days,
            CancellationToken ct = default)
        {
            var result = new ServerLogSearchResultDto { Directory = Directory };

            var window = days <= 0 ? 7 : Math.Min(days, MaxDays);
            var normalizedTrace = TraceCode.TryNormalize(traceCode);
            // Un code mal recopié ne doit pas se transformer en balayage de tout
            // le journal : on le dit franchement plutôt que de renvoyer du bruit.
            if (!string.IsNullOrWhiteSpace(traceCode) && normalizedTrace == null) return result;

            var wantedLevel = string.IsNullOrWhiteSpace(level) ? null : level.Trim();

            List<FileInfo> files;
            try
            {
                var dir = new DirectoryInfo(Directory);
                if (!dir.Exists) return result;
                files = dir.GetFiles("idara-*.json")
                    .OrderByDescending(f => f.Name)
                    .Take(window)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[observability] Répertoire de journaux illisible : {Dir}", Directory);
                return result;
            }

            foreach (var file in files)
            {
                if (result.Entries.Count >= MaxEntries)
                {
                    result.Truncated = true;
                    break;
                }
                result.FilesScanned.Add(file.Name);
                await ScanFileAsync(file, normalizedTrace, userId, schoolId, wantedLevel, result, ct);
            }

            // Le plus récent d'abord : c'est l'ordre dans lequel on cherche un
            // incident dont on vient d'apprendre l'existence.
            result.Entries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            return result;
        }

        private async Task ScanFileAsync(
            FileInfo file,
            string? trace,
            int? userId,
            int? schoolId,
            string? level,
            ServerLogSearchResultDto result,
            CancellationToken ct)
        {
            try
            {
                // ⚠️ FileShare.ReadWrite est INDISPENSABLE : le fichier du jour est
                // ouvert en écriture par Serilog au même moment. Sans ce partage,
                // toute recherche portant sur aujourd'hui — c'est-à-dire le cas
                // normal — échouerait sur un fichier verrouillé.
                await using var stream = new FileStream(
                    file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);

                long readBytes = 0;
                while (!reader.EndOfStream)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break;

                    readBytes += line.Length;
                    if (readBytes > MaxBytesPerFile)
                    {
                        result.Truncated = true;
                        return;
                    }
                    if (line.Length < 2) continue;

                    var entry = TryParse(line, trace, userId, schoolId, level);
                    if (entry == null) continue;

                    result.Entries.Add(entry);
                    if (result.Entries.Count >= MaxEntries)
                    {
                        result.Truncated = true;
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[observability] Lecture impossible : {File}", file.Name);
            }
        }

        /// <summary>
        /// Parse une ligne au format CLEF de Serilog et applique les filtres.
        ///
        /// <para>Rappel du format : <c>@t</c> horodatage, <c>@m</c> message rendu,
        /// <c>@mt</c> gabarit, <c>@l</c> niveau (<b>absent quand le niveau est
        /// Information</b> — c'est le piège classique de ce format), <c>@x</c>
        /// exception. Les autres clés sont nos propres propriétés structurées.</para>
        /// </summary>
        private static ServerLogEntryDto? TryParse(
            string line, string? trace, int? userId, int? schoolId, string? level)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                var lineTrace = GetString(root, "Trace");
                if (trace != null && !string.Equals(lineTrace, trace, StringComparison.Ordinal)) return null;

                var lineUser = GetInt(root, "UserId");
                if (userId != null && lineUser != userId) return null;

                var lineSchool = GetInt(root, "SchoolId");
                if (schoolId != null && lineSchool != schoolId) return null;

                // « @l » n'est écrit que pour les niveaux différents d'Information.
                var lineLevel = GetString(root, "@l") ?? "Information";
                if (level != null && !lineLevel.StartsWith(level, StringComparison.OrdinalIgnoreCase)) return null;

                var timestamp = DateTime.TryParse(GetString(root, "@t"), null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal |
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var t)
                    ? t
                    : DateTime.MinValue;

                return new ServerLogEntryDto
                {
                    Timestamp = timestamp,
                    Level = lineLevel,
                    Message = GetString(root, "@m") ?? GetString(root, "@mt") ?? string.Empty,
                    Trace = lineTrace,
                    UserId = lineUser,
                    SchoolId = lineSchool,
                    Role = GetString(root, "Role"),
                    RequestPath = GetString(root, "RequestPath"),
                    RequestMethod = GetString(root, "RequestMethod"),
                    StatusCode = GetInt(root, "StatusCode"),
                    ElapsedMs = GetDouble(root, "Elapsed"),
                    Exception = Truncate(GetString(root, "@x"), 4000),
                };
            }
            catch
            {
                // Ligne tronquée (écriture en cours au moment de la lecture) :
                // on l'ignore. Une ligne illisible ne doit jamais faire échouer
                // toute une recherche.
                return null;
            }
        }

        private static string? GetString(JsonElement root, string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static int? GetInt(JsonElement root, string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
                ? i : null;

        private static double? GetDouble(JsonElement root, string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)
                ? d : null;

        private static string? Truncate(string? value, int max)
        {
            if (string.IsNullOrEmpty(value)) return null;
            return value.Length <= max ? value : value[..max];
        }
    }
}
