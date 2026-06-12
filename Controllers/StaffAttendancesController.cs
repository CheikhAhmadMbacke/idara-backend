using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Operations;
using Idara.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Pointage de présence du personnel et des enseignants (rôle Surveillant +
    /// SchoolAdmin/Staff). Pendant de <see cref="AttendancesController"/> côté élèves.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/staff-attendances")]
    public class StaffAttendancesController : ControllerBase
    {
        private readonly AppDbContext _context;
        public StaffAttendancesController(AppDbContext context) => _context = context;

        // Rôles « pointables » : enseignants + personnel + surveillants. On exclut
        // SchoolAdmin (direction) et Guardian (parents, hors école).
        private static readonly string[] StaffableRoles =
            { UserRoles.Teacher, UserRoles.SchoolStaff, UserRoles.Surveillant };

        /// <summary>Liste les membres du personnel pointables de l'école.</summary>
        [HttpGet("staff")]
        public async Task<ActionResult<IEnumerable<StaffMemberDto>>> GetStaff(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var staff = await _context.Users
                .Where(u => u.SchoolId == schoolId.Value
                    && !u.IsDeleted
                    && StaffableRoles.Contains(u.Role))
                .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                .Select(u => new StaffMemberDto
                {
                    Id = u.Id,
                    FullName = u.FullName ?? string.Empty,
                    Role = u.Role,
                    PhoneNumber = u.PhoneNumber
                })
                .ToListAsync(ct);

            return Ok(staff);
        }

        /// <summary>Pointages du personnel (filtres date/staff optionnels).</summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<StaffAttendanceDto>>> Get(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? staffId,
            CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var query = _context.StaffAttendances
                .Include(a => a.Staff)
                .Where(a => a.SchoolId == schoolId.Value && !a.IsDeleted);

            if (from.HasValue) query = query.Where(a => a.Date >= from.Value.ToUtcDay());
            if (to.HasValue) query = query.Where(a => a.Date <= to.Value.ToUtcDay());
            if (staffId.HasValue) query = query.Where(a => a.StaffId == staffId.Value);

            var items = await query.OrderByDescending(a => a.Date).ToListAsync(ct);
            return Ok(items.Select(a => new StaffAttendanceDto
            {
                Id = a.Id,
                StaffId = a.StaffId,
                StaffName = a.Staff.FullName ?? string.Empty,
                StaffRole = a.Staff.Role,
                Date = a.Date,
                Status = a.Status,
                Reason = a.Reason
            }));
        }

        /// <summary>
        /// Pointage en lot du personnel pour une journée. Upsert par (StaffId, Date) :
        /// une entrée existante est mise à jour (et ressuscitée si soft-deleted),
        /// sinon créée. Réservé SchoolAdmin / SchoolStaff / Surveillant.
        /// </summary>
        [HttpPost("bulk")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Surveillant}")]
        public async Task<IActionResult> Bulk([FromBody] StaffAttendanceBulkDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var date = dto.Date.ToUtcDay();
            var staffIds = dto.Entries.Select(e => e.StaffId).ToList();

            // Sécurité multi-tenant : le staff doit appartenir à l'école et être pointable.
            var validStaffIds = await _context.Users
                .Where(u => staffIds.Contains(u.Id)
                    && u.SchoolId == schoolId.Value
                    && !u.IsDeleted
                    && StaffableRoles.Contains(u.Role))
                .Select(u => u.Id).ToListAsync();

            // Inclut les soft-deleted dans le lookup pour pouvoir les ressusciter.
            var existing = await _context.StaffAttendances
                .Where(a => validStaffIds.Contains(a.StaffId) && a.Date == date)
                .ToDictionaryAsync(a => a.StaffId);

            var saved = 0;
            foreach (var entry in dto.Entries.Where(e => validStaffIds.Contains(e.StaffId)))
            {
                if (existing.TryGetValue(entry.StaffId, out var rec))
                {
                    rec.Status = entry.Status;
                    rec.Reason = entry.Reason;
                    rec.RecordedById = userId.Value;
                    rec.RecordedAt = DateTime.UtcNow;
                    if (rec.IsDeleted)
                    {
                        rec.IsDeleted = false;
                        rec.DeletedAt = null;
                        rec.DeletedById = null;
                    }
                }
                else
                {
                    _context.StaffAttendances.Add(new StaffAttendance
                    {
                        SchoolId = schoolId.Value,
                        StaffId = entry.StaffId,
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
            return Ok(ApiResponse<bool>.Ok(true, $"{saved} pointage(s) enregistré(s)."));
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Delete(int id)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var entity = await _context.StaffAttendances
                .FirstOrDefaultAsync(a => a.Id == id && a.SchoolId == schoolId.Value && !a.IsDeleted);
            if (entity == null) return NotFound();

            entity.IsDeleted = true;
            entity.DeletedAt = DateTime.UtcNow;
            entity.DeletedById = userId.Value;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
