using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Observability;
using Idara.API.Enums;
using Idara.API.Services.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Réception des incidents remontés par l'application, et consultation par le
    /// SuperAdmin.
    ///
    /// <para><b>Ce que cet ensemble change concrètement.</b> Un directeur de daara
    /// appelle en disant « ça ne marche pas ». Il lit le code affiché sur son
    /// écran (<c>IDR-7K2MQ4</c>) ; on le colle ici et on obtient son écran, sa
    /// version, son école, la pile d'appels, et les lignes de journal serveur de
    /// la requête correspondante. Le cycle « décris-moi le bug » disparaît — non
    /// pas parce qu'on aurait mieux deviné, mais parce que l'utilisateur nous a
    /// donné la clé exacte sans rien avoir à expliquer.</para>
    /// </summary>
    [ApiController]
    [Route("api/observability")]
    public class ObservabilityController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ITelemetrySink _sink;
        private readonly IServerLogSearchService _logs;
        private readonly IIncidentAlertService _alerts;
        private readonly ILogger<ObservabilityController> _logger;

        public ObservabilityController(
            AppDbContext context,
            ITelemetrySink sink,
            IServerLogSearchService logs,
            IIncidentAlertService alerts,
            ILogger<ObservabilityController> logger)
        {
            _context = context;
            _sink = sink;
            _logs = logs;
            _alerts = alerts;
            _logger = logger;
        }

        // =====================================================================
        //  Réception
        // =====================================================================

        /// <summary>
        /// Reçoit un incident depuis l'application.
        ///
        /// <para><b>Authentifié, volontairement</b> (§4.8, garde-fou 3) : ouvert,
        /// cet endpoint serait un moyen trivial de remplir le disque. La
        /// contrepartie — un plantage survenu avant la connexion serait perdu — est
        /// traitée côté application, qui garde le rapport et le renvoie une fois
        /// l'utilisateur connecté.</para>
        ///
        /// <para>Le corps est plafonné par les <c>[StringLength]</c> du DTO, donc
        /// bien en deçà des 64 Ko annoncés, et rejeté par la validation
        /// automatique d'<c>[ApiController]</c> avant d'atteindre la base.</para>
        /// </summary>
        [HttpPost("incidents")]
        [Authorize]
        public async Task<IActionResult> Report([FromBody] ReportIncidentDto dto)
        {
            var result = await _sink.RecordAsync(
                dto,
                User.GetUserId(),
                User.GetSchoolId(),
                User.GetRole() ?? string.Empty,
                HttpContext.RequestAborted);

            // Alerte e-mail au SuperAdmin, APRÈS l'écriture et sans attendre son
            // envoi (motif §42/§57 : un envoi d'e-mail ne doit jamais retarder ni
            // faire échouer ce qui est déjà enregistré). C'est cette alerte qui
            // permet à l'utilisateur de n'avoir RIEN à faire : ni copier un code,
            // ni écrire, ni nous appeler — l'e-mail contient son numéro.
            if (result.Stored && result.IncidentId != null)
            {
                _alerts.QueueAlert(result.IncidentId.Value);
            }

            // Toujours 200, même quand le plafond est atteint : l'application ne
            // doit rien dire de plus à l'utilisateur que « c'est envoyé ». Son
            // problème est réel ; qu'on ait déjà cinq copies du même rapport est
            // notre affaire, pas la sienne.
            return Ok(ApiResponse<IncidentAcceptedDto>.Ok(result));
        }

        // =====================================================================
        //  Consultation (SuperAdmin)
        // =====================================================================

        /// <summary>
        /// Liste des incidents, filtrable par code, utilisateur, école, type et
        /// période. La recherche par code accepte un code recopié approximativement
        /// (sans préfixe ni tiret) : c'est ce qui arrive par WhatsApp.
        /// </summary>
        [HttpGet("incidents")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<IActionResult> List(
            [FromQuery] string? q,
            [FromQuery] int? kind,
            [FromQuery] int? schoolId,
            [FromQuery] int? userId,
            [FromQuery] bool? unresolvedOnly,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 30)
        {
            if (page < 1) page = 1;
            // Borné : rien ne doit permettre de demander la table entière (§129).
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _context.ClientIncidents.AsNoTracking().AsQueryable();

            if (kind != null) query = query.Where(i => (int)i.Kind == kind);
            if (schoolId != null) query = query.Where(i => i.SchoolId == schoolId);
            if (userId != null) query = query.Where(i => i.UserId == userId);
            if (unresolvedOnly == true) query = query.Where(i => !i.IsResolved);

            // Bornes en jour civil UTC (§47) : sans ça, un filtre de dates part en
            // 500 sur PostgreSQL dès que le client envoie une date sans fuseau.
            if (from != null) query = query.Where(i => i.CreatedAt >= from.Value.ToUtcDay());
            if (to != null) query = query.Where(i => i.CreatedAt < to.Value.ToUtcDay().AddDays(1));

            if (!string.IsNullOrWhiteSpace(q))
            {
                var code = Common.Observability.TraceCode.TryNormalize(q);
                if (code != null)
                {
                    // Un code identifie un incident OU la requête qui l'a provoqué :
                    // chercher les deux évite de devoir expliquer la nuance.
                    query = query.Where(i => i.Code == code || i.RequestTrace == code);
                }
                else
                {
                    var pattern = TransactionSearch.Pattern(q);
                    if (pattern != null)
                    {
                        // Le `?? ""` est là pour l'analyse de nullité du
                        // compilateur, pas pour la sémantique : PostgreSQL
                        // traduit en COALESCE, et une valeur absente ne
                        // correspond de toute façon à aucune recherche.
                        query = query.Where(i =>
                            EF.Functions.ILike(i.Code, pattern) ||
                            EF.Functions.ILike(i.Message, pattern) ||
                            EF.Functions.ILike(i.Route, pattern) ||
                            // Le commentaire de l'utilisateur est le texte le
                            // PLUS signifiant d'un signalement (« le bouton
                            // retrait ne fait rien ») : c'est le premier réflexe
                            // de recherche, il serait absurde de l'exclure.
                            EF.Functions.ILike(i.UserComment ?? string.Empty, pattern) ||
                            (i.User != null && EF.Functions.ILike(i.User.FullName ?? string.Empty, pattern)) ||
                            (i.School != null && EF.Functions.ILike(i.School.Name ?? string.Empty, pattern)));
                    }
                }
            }

            var total = await query.CountAsync();
            var rows = await query
                .OrderByDescending(i => i.CreatedAt)
                .ThenByDescending(i => i.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(i => new IncidentListItemDto
                {
                    Id = i.Id,
                    Code = i.Code,
                    Kind = (int)i.Kind,
                    KindLabel = KindLabel(i.Kind),
                    CreatedAt = i.CreatedAt,
                    UserId = i.UserId,
                    UserName = i.User != null ? i.User.FullName : null,
                    Role = i.Role,
                    SchoolId = i.SchoolId,
                    SchoolName = i.School != null ? i.School.Name : null,
                    Platform = i.Platform,
                    AppVersion = i.AppVersion,
                    Route = i.Route,
                    Message = i.Message,
                    IsResolved = i.IsResolved,
                })
                .ToListAsync();

            return Ok(ApiResponse<PaginatedResult<IncidentListItemDto>>.Ok(
                new PaginatedResult<IncidentListItemDto>
                {
                    Data = rows,
                    TotalCount = total,
                    Page = page,
                    PageSize = pageSize,
                }));
        }

        [HttpGet("incidents/{id:int}")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<IActionResult> Detail(int id)
        {
            var i = await _context.ClientIncidents
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.School)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (i == null) return NotFound(ApiResponse<bool>.Fail("Incident introuvable."));

            return Ok(ApiResponse<IncidentDetailDto>.Ok(new IncidentDetailDto
            {
                Id = i.Id,
                Code = i.Code,
                Kind = (int)i.Kind,
                KindLabel = KindLabel(i.Kind),
                CreatedAt = i.CreatedAt,
                UserId = i.UserId,
                UserName = i.User?.FullName,
                Role = i.Role,
                SchoolId = i.SchoolId,
                SchoolName = i.School?.Name,
                Platform = i.Platform,
                AppVersion = i.AppVersion,
                Device = i.Device,
                LocaleCode = i.LocaleCode,
                Route = i.Route,
                Message = i.Message,
                ExceptionType = i.ExceptionType,
                StackTrace = i.StackTrace,
                RequestTrace = i.RequestTrace,
                UserComment = i.UserComment,
                Timeline = i.Timeline,
                IsResolved = i.IsResolved,
            }));
        }

        /// <summary>Marque un incident comme traité (ou le rouvre).</summary>
        [HttpPost("incidents/{id:int}/resolved")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<IActionResult> SetResolved(int id, [FromQuery] bool value = true)
        {
            var updated = await _context.ClientIncidents
                .Where(i => i.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsResolved, value));

            if (updated == 0) return NotFound(ApiResponse<bool>.Fail("Incident introuvable."));
            return Ok(ApiResponse<bool>.Ok(true));
        }

        /// <summary>
        /// Recherche dans les journaux serveur. C'est l'endpoint qui évite d'ouvrir
        /// un terminal SSH quand un directeur appelle avec son code.
        /// </summary>
        [HttpGet("logs")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<IActionResult> Logs(
            [FromQuery] string? trace,
            [FromQuery] int? userId,
            [FromQuery] int? schoolId,
            [FromQuery] string? level,
            [FromQuery] int days = 7)
        {
            // Refuser une recherche sans aucun critère est un choix de
            // performance : renvoyer « les 300 dernières lignes » obligerait à
            // balayer tous les fichiers de la fenêtre pour un résultat sans usage.
            if (string.IsNullOrWhiteSpace(trace) && userId == null && schoolId == null &&
                string.IsNullOrWhiteSpace(level))
            {
                return BadRequest(ApiResponse<bool>.Fail(
                    "Indiquez au moins un critère : code d'incident, utilisateur, école ou niveau."));
            }

            var result = await _logs.SearchAsync(
                trace, userId, schoolId, level, days, HttpContext.RequestAborted);

            _logger.LogInformation(
                "[observability] Recherche journaux : trace={Trace}, user={User}, école={School}, " +
                "niveau={Level}, {Files} fichier(s), {Count} ligne(s).",
                trace, userId, schoolId, level, result.FilesScanned.Count, result.Entries.Count);

            return Ok(ApiResponse<ServerLogSearchResultDto>.Ok(result));
        }

        private static string KindLabel(IncidentKind kind) => kind switch
        {
            IncidentKind.FlutterError => "Plantage de l'application",
            IncidentKind.UserReport => "Signalé par l'utilisateur",
            IncidentKind.ApiError => "Erreur du service",
            IncidentKind.UnexpectedRestart => "Redémarrage inattendu",
            _ => kind.ToString(),
        };
    }
}
