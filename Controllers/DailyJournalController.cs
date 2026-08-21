using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Operations;
using Idara.API.Models;
using Idara.API.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/daily-journal")]
    public class DailyJournalController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notif;
        public DailyJournalController(AppDbContext context, INotificationService notif)
        {
            _context = context;
            _notif = notif;
        }

        /// <summary>Notifie (push) les parents des élèves dont le journal du jour a
        /// été renseigné. Best-effort post-commit, 1 push/élève/jour (anti-spam).
        /// Clic → fiche de l'enfant.</summary>
        private async Task NotifyJournalAsync(IReadOnlyCollection<int> studentIds)
        {
            if (studentIds.Count == 0) return;
            // Enrolled() : !IsDeleted manquait (oubli préexistant corrigé le
            // 2026-08-17) et un sortant ne notifie plus ses parents.
            var names = await _context.Students
                .Where(s => studentIds.Contains(s.Id))
                .Enrolled()
                .Select(s => new { s.Id, s.FirstName, s.LastName })
                .ToListAsync();
            foreach (var s in names)
            {
                var eleve = $"{s.FirstName} {s.LastName}".Trim();
                await _notif.NotifyGuardiansOfStudentAsync(
                    s.Id, NotificationTemplates.ChildJournalUpdated(eleve),
                    "CHILD_JOURNAL", $"/guardian/children/{s.Id}", oncePerDay: true);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Get(
            [FromQuery] int? studentId,
            [FromQuery] int? classId,
            [FromQuery] int? teacherId,
            [FromQuery] int? subjectId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] DateTime? date)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // Périmètre de l'appelant : un enseignant ne lit le journal que des
            // élèves de ses classes (§150).
            var visible = await _context.VisibleClassIdsAsync(
                User.GetRole(), userId.Value, schoolId.Value);

            var query = _context.DailyJournalEntries
                .Include(j => j.Student)
                .Include(j => j.Teacher)
                .Include(j => j.Subject)
                .Where(j => j.SchoolId == schoolId.Value && !j.IsDeleted)
                .Where(j => visible == null
                    || (j.Student.ClassId != null && visible.Contains(j.Student.ClassId.Value)));

            if (studentId.HasValue) query = query.Where(j => j.StudentId == studentId.Value);
            if (classId.HasValue)   query = query.Where(j => j.Student.ClassId == classId.Value);
            if (teacherId.HasValue) query = query.Where(j => j.TeacherId == teacherId.Value);
            if (subjectId.HasValue) query = query.Where(j => j.SubjectId == subjectId.Value);

            if (date.HasValue)
            {
                var d = date.Value.ToUtcDay();
                query = query.Where(j => j.Date == d);
            }
            else
            {
                if (from.HasValue) query = query.Where(j => j.Date >= from.Value.ToUtcDay());
                if (to.HasValue) query = query.Where(j => j.Date <= to.Value.ToUtcDay());
            }

            var items = await query
                .OrderByDescending(j => j.Date).ThenByDescending(j => j.CreatedAt)
                .ToListAsync();
            // Le verrou dépend du rôle du LECTEUR : la direction voit tout
            // comme modifiable, l'enseignant seulement ce qui est dans sa
            // fenêtre de 48 h.
            var readerRole = User.GetRole();
            return Ok(items.Select(j => Map(j, readerRole)));
        }

        [HttpPost]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> Create([FromBody] DailyJournalEntryCreateDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            if (!await ValidateRefs(dto.StudentId, dto.SubjectId, schoolId.Value))
                return BadRequest(ApiResponse<bool>.Fail("Référence invalide."));

            // Un enseignant n'écrit que sur les élèves de ses classes (§150).
            if (!await _context.CanAccessStudentAsync(
                    User.GetRole(), userId.Value, schoolId.Value, dto.StudentId))
                return BadRequest(ApiResponse<bool>.Fail("Cet élève n'est pas dans vos classes."));

            var date = dto.Date.ToUtcDay();
            // On charge meme les soft-deleted pour pouvoir les ressusciter si l'enseignant
            // re-saisit un rapport apres l'avoir supprime.
            var existing = await _context.DailyJournalEntries.FirstOrDefaultAsync(j =>
                j.StudentId == dto.StudentId
                && j.Date == date
                && j.TeacherId == userId.Value
                && j.SubjectId == dto.SubjectId);

            if (existing != null)
            {
                existing.LearnedToday = dto.LearnedToday;
                existing.BehaviorScore = dto.BehaviorScore;
                existing.EffortScore = dto.EffortScore;
                existing.UpdatedAt = DateTime.UtcNow;
                if (existing.IsDeleted)
                {
                    existing.IsDeleted = false;
                    existing.DeletedAt = null;
                    existing.DeletedById = null;
                }
            }
            else
            {
                _context.DailyJournalEntries.Add(new DailyJournalEntry
                {
                    SchoolId = schoolId.Value,
                    StudentId = dto.StudentId,
                    TeacherId = userId.Value,
                    SubjectId = dto.SubjectId,
                    Date = date,
                    LearnedToday = dto.LearnedToday,
                    BehaviorScore = dto.BehaviorScore,
                    EffortScore = dto.EffortScore,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await _context.SaveChangesAsync();
            return Ok(ApiResponse<bool>.Ok(true, "Rapport enregistré."));
        }

        [HttpPost("bulk")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> Bulk([FromBody] DailyJournalBulkDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            var role = User.GetRole();
            if (schoolId == null || userId == null) return Unauthorized();

            if (dto.SubjectId.HasValue)
            {
                var subOk = await _context.Subjects
                    .AnyAsync(s => s.Id == dto.SubjectId.Value && s.SchoolId == schoolId.Value && !s.IsDeleted);
                if (!subOk) return BadRequest(ApiResponse<bool>.Fail("Matière introuvable."));
            }

            var date = dto.Date.ToUtcDay();
            var studentIds = dto.Entries.Select(e => e.StudentId).ToList();

            // Périmètre de l'appelant : un enseignant ne rédige que pour ses
            // classes. Les élèves hors périmètre sont ignorés comme ceux d'une
            // autre école (§16).
            var visible = await _context.VisibleClassIdsAsync(
                User.GetRole(), userId.Value, schoolId.Value);

            // Enrolled() : un élève sorti est ignoré comme un élève d'une autre
            // école (§16) — plus de saisie de journal sur lui (D4).
            var validIds = await _context.Students
                .Where(s => studentIds.Contains(s.Id) && s.SchoolId == schoolId.Value)
                .Enrolled()
                .Where(s => visible == null
                    || (s.ClassId != null && visible.Contains(s.ClassId.Value)))
                .Select(s => s.Id).ToListAsync();

            // Existant pour upsert
            var existing = await _context.DailyJournalEntries
                .Where(j => validIds.Contains(j.StudentId)
                            && j.Date == date
                            && j.TeacherId == userId.Value
                            && j.SubjectId == dto.SubjectId)
                .ToDictionaryAsync(j => j.StudentId);

            var saved = 0;
            var deleted = 0;
            // Entrées trop anciennes pour cet enseignant : on les compte pour
            // le DIRE. Les passer en silence laisserait croire à une saisie
            // enregistrée qui ne l'a pas été — le défaut du §146.
            var locked = 0;
            var notifyStudents = new HashSet<int>();
            foreach (var entry in dto.Entries.Where(e => validIds.Contains(e.StudentId)))
            {
                var hasContent = !string.IsNullOrWhiteSpace(entry.LearnedToday)
                                 || entry.BehaviorScore.HasValue
                                 || entry.EffortScore.HasValue;

                if (existing.TryGetValue(entry.StudentId, out var rec))
                {
                    // Verrou 48 h (D7). Le lot est le chemin PRINCIPAL de
                    // saisie : ne le protéger que sur les endpoints unitaires
                    // laisserait la feuille de classe tout réécrire. Une entrée
                    // verrouillée est passée, jamais refusée en bloc — l'écran
                    // ne propose la modification que de ce qui est modifiable,
                    // et une saisie de vingt élèves ne doit pas échouer parce
                    // qu'une seule ligne est trop ancienne.
                    if (!EditWindow.CanEdit(role, rec.CreatedAt))
                    {
                        locked++;
                        continue;
                    }

                    if (!hasContent)
                    {
                        // Suppression uniquement si l'enseignant l'a explicitement demandé
                        // (via dto.DeleteEmpty). Évite les pertes accidentelles.
                        if (dto.DeleteEmpty)
                        {
                            rec.IsDeleted = true;
                            rec.DeletedAt = DateTime.UtcNow;
                            rec.DeletedById = userId.Value;
                            rec.UpdatedAt = DateTime.UtcNow;
                            deleted++;
                        }
                        // sinon : on laisse l'entrée précédente intacte.
                    }
                    else
                    {
                        rec.LearnedToday = entry.LearnedToday ?? string.Empty;
                        rec.BehaviorScore = entry.BehaviorScore;
                        rec.EffortScore = entry.EffortScore;
                        rec.UpdatedAt = DateTime.UtcNow;
                        // Re-saisie explicite : reactiver si etait soft-deleted.
                        if (rec.IsDeleted)
                        {
                            rec.IsDeleted = false;
                            rec.DeletedAt = null;
                            rec.DeletedById = null;
                        }
                        saved++;
                        notifyStudents.Add(entry.StudentId);
                    }
                }
                else if (hasContent)
                {
                    _context.DailyJournalEntries.Add(new DailyJournalEntry
                    {
                        SchoolId = schoolId.Value,
                        StudentId = entry.StudentId,
                        TeacherId = userId.Value,
                        SubjectId = dto.SubjectId,
                        Date = date,
                        LearnedToday = entry.LearnedToday ?? string.Empty,
                        BehaviorScore = entry.BehaviorScore,
                        EffortScore = entry.EffortScore,
                        CreatedAt = DateTime.UtcNow
                    });
                    saved++;
                    notifyStudents.Add(entry.StudentId);
                }
                // sinon (pas d'entrée existante + pas de contenu) : on ignore silencieusement.
            }
            await _context.SaveChangesAsync();

            // Notif parents (push, post-commit, best-effort, 1/élève/jour).
            await NotifyJournalAsync(notifyStudents);

            var msg = deleted > 0
                ? $"{saved} rapport(s) enregistré(s), {deleted} supprimé(s)."
                : $"{saved} rapport(s) enregistré(s).";
            if (locked > 0)
                msg += $" {locked} rapport(s) de plus de {EditWindow.Hours} h n'ont pas été "
                     + "modifiés : demandez à la direction du daara.";
            return Ok(ApiResponse<bool>.Ok(true, msg));
        }

        [HttpPut("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> Update(int id, [FromBody] DailyJournalEntryUpdateDto dto)
        {
            if (id != dto.Id) return BadRequest(ApiResponse<bool>.Fail("ID mismatch."));

            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            var role = User.GetRole();
            if (schoolId == null || userId == null) return Unauthorized();

            var entity = await _context.DailyJournalEntries
                .FirstOrDefaultAsync(j => j.Id == id && j.SchoolId == schoolId.Value && !j.IsDeleted);
            if (entity == null) return NotFound();

            // Un enseignant ne peut modifier que ses propres entrées ; admin/staff peuvent tout.
            var isAdminLevel = role == UserRoles.SchoolAdmin || role == UserRoles.SchoolStaff;
            if (!isAdminLevel && entity.TeacherId != userId.Value) return Forbid();

            // Verrou 48 h, étendu du suivi coranique au journal (D7) : les deux
            // relevés vivent désormais derrière le même écran, la règle ne peut
            // pas s'appliquer à l'un seulement.
            if (!EditWindow.CanEdit(role, entity.CreatedAt))
                return BadRequest(ApiResponse<bool>.Fail(EditWindow.RefusalMessage()));

            entity.LearnedToday = dto.LearnedToday;
            entity.BehaviorScore = dto.BehaviorScore;
            entity.EffortScore = dto.EffortScore;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(ApiResponse<bool>.Ok(true));
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> Delete(int id)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            var role = User.GetRole();
            if (schoolId == null || userId == null) return Unauthorized();

            var entity = await _context.DailyJournalEntries
                .FirstOrDefaultAsync(j => j.Id == id && j.SchoolId == schoolId.Value && !j.IsDeleted);
            if (entity == null) return NotFound();

            var isAdminLevel = role == UserRoles.SchoolAdmin || role == UserRoles.SchoolStaff;
            if (!isAdminLevel && entity.TeacherId != userId.Value) return Forbid();

            // ⚠️ MÊME verrou que la modification : sans lui, il suffirait de
            // supprimer puis de re-saisir pour contourner le délai (§151).
            if (!EditWindow.CanEdit(role, entity.CreatedAt))
                return BadRequest(ApiResponse<bool>.Fail(EditWindow.RefusalMessage(deleting: true)));

            // Soft-delete + audit (traçabilité conformité).
            entity.IsDeleted = true;
            entity.DeletedAt = DateTime.UtcNow;
            entity.DeletedById = userId.Value;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ----- Helpers -----

        private async Task<bool> ValidateRefs(int studentId, int? subjectId, int schoolId)
        {
            // Enrolled() : la saisie unitaire suit la même règle que le lot —
            // plus de journal sur un élève sorti (D4).
            var sOk = await _context.Students
                .Where(s => s.Id == studentId && s.SchoolId == schoolId)
                .Enrolled()
                .AnyAsync();
            if (!sOk) return false;
            if (subjectId.HasValue)
            {
                var subOk = await _context.Subjects.AnyAsync(s => s.Id == subjectId.Value && s.SchoolId == schoolId && !s.IsDeleted);
                if (!subOk) return false;
            }
            return true;
        }

        /// <param name="role">Rôle du LECTEUR : c'est de lui que dépend le
        /// verrou, pas de l'auteur de la saisie.</param>
        private static DailyJournalEntryDto Map(DailyJournalEntry j, string? role = null) => new()
        {
            Id = j.Id,
            StudentId = j.StudentId,
            StudentName = j.Student != null ? $"{j.Student.FirstName} {j.Student.LastName}" : string.Empty,
            TeacherId = j.TeacherId,
            TeacherName = j.Teacher?.FullName ?? j.Teacher?.Email ?? string.Empty,
            SubjectId = j.SubjectId,
            SubjectName = j.Subject?.Name,
            Date = j.Date,
            LearnedToday = j.LearnedToday,
            BehaviorScore = j.BehaviorScore,
            EffortScore = j.EffortScore,
            CreatedAt = j.CreatedAt,
            UpdatedAt = j.UpdatedAt,
            Editable = EditWindow.CanEdit(role, j.CreatedAt),
            EditWindowHours = EditWindow.Hours
        };
    }
}
