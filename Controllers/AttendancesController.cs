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
    [Route("api/[controller]")]
    public class AttendancesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notif;
        public AttendancesController(AppDbContext context, INotificationService notif)
        {
            _context = context;
            _notif = notif;
        }

        /// <summary>Notifie (push) les parents des élèves marqués absents. Best-effort
        /// post-commit, 1 push/élève/jour. Clic → fiche de l'enfant.</summary>
        private async Task NotifyAbsencesAsync(IReadOnlyCollection<int> absentStudentIds)
        {
            if (absentStudentIds.Count == 0) return;
            var names = await _context.Students
                .Where(s => absentStudentIds.Contains(s.Id))
                .Select(s => new { s.Id, s.FirstName, s.LastName })
                .ToListAsync();
            foreach (var s in names)
            {
                var eleve = $"{s.FirstName} {s.LastName}".Trim();
                await _notif.NotifyGuardiansOfStudentAsync(
                    s.Id, NotificationTemplates.ChildAbsent(eleve),
                    "CHILD_ABSENCE", $"/guardian/children/{s.Id}", oncePerDay: true);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] AttendanceQueryDto q)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var query = _context.Attendances
                .Include(a => a.Student)
                .Where(a => a.SchoolId == schoolId.Value && !a.IsDeleted);

            if (q.From.HasValue) query = query.Where(a => a.Date >= q.From.Value.ToUtcDay());
            if (q.To.HasValue) query = query.Where(a => a.Date <= q.To.Value.ToUtcDay());
            if (q.StudentId.HasValue) query = query.Where(a => a.StudentId == q.StudentId.Value);
            if (q.ClassId.HasValue) query = query.Where(a => a.Student.ClassId == q.ClassId.Value);

            var items = await query.OrderByDescending(a => a.Date).ToListAsync();
            return Ok(items.Select(a => new AttendanceDto
            {
                Id = a.Id,
                StudentId = a.StudentId,
                StudentName = $"{a.Student.FirstName} {a.Student.LastName}",
                Date = a.Date,
                Status = a.Status,
                Reason = a.Reason
            }));
        }

        /// <summary>
        /// Pointage en lot pour une journée. Si une entrée existe déjà
        /// pour (student, date), elle est mise à jour ; sinon elle est créée.
        /// </summary>
        [HttpPost("bulk")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> Bulk([FromBody] AttendanceBulkDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var date = dto.Date.ToUtcDay();
            var studentIds = dto.Entries.Select(e => e.StudentId).ToList();

            // Sécurité multi-tenant : tous les élèves doivent appartenir à l'école
            var validStudentIds = await _context.Students
                .Where(s => studentIds.Contains(s.Id) && s.SchoolId == schoolId.Value && !s.IsDeleted)
                .Select(s => s.Id).ToListAsync();

            // On inclut volontairement les soft-deleted dans le lookup pour pouvoir les "ressusciter"
            // (le user a re-pointe l'eleve apres une suppression manuelle, on remet IsDeleted=false).
            var existing = await _context.Attendances
                .Where(a => validStudentIds.Contains(a.StudentId) && a.Date == date)
                .ToDictionaryAsync(a => a.StudentId);

            var saved = 0;
            var absentStudents = new HashSet<int>();
            foreach (var entry in dto.Entries.Where(e => validStudentIds.Contains(e.StudentId)))
            {
                if (entry.Status == Enums.AttendanceStatus.Absent)
                    absentStudents.Add(entry.StudentId);
                if (existing.TryGetValue(entry.StudentId, out var rec))
                {
                    rec.Status = entry.Status;
                    rec.Reason = entry.Reason;
                    rec.RecordedById = userId.Value;
                    rec.RecordedAt = DateTime.UtcNow;
                    // Re-pointage explicite : reactiver si etait soft-deleted.
                    if (rec.IsDeleted)
                    {
                        rec.IsDeleted = false;
                        rec.DeletedAt = null;
                        rec.DeletedById = null;
                    }
                }
                else
                {
                    _context.Attendances.Add(new Attendance
                    {
                        SchoolId = schoolId.Value,
                        StudentId = entry.StudentId,
                        Date = date,
                        Status = entry.Status,
                        Reason = entry.Reason,
                        RecordedById = userId.Value,
                        RecordedAt = DateTime.UtcNow
                    });
                }
                saved++;
            }

            await _context.SaveChangesAsync();

            // Notif parents des absents (push, post-commit, best-effort, 1/élève/jour).
            await NotifyAbsencesAsync(absentStudents);

            return Ok(ApiResponse<bool>.Ok(true, $"{saved} pointage(s) enregistré(s)."));
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Delete(int id)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var entity = await _context.Attendances
                .FirstOrDefaultAsync(a => a.Id == id && a.SchoolId == schoolId.Value && !a.IsDeleted);
            if (entity == null) return NotFound();

            // Soft-delete + audit (conformité RGPD).
            entity.IsDeleted = true;
            entity.DeletedAt = DateTime.UtcNow;
            entity.DeletedById = userId.Value;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpGet("summary")]
        public async Task<IActionResult> Summary([FromQuery] AttendanceQueryDto q)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var query = _context.Attendances
                .Include(a => a.Student)
                .Where(a => a.SchoolId == schoolId.Value && !a.IsDeleted);

            if (q.From.HasValue) query = query.Where(a => a.Date >= q.From.Value.ToUtcDay());
            if (q.To.HasValue) query = query.Where(a => a.Date <= q.To.Value.ToUtcDay());
            if (q.ClassId.HasValue) query = query.Where(a => a.Student.ClassId == q.ClassId.Value);
            if (q.StudentId.HasValue) query = query.Where(a => a.StudentId == q.StudentId.Value);

            var grouped = await query
                .GroupBy(a => new { a.StudentId, a.Student.FirstName, a.Student.LastName })
                .Select(g => new AttendanceSummaryDto
                {
                    StudentId = g.Key.StudentId,
                    StudentName = $"{g.Key.FirstName} {g.Key.LastName}",
                    Present = g.Count(a => a.Status == Enums.AttendanceStatus.Present),
                    Absent = g.Count(a => a.Status == Enums.AttendanceStatus.Absent),
                    Late = g.Count(a => a.Status == Enums.AttendanceStatus.Late),
                    Excused = g.Count(a => a.Status == Enums.AttendanceStatus.Excused)
                })
                .ToListAsync();

            return Ok(grouped);
        }
    }
}
