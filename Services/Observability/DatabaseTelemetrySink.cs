using Idara.API.Common.Observability;
using Idara.API.Data;
using Idara.API.DTOs.Observability;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Services.Observability
{
    /// <summary>
    /// Stocke les incidents dans PostgreSQL, avec les plafonds qui empêchent
    /// l'endpoint de réception de devenir un vecteur de saturation du disque.
    /// </summary>
    public class DatabaseTelemetrySink : ITelemetrySink
    {
        private readonly AppDbContext _db;
        private readonly Options.ObservabilitySettings _settings;
        private readonly ILogger<DatabaseTelemetrySink> _logger;

        public DatabaseTelemetrySink(
            AppDbContext db,
            IOptions<Options.ObservabilitySettings> settings,
            ILogger<DatabaseTelemetrySink> logger)
        {
            _db = db;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<IncidentAcceptedDto> RecordAsync(
            ReportIncidentDto dto,
            int? userId,
            int? schoolId,
            string role,
            CancellationToken ct = default)
        {
            // Le code proposé par l'application est retenu s'il est au bon format :
            // c'est celui qu'elle a DÉJÀ affiché à l'utilisateur, en changer ferait
            // que le code dicté au téléphone ne correspondrait à rien.
            var code = TraceCode.TryNormalize(dto.Code) ?? TraceCode.New();

            var result = new IncidentAcceptedDto { Code = code, Stored = false };

            // ---- Plafonds -------------------------------------------------
            // Comptés EN BASE et non dans le cache mémoire : celui-ci est remis à
            // zéro à chaque déploiement (§92), ce qui rendrait un plafond
            // journalier illusoire. Deux COUNT sur colonnes indexées, à
            // 0,005 requête/seconde : hors sujet côté coût.
            var since = DateTime.UtcNow.Date;
            try
            {
                if (userId != null)
                {
                    var perUser = await _db.ClientIncidents
                        .CountAsync(i => i.UserId == userId && i.CreatedAt >= since, ct);
                    if (perUser >= _settings.MaxIncidentsPerUserPerDay)
                    {
                        _logger.LogInformation(
                            "[observability] Plafond journalier atteint pour l'utilisateur {UserId} ({Count}) — incident {Code} non stocké.",
                            userId, perUser, code);
                        return result;
                    }
                }

                var total = await _db.ClientIncidents.CountAsync(i => i.CreatedAt >= since, ct);
                if (total >= _settings.MaxIncidentsPerDay)
                {
                    _logger.LogWarning(
                        "[observability] Plafond GLOBAL journalier atteint ({Count}) — incident {Code} non stocké. " +
                        "Un bug touche vraisemblablement beaucoup d'appareils à la fois.",
                        total, code);
                    return result;
                }
            }
            catch (Exception ex)
            {
                // Ne jamais transformer un rapport d'incident en erreur : on
                // préfère stocker sans avoir pu vérifier le plafond.
                _logger.LogWarning(ex, "[observability] Vérification des plafonds impossible.");
            }

            var incident = new ClientIncident
            {
                Code = code,
                Kind = (Enums.IncidentKind)dto.Kind,
                UserId = userId,
                SchoolId = schoolId,
                Role = Clip(role, 30),
                Platform = Clip(dto.Platform, 20),
                AppVersion = Clip(dto.AppVersion, 40),
                Device = Clip(dto.Device, 120),
                LocaleCode = Clip(dto.LocaleCode, 10),
                Route = Clip(dto.Route, 200),
                Message = Clip(dto.Message, 400),
                ExceptionType = Clip(dto.ExceptionType, 160),
                StackTrace = Clip(dto.StackTrace, 8000),
                RequestTrace = TraceCode.TryNormalize(dto.RequestTrace),
                UserComment = NullIfEmpty(Clip(dto.UserComment, 600)),
            };

            _db.ClientIncidents.Add(incident);
            await _db.SaveChangesAsync(ct);
            result.IncidentId = incident.Id;

            // Journalisé en Warning : c'est ce qui rendra l'incident visible dans
            // la console (donc dans journalctl) en plus du fichier, sans avoir à
            // ouvrir la page SuperAdmin.
            // `:l` = valeur sans guillemets dans le message rendu (cf. le même
            // choix dans GlobalExceptionMiddleware).
            _logger.LogWarning(
                "[observability] Incident {Code:l} ({Kind}) — {Route:l} — utilisateur {UserId}, école {SchoolId} : {IncidentMessage:l}",
                code, incident.Kind, incident.Route, userId, schoolId, incident.Message);

            result.Stored = true;
            return result;
        }

        /// <summary>
        /// Tronque au lieu de refuser. Un rapport un peu trop long reste
        /// exploitable ; le rejeter reviendrait à perdre l'incident, c'est-à-dire
        /// exactement ce qu'on cherche à éviter.
        /// </summary>
        private static string Clip(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var trimmed = value.Trim();
            return trimmed.Length <= max ? trimmed : trimmed[..max];
        }

        private static string? NullIfEmpty(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
