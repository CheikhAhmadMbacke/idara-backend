using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Operations;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Journal du daara : ce que le directeur consignait dans un carnet papier.
    ///
    /// Trois niveaux de visibilité (cf. <see cref="EventVisibility"/>) :
    /// la direction écrit, l'équipe lit ce qui lui est ouvert, et les parents ne
    /// voient QUE ce qui a été explicitement ouvert (endpoint séparé côté
    /// <c>GuardianController</c>).
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/daara-events")]
    public class DaaraEventsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly UploadSettings _uploads;
        private readonly ILogger<DaaraEventsController> _logger;

        public DaaraEventsController(
            AppDbContext context,
            IWebHostEnvironment env,
            IOptions<UploadSettings> uploads,
            ILogger<DaaraEventsController> logger)
        {
            _context = context;
            _env = env;
            _uploads = uploads.Value;
            _logger = logger;
        }

        /// <summary>
        /// Ouverture MINIMALE qu'un événement doit avoir pour être lisible par
        /// le rôle courant. Les valeurs de <see cref="EventVisibility"/> étant
        /// croissantes en ouverture, la lecture se réduit à
        /// <c>e.Visibility &gt;= seuil</c>.
        ///
        /// ⚠️ Une seule définition, utilisée par TOUTES les lectures : si un
        /// endpoint décidait seul de ce qu'un surveillant peut lire, une note
        /// « direction seule » finirait par fuiter par l'un des chemins.
        ///
        /// ⚠️ L'enseignant n'a PLUS accès au journal depuis le 2026-08-18
        /// (décision produit : réservé au personnel et à la direction) — il est
        /// retiré des listes de rôles des endpoints de lecture, pas seulement
        /// de l'interface.
        ///
        /// ⚠️ Décision : l'observateur (SchoolViewer) est traité comme l'équipe,
        /// PAS comme la direction — alors qu'ailleurs il voit tout ce que voit
        /// le SchoolAdmin. Le niveau « direction seule » n'existe que pour
        /// écrire ce qui ne doit être lu par personne d'autre (« conflit avec le
        /// propriétaire », « retard de salaire »), et l'observateur est
        /// justement souvent le propriétaire ou un auditeur. Lui ouvrir ce
        /// niveau viderait la fonctionnalité de son sens : le directeur
        /// cesserait d'écrire ces lignes-là. Une ligne à changer ici si l'on
        /// veut l'inverse.
        /// </summary>
        private static EventVisibility MinVisibilityFor(string? role) => role switch
        {
            UserRoles.SchoolAdmin or UserRoles.SchoolStaff => EventVisibility.Direction,
            _ => EventVisibility.School,
        };

        /// <summary>
        /// Ramène une visibilité « Parents inclus » au niveau « Toute l'école ».
        ///
        /// <para>⚠️ Depuis le 2026-08-22, la vie du daara ne figure plus dans
        /// l'espace parent (décision produit) : plus personne ne LIT le niveau
        /// <see cref="EventVisibility.Guardians"/>. Le laisser s'enregistrer
        /// ferait un <b>faux bouton</b> — une école cocherait « Parents inclus »
        /// en croyant publier aux familles, et rien n'arriverait jamais, sans le
        /// moindre message. Même traitement que le niveau « Parents » des
        /// objectifs, qui n'a jamais eu d'écran (§144).</para>
        ///
        /// <para>Aucune reprise de données : les événements déjà enregistrés à ce
        /// niveau restent lisibles par l'école (la lecture est un
        /// <c>&gt;=</c>).</para>
        /// </summary>
        private static EventVisibility Storable(EventVisibility v) =>
            v == EventVisibility.Guardians ? EventVisibility.School : v;

        // ========================================================
        // ===== Lecture =====
        // ========================================================

        /// <summary>
        /// `GET /api/daara-events` — frise du journal, du plus récent au plus
        /// ancien, filtrable par catégorie, période et recherche libre.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Surveillant},{UserRoles.SchoolViewer}")]
        public async Task<ActionResult<DaaraEventListDto>> GetEvents(
            [FromQuery] string? q,
            [FromQuery] DaaraEventCategory? category,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 30,
            CancellationToken ct = default)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var minVisibility = MinVisibilityFor(User.GetRole());

            var query = _context.DaaraEvents
                .Where(e => e.SchoolId == schoolId.Value && e.Visibility >= minVisibility);

            if (category.HasValue)
                query = query.Where(e => e.Category == category.Value);

            // Bornes en JOUR CIVIL (cf. §47) : un `to` reçu tel quel exclurait
            // les événements du jour même.
            if (from.HasValue)
            {
                var f = from.Value.ToUtcDay();
                query = query.Where(e => e.Date >= f);
            }
            if (to.HasValue)
            {
                var t = to.Value.ToUtcDay();
                query = query.Where(e => e.Date <= t);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                // Même normalisation que les historiques financiers (§121) :
                // insensible à la casse et jokers `%`/`_` échappés, sans quoi
                // taper « % » ramènerait tout le journal.
                if (TransactionSearch.Pattern(q) is string pattern)
                {
                    query = query.Where(e =>
                        EF.Functions.ILike(e.Title, pattern) ||
                        (e.Description != null && EF.Functions.ILike(e.Description, pattern)));
                }
            }

            var total = await query.CountAsync(ct);

            var p = page < 1 ? 1 : page;
            var size = Math.Clamp(pageSize, 1, 200);

            // ThenByDescending(Id) : plusieurs événements le même jour sont
            // fréquents (une visite et une réunion), l'ordre doit être stable
            // d'un chargement à l'autre — cf. §107.
            var items = await query
                .OrderByDescending(e => e.Date)
                .ThenByDescending(e => e.Id)
                .Skip((p - 1) * size)
                .Take(size)
                .Include(e => e.Photos)
                .Include(e => e.DaaraObjective)
                .ToListAsync(ct);

            var authorNames = await AuthorNamesAsync(items, ct);

            return Ok(new DaaraEventListDto
            {
                Items = items.Select(e => Map(e, authorNames)).ToList(),
                TotalCount = total,
                Page = p,
                PageSize = size,
            });
        }

        [HttpGet("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Surveillant},{UserRoles.SchoolViewer}")]
        public async Task<ActionResult<DaaraEventDto>> GetEvent(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var minVisibility = MinVisibilityFor(User.GetRole());

            var ev = await _context.DaaraEvents
                .Include(e => e.Photos)
                .Include(e => e.DaaraObjective)
                .FirstOrDefaultAsync(e => e.Id == id
                                          && e.SchoolId == schoolId.Value
                                          && e.Visibility >= minVisibility, ct);
            if (ev == null) return NotFound();

            var names = await AuthorNamesAsync(new[] { ev }, ct);
            return Ok(Map(ev, names));
        }

        // ========================================================
        // ===== Écriture =====
        // ========================================================

        [HttpPost]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<DaaraEventDto>> CreateEvent(
            [FromBody] CreateDaaraEventDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var now = DateTime.UtcNow;
            var ev = new DaaraEvent
            {
                SchoolId = schoolId.Value,
                Date = (dto.Date ?? now).ToUtcDay(),
                Title = dto.Title.Trim(),
                Description = NullIfEmpty(dto.Description),
                Category = dto.Category,
                Visibility = Storable(dto.Visibility),
                DaaraObjectiveId =
                    await ResolveObjectiveAsync(dto.DaaraObjectiveId, schoolId.Value, ct),
                CreatedById = userId.Value,
                CreatedAt = now,
            };

            _context.DaaraEvents.Add(ev);
            await _context.SaveChangesAsync(ct);

            foreach (var photo in dto.Photos)
            {
                var saved = await SavePhotoAsync(photo);
                if (saved == null) continue;
                _context.DaaraEventPhotos.Add(new DaaraEventPhoto
                {
                    DaaraEventId = ev.Id,
                    FilePath = saved.Value.path,
                    ContentType = saved.Value.contentType,
                    FileSize = saved.Value.size,
                    UploadedAt = now,
                });
            }
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[daara-events] Événement créé SchoolId={SchoolId} EventId={EventId} Catégorie={Category} Visibilité={Visibility}",
                schoolId, ev.Id, ev.Category, ev.Visibility);

            var reloaded = await _context.DaaraEvents
                .Include(e => e.Photos)
                .Include(e => e.DaaraObjective)
                .FirstAsync(e => e.Id == ev.Id, ct);
            var names = await AuthorNamesAsync(new[] { reloaded }, ct);
            return Ok(Map(reloaded, names));
        }

        [HttpPut("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<DaaraEventDto>> UpdateEvent(
            int id, [FromBody] UpdateDaaraEventDto dto, CancellationToken ct)
        {
            if (id != dto.Id)
                return BadRequest(ApiResponse<bool>.Fail(
                    "L'identifiant dans l'URL ne correspond pas à celui du corps de la requête."));

            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var ev = await _context.DaaraEvents
                .Include(e => e.Photos)
                .FirstOrDefaultAsync(e => e.Id == id && e.SchoolId == schoolId.Value, ct);
            if (ev == null) return NotFound();

            ev.Date = (dto.Date ?? ev.Date).ToUtcDay();
            ev.Title = dto.Title.Trim();
            ev.Description = NullIfEmpty(dto.Description);
            ev.Category = dto.Category;
            ev.Visibility = Storable(dto.Visibility);
            ev.DaaraObjectiveId =
                await ResolveObjectiveAsync(dto.DaaraObjectiveId, schoolId.Value, ct);
            ev.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);

            var names = await AuthorNamesAsync(new[] { ev }, ct);
            return Ok(Map(ev, names));
        }

        /// <summary>
        /// Suppression DOUCE : une note effacée par erreur reste récupérable en
        /// base, et l'école n'hésite pas à écrire de peur de ne pas pouvoir
        /// corriger. Les photos restent sur disque tant que l'événement existe.
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> DeleteEvent(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var ev = await _context.DaaraEvents
                .FirstOrDefaultAsync(e => e.Id == id && e.SchoolId == schoolId.Value, ct);
            if (ev == null) return NotFound();

            ev.IsDeleted = true;
            ev.DeletedAt = DateTime.UtcNow;
            ev.DeletedById = userId.Value;
            await _context.SaveChangesAsync(ct);

            return Ok(ApiResponse<bool>.Ok(true, "Événement supprimé."));
        }

        [HttpPost("{id}/photos")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<DaaraEventPhotoDto>> AddPhoto(
            int id, [FromBody] DaaraEventPhotoInputDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var ev = await _context.DaaraEvents
                .FirstOrDefaultAsync(e => e.Id == id && e.SchoolId == schoolId.Value, ct);
            if (ev == null) return NotFound();

            var saved = await SavePhotoAsync(dto);
            if (saved == null)
                return BadRequest(ApiResponse<bool>.Fail(
                    "Image invalide ou trop lourde."));

            var photo = new DaaraEventPhoto
            {
                DaaraEventId = ev.Id,
                FilePath = saved.Value.path,
                ContentType = saved.Value.contentType,
                FileSize = saved.Value.size,
                UploadedAt = DateTime.UtcNow,
            };
            _context.DaaraEventPhotos.Add(photo);
            await _context.SaveChangesAsync(ct);

            return Ok(new DaaraEventPhotoDto
            {
                Id = photo.Id,
                FilePath = photo.FilePath,
                ContentType = photo.ContentType,
                FileSize = photo.FileSize,
                UploadedAt = photo.UploadedAt,
            });
        }

        [HttpDelete("{id}/photos/{photoId}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> DeletePhoto(int id, int photoId, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var photo = await _context.DaaraEventPhotos
                .Include(p => p.DaaraEvent)
                .FirstOrDefaultAsync(p => p.Id == photoId
                                          && p.DaaraEventId == id
                                          && p.DaaraEvent.SchoolId == schoolId.Value, ct);
            if (photo == null) return NotFound();

            DeletePhotoFile(photo.FilePath);
            _context.DaaraEventPhotos.Remove(photo);
            await _context.SaveChangesAsync(ct);

            return Ok(ApiResponse<bool>.Ok(true, "Photo supprimée."));
        }

        // ========================================================
        // ===== Helpers =====
        // ========================================================

        /// <summary>
        /// Noms des auteurs en UNE requête (pas de N+1). Un compte supprimé
        /// (§68) n'a plus de nom exploitable : on renvoie null plutôt qu'un
        /// « deleted-17@deleted.idara.local » incompréhensible.
        /// </summary>
        private async Task<Dictionary<int, string>> AuthorNamesAsync(
            IReadOnlyCollection<DaaraEvent> events, CancellationToken ct)
        {
            var ids = events.Select(e => e.CreatedById).Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, string>();

            var rows = await _context.Users
                .Where(u => ids.Contains(u.Id) && !u.IsDeleted && u.FullName != null)
                .Select(u => new { u.Id, u.FullName })
                .ToListAsync(ct);

            return rows.ToDictionary(r => r.Id, r => r.FullName!);
        }

        private static DaaraEventDto Map(DaaraEvent e, Dictionary<int, string> authors) => new()
        {
            Id = e.Id,
            Date = e.Date,
            Title = e.Title,
            Description = e.Description,
            Category = e.Category,
            Visibility = e.Visibility,
            DaaraObjectiveId = e.DaaraObjectiveId,
            DaaraObjectiveTitle = e.DaaraObjective?.Title,
            CreatedByName = authors.GetValueOrDefault(e.CreatedById),
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            Photos = e.Photos.OrderBy(p => p.Id).Select(p => new DaaraEventPhotoDto
            {
                Id = p.Id,
                FilePath = p.FilePath,
                ContentType = p.ContentType,
                FileSize = p.FileSize,
                UploadedAt = p.UploadedAt,
            }).ToList(),
        };

        private async Task<(string path, string contentType, long size)?> SavePhotoAsync(
            DaaraEventPhotoInputDto dto)
        {
            var decoded = FileUploadValidator.DecodeAndValidate(
                dto.ContentBase64,
                _uploads.MaxPhotoSizeMb,
                _uploads.AllowedPhotoMimeTypes,
                dto.ContentType,
                dto.OriginalFileName);
            if (decoded == null) return null;

            var folder = Path.Combine(_env.WebRootPath, "uploads", "events");
            Directory.CreateDirectory(folder);

            // Nom aléatoire : ces photos sont servies par nginx sans
            // authentification (§32), un nom séquentiel les rendrait
            // énumérables — c'est le schéma de la fuite des reçus (§122).
            var fileName = $"{Guid.NewGuid():N}{decoded.Extension}";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), decoded.Bytes);

            return ($"/uploads/events/{fileName}", decoded.ContentType, decoded.Bytes.LongLength);
        }

        /// <summary>Retrait du fichier, best-effort, avec défense path-traversal (§43).</summary>
        private void DeletePhotoFile(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            try
            {
                var root = Path.GetFullPath(_env.WebRootPath);
                var full = Path.GetFullPath(Path.Combine(root, filePath.TrimStart('/')));
                if (!full.StartsWith(root, StringComparison.Ordinal)) return;
                if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[daara-events] Impossible de supprimer la photo {Path}", filePath);
            }
        }

        /// <summary>
        /// Valide que l'objectif appartient bien à l'école avant de l'attacher.
        /// Sans ce contrôle, un identifiant deviné rattacherait un événement à
        /// l'objectif d'un AUTRE daara — et son titre s'afficherait sur la
        /// fiche, révélant un projet qui ne nous regarde pas.
        /// Un identifiant inconnu détache simplement (null), il ne fait pas
        /// échouer l'enregistrement de la note.
        /// </summary>
        private async Task<int?> ResolveObjectiveAsync(
            int? objectiveId, int schoolId, CancellationToken ct)
        {
            if (objectiveId is null or <= 0) return null;
            var exists = await _context.DaaraObjectives
                .AnyAsync(o => o.Id == objectiveId.Value && o.SchoolId == schoolId, ct);
            return exists ? objectiveId : null;
        }

        private static string? NullIfEmpty(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
