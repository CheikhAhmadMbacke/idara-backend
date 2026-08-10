using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Operations;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Objectifs du daara : ce que le directeur notait à côté de ses événements
    /// (« augmenter le mur », « atteindre 200 élèves »).
    ///
    /// Deux niveaux de visibilité seulement — <c>Direction</c> et <c>School</c> :
    /// il n'existe pas d'écran parent pour les objectifs, offrir un niveau
    /// « Parents » sans lecteur serait un faux bouton.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/daara-objectives")]
    public class DaaraObjectivesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<DaaraObjectivesController> _logger;

        public DaaraObjectivesController(
            AppDbContext context, ILogger<DaaraObjectivesController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Ouverture minimale exigée pour lire. Même règle que le journal
        /// (<see cref="DaaraEventsController"/>) : l'observateur est traité
        /// comme l'équipe, pas comme la direction.
        /// </summary>
        private static EventVisibility MinVisibilityFor(string? role) => role switch
        {
            UserRoles.SchoolAdmin or UserRoles.SchoolStaff => EventVisibility.Direction,
            _ => EventVisibility.School,
        };

        // ========================================================
        // ===== Lecture =====
        // ========================================================

        [HttpGet]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher},{UserRoles.Surveillant},{UserRoles.SchoolViewer}")]
        public async Task<ActionResult<List<DaaraObjectiveDto>>> GetObjectives(
            [FromQuery] ObjectiveStatus? status, CancellationToken ct = default)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var minVisibility = MinVisibilityFor(User.GetRole());

            var query = _context.DaaraObjectives
                .Where(o => o.SchoolId == schoolId.Value && o.Visibility >= minVisibility);

            if (status.HasValue)
                query = query.Where(o => o.Status == status.Value);

            var objectives = await query
                // Les objectifs en cours d'abord : ce sont eux qu'on vient voir.
                // ThenByDescending(Id) départage deux objectifs créés le même
                // jour, sinon l'ordre change d'un chargement à l'autre (§107).
                .OrderBy(o => o.Status)
                .ThenByDescending(o => o.CreatedAt)
                .ThenByDescending(o => o.Id)
                .Include(o => o.Steps)
                .ToListAsync(ct);

            return Ok(await MapManyAsync(objectives, schoolId.Value, ct));
        }

        [HttpGet("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher},{UserRoles.Surveillant},{UserRoles.SchoolViewer}")]
        public async Task<ActionResult<DaaraObjectiveDto>> GetObjective(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var minVisibility = MinVisibilityFor(User.GetRole());

            var o = await _context.DaaraObjectives
                .Include(x => x.Steps)
                .FirstOrDefaultAsync(x => x.Id == id
                                          && x.SchoolId == schoolId.Value
                                          && x.Visibility >= minVisibility, ct);
            if (o == null) return NotFound();

            return Ok((await MapManyAsync(new[] { o }, schoolId.Value, ct)).First());
        }

        // ========================================================
        // ===== Écriture =====
        // ========================================================

        [HttpPost]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<DaaraObjectiveDto>> CreateObjective(
            [FromBody] CreateDaaraObjectiveDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var now = DateTime.UtcNow;
            var o = new DaaraObjective
            {
                SchoolId = schoolId.Value,
                Title = dto.Title.Trim(),
                Description = NullIfEmpty(dto.Description),
                MeasureMode = dto.MeasureMode,
                TargetValue = dto.MeasureMode == ObjectiveMeasureMode.Simple ? 0 : dto.TargetValue,
                // En mode automatique, l'avancement est LU à l'affichage : le
                // stocker le figerait à la valeur du jour de la création.
                CurrentValue = dto.MeasureMode is ObjectiveMeasureMode.Simple
                    or ObjectiveMeasureMode.StudentCount ? 0 : dto.CurrentValue,
                Unit = dto.MeasureMode == ObjectiveMeasureMode.Manual
                    ? NullIfEmpty(dto.Unit)
                    : null,
                TargetDate = dto.TargetDate?.ToUtcDay(),
                Visibility = ClampVisibility(dto.Visibility),
                Status = ObjectiveStatus.InProgress,
                CreatedById = userId.Value,
                CreatedAt = now,
            };

            _context.DaaraObjectives.Add(o);
            await _context.SaveChangesAsync(ct);

            var order = 0;
            foreach (var label in dto.Steps)
            {
                if (string.IsNullOrWhiteSpace(label)) continue;
                _context.DaaraObjectiveSteps.Add(new DaaraObjectiveStep
                {
                    DaaraObjectiveId = o.Id,
                    Label = label.Trim(),
                    SortOrder = order++,
                });
            }
            if (order > 0) await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[daara-objectives] Objectif créé SchoolId={SchoolId} Id={Id} Mode={Mode} Cible={Target}",
                schoolId, o.Id, o.MeasureMode, o.TargetValue);

            var reloaded = await _context.DaaraObjectives
                .Include(x => x.Steps)
                .FirstAsync(x => x.Id == o.Id, ct);
            return Ok((await MapManyAsync(new[] { reloaded }, schoolId.Value, ct)).First());
        }

        [HttpPut("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<DaaraObjectiveDto>> UpdateObjective(
            int id, [FromBody] UpdateDaaraObjectiveDto dto, CancellationToken ct)
        {
            if (id != dto.Id)
                return BadRequest(ApiResponse<bool>.Fail(
                    "L'identifiant dans l'URL ne correspond pas à celui du corps de la requête."));

            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var o = await _context.DaaraObjectives
                .Include(x => x.Steps)
                .FirstOrDefaultAsync(x => x.Id == id && x.SchoolId == schoolId.Value, ct);
            if (o == null) return NotFound();

            var wasAchieved = o.Status == ObjectiveStatus.Achieved;

            o.Title = dto.Title.Trim();
            o.Description = NullIfEmpty(dto.Description);
            o.Status = dto.Status;
            o.MeasureMode = dto.MeasureMode;
            o.TargetValue = dto.MeasureMode == ObjectiveMeasureMode.Simple ? 0 : dto.TargetValue;
            o.Unit = dto.MeasureMode == ObjectiveMeasureMode.Manual
                ? NullIfEmpty(dto.Unit)
                : null;
            o.TargetDate = dto.TargetDate?.ToUtcDay();
            o.Visibility = ClampVisibility(dto.Visibility);
            o.UpdatedAt = DateTime.UtcNow;

            // La date d'atteinte suit le statut dans les DEUX sens : rouvrir un
            // objectif clos par erreur doit effacer la date, sinon la fiche
            // afficherait « atteint le 3 mars » sur un objectif en cours.
            if (dto.Status == ObjectiveStatus.Achieved && !wasAchieved)
                o.AchievedAt = DateTime.UtcNow;
            else if (dto.Status != ObjectiveStatus.Achieved)
                o.AchievedAt = null;

            await _context.SaveChangesAsync(ct);
            return Ok((await MapManyAsync(new[] { o }, schoolId.Value, ct)).First());
        }

        /// <summary>Mise à jour du seul avancement — le geste courant.</summary>
        [HttpPut("{id}/progress")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<DaaraObjectiveDto>> UpdateProgress(
            int id, [FromBody] UpdateObjectiveProgressDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var o = await _context.DaaraObjectives
                .Include(x => x.Steps)
                .FirstOrDefaultAsync(x => x.Id == id && x.SchoolId == schoolId.Value, ct);
            if (o == null) return NotFound();

            if (o.MeasureMode is ObjectiveMeasureMode.Simple
                or ObjectiveMeasureMode.StudentCount)
            {
                return BadRequest(ApiResponse<bool>.Fail(
                    "Cet objectif ne se saisit pas à la main : son avancement suit les étapes ou l'effectif."));
            }

            o.CurrentValue = dto.CurrentValue;
            o.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            return Ok((await MapManyAsync(new[] { o }, schoolId.Value, ct)).First());
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> DeleteObjective(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var o = await _context.DaaraObjectives
                .FirstOrDefaultAsync(x => x.Id == id && x.SchoolId == schoolId.Value, ct);
            if (o == null) return NotFound();

            o.IsDeleted = true;
            o.DeletedAt = DateTime.UtcNow;
            o.DeletedById = userId.Value;

            // Les événements rattachés sont DÉTACHÉS, jamais supprimés : ils
            // racontent ce qui s'est passé, indépendamment de l'objectif.
            //
            // Mise à jour SUIVIE et non ExecuteUpdate : elle part dans le MÊME
            // SaveChanges que la suppression, donc soit les deux ont lieu, soit
            // aucun — jamais des événements orphelins d'un objectif encore
            // vivant. Le volume le permet : un objectif compte quelques
            // événements liés, pas des milliers.
            // IgnoreQueryFilters : un événement déjà effacé garderait sinon une
            // clé étrangère pointant vers un objectif supprimé.
            var linked = await _context.DaaraEvents
                .IgnoreQueryFilters()
                .Where(e => e.DaaraObjectiveId == id)
                .ToListAsync(ct);
            foreach (var e in linked) e.DaaraObjectiveId = null;

            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<bool>.Ok(true, "Objectif supprimé."));
        }

        // ----- Étapes -----

        [HttpPost("{id}/steps")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<DaaraObjectiveDto>> AddStep(
            int id, [FromBody] CreateObjectiveStepDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var o = await _context.DaaraObjectives
                .Include(x => x.Steps)
                .FirstOrDefaultAsync(x => x.Id == id && x.SchoolId == schoolId.Value, ct);
            if (o == null) return NotFound();

            _context.DaaraObjectiveSteps.Add(new DaaraObjectiveStep
            {
                DaaraObjectiveId = o.Id,
                Label = dto.Label.Trim(),
                SortOrder = o.Steps.Count == 0 ? 0 : o.Steps.Max(s => s.SortOrder) + 1,
            });
            await _context.SaveChangesAsync(ct);

            var reloaded = await _context.DaaraObjectives
                .Include(x => x.Steps)
                .FirstAsync(x => x.Id == o.Id, ct);
            return Ok((await MapManyAsync(new[] { reloaded }, schoolId.Value, ct)).First());
        }

        [HttpPut("{id}/steps/{stepId}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<DaaraObjectiveDto>> ToggleStep(
            int id, int stepId, [FromBody] ToggleObjectiveStepDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var step = await _context.DaaraObjectiveSteps
                .Include(s => s.DaaraObjective)
                .FirstOrDefaultAsync(s => s.Id == stepId
                                          && s.DaaraObjectiveId == id
                                          && s.DaaraObjective.SchoolId == schoolId.Value, ct);
            if (step == null) return NotFound();

            step.IsDone = dto.IsDone;
            step.DoneAt = dto.IsDone ? DateTime.UtcNow : null;
            await _context.SaveChangesAsync(ct);

            var reloaded = await _context.DaaraObjectives
                .Include(x => x.Steps)
                .FirstAsync(x => x.Id == id, ct);
            return Ok((await MapManyAsync(new[] { reloaded }, schoolId.Value, ct)).First());
        }

        [HttpDelete("{id}/steps/{stepId}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<DaaraObjectiveDto>> DeleteStep(
            int id, int stepId, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var step = await _context.DaaraObjectiveSteps
                .Include(s => s.DaaraObjective)
                .FirstOrDefaultAsync(s => s.Id == stepId
                                          && s.DaaraObjectiveId == id
                                          && s.DaaraObjective.SchoolId == schoolId.Value, ct);
            if (step == null) return NotFound();

            // Suppression franche : une étape n'a pas d'historique à préserver,
            // contrairement à un objectif ou à une note du journal.
            _context.DaaraObjectiveSteps.Remove(step);
            await _context.SaveChangesAsync(ct);

            var reloaded = await _context.DaaraObjectives
                .Include(x => x.Steps)
                .FirstAsync(x => x.Id == id, ct);
            return Ok((await MapManyAsync(new[] { reloaded }, schoolId.Value, ct)).First());
        }

        // ========================================================
        // ===== Helpers =====
        // ========================================================

        /// <summary>
        /// Les objectifs n'ont pas d'écran parent : un niveau « Parents »
        /// arrivé par l'API serait un droit que personne n'exerce. On le ramène
        /// au niveau école plutôt que de le stocker tel quel.
        /// </summary>
        private static EventVisibility ClampVisibility(EventVisibility v) =>
            v == EventVisibility.Direction ? EventVisibility.Direction : EventVisibility.School;

        private async Task<List<DaaraObjectiveDto>> MapManyAsync(
            IReadOnlyCollection<DaaraObjective> objectives, int schoolId, CancellationToken ct)
        {
            if (objectives.Count == 0) return new List<DaaraObjectiveDto>();

            // Effectif LU maintenant, une seule fois pour toute la liste : c'est
            // lui qui remplit la barre des objectifs en mode automatique.
            long studentCount = 0;
            if (objectives.Any(o => o.MeasureMode == ObjectiveMeasureMode.StudentCount))
            {
                studentCount = await _context.Students
                    .CountAsync(s => s.SchoolId == schoolId && !s.IsDeleted, ct);
            }

            var ids = objectives.Select(o => o.Id).ToList();

            var linkedCounts = (await _context.DaaraEvents
                    .Where(e => e.DaaraObjectiveId != null && ids.Contains(e.DaaraObjectiveId!.Value))
                    .GroupBy(e => e.DaaraObjectiveId!.Value)
                    .Select(g => new { ObjectiveId = g.Key, Count = g.Count() })
                    .ToListAsync(ct))
                .ToDictionary(x => x.ObjectiveId, x => x.Count);

            var authorIds = objectives.Select(o => o.CreatedById).Distinct().ToList();
            var authorRows = await _context.Users
                .Where(u => authorIds.Contains(u.Id) && !u.IsDeleted && u.FullName != null)
                .Select(u => new { u.Id, u.FullName })
                .ToListAsync(ct);
            var authors = authorRows.ToDictionary(r => r.Id, r => r.FullName!);

            return objectives.Select(o =>
            {
                var current = o.MeasureMode == ObjectiveMeasureMode.StudentCount
                    ? studentCount
                    : o.CurrentValue;

                return new DaaraObjectiveDto
                {
                    Id = o.Id,
                    Title = o.Title,
                    Description = o.Description,
                    Status = o.Status,
                    MeasureMode = o.MeasureMode,
                    TargetValue = o.TargetValue,
                    CurrentValue = current,
                    Unit = o.Unit,
                    TargetDate = o.TargetDate,
                    Visibility = o.Visibility,
                    LinkedEventCount = linkedCounts.GetValueOrDefault(o.Id),
                    Progress = ComputeProgress(o, current),
                    CreatedByName = authors.GetValueOrDefault(o.CreatedById),
                    CreatedAt = o.CreatedAt,
                    UpdatedAt = o.UpdatedAt,
                    AchievedAt = o.AchievedAt,
                    Steps = o.Steps
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.Id)
                        .Select(s => new DaaraObjectiveStepDto
                        {
                            Id = s.Id,
                            Label = s.Label,
                            IsDone = s.IsDone,
                            DoneAt = s.DoneAt,
                            SortOrder = s.SortOrder,
                        }).ToList(),
                };
            }).ToList();
        }

        /// <summary>
        /// Avancement de 0 à 1. Calculé ICI et nulle part ailleurs, pour que
        /// l'écran, un futur export et un futur rapport donnent le même chiffre.
        ///
        /// En mode simple, ce sont les ÉTAPES qui portent l'avancement quand il
        /// y en a — c'est ainsi qu'un chantier s'écrit sur un carnet. Sans
        /// étape, l'objectif est binaire.
        /// </summary>
        private static double ComputeProgress(DaaraObjective o, long current)
        {
            if (o.Status == ObjectiveStatus.Achieved) return 1;

            if (o.MeasureMode == ObjectiveMeasureMode.Simple)
            {
                if (o.Steps.Count == 0) return 0;
                return (double)o.Steps.Count(s => s.IsDone) / o.Steps.Count;
            }

            // La cible est refusée à zéro à la saisie ; le garde-fou couvre les
            // lignes écrites autrement (import, correction manuelle en base).
            if (o.TargetValue <= 0) return 0;

            var ratio = (double)current / o.TargetValue;
            return ratio < 0 ? 0 : (ratio > 1 ? 1 : ratio);
        }

        private static string? NullIfEmpty(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
