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
        private readonly Services.IAttendanceService _attendance;
        public AttendancesController(AppDbContext context, INotificationService notif,
            Services.IAttendanceService attendance)
        {
            _context = context;
            _notif = notif;
            _attendance = attendance;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] AttendanceQueryDto q)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // Périmètre de l'appelant : un enseignant ne consulte que les
            // présences des élèves de ses classes (§150).
            var visible = await _context.VisibleClassIdsAsync(
                User.GetRole(), userId.Value, schoolId.Value);

            var query = _context.Attendances
                .Include(a => a.Student)
                .Where(a => a.SchoolId == schoolId.Value && !a.IsDeleted)
                .Where(a => visible == null
                    || (a.Student.ClassId != null && visible.Contains(a.Student.ClassId.Value)));

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
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher},{UserRoles.Surveillant}")]
        public async Task<IActionResult> Bulk([FromBody] AttendanceBulkDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // Même chemin que l'assistant (§199) : périmètre enseignant,
            // élèves sortis ignorés, résurrection, notification des absents.
            var saved = await _attendance.RecordBulkAsync(
                schoolId.Value, userId.Value, User.GetRole(), dto.Date,
                dto.Entries.Select(e => new Services.AttendanceEntryInput(e.StudentId, e.Status, e.Reason)).ToList());

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
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // Même périmètre que la liste : une synthèse non filtrée révélerait
            // les statistiques de présence de toute l'école.
            var visible = await _context.VisibleClassIdsAsync(
                User.GetRole(), userId.Value, schoolId.Value);

            var query = _context.Attendances
                .Include(a => a.Student)
                .Where(a => a.SchoolId == schoolId.Value && !a.IsDeleted)
                .Where(a => visible == null
                    || (a.Student.ClassId != null && visible.Contains(a.Student.ClassId.Value)));

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
