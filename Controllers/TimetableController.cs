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
    [Authorize]
    [ApiController]
    [Route("api/timetable")]
    public class TimetableController : ControllerBase
    {
        private readonly AppDbContext _context;
        public TimetableController(AppDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] int? classId, [FromQuery] int? teacherId)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var query = _context.TimetableSlots
                .Include(t => t.Class).Include(t => t.Subject).Include(t => t.Teacher)
                .Where(t => t.SchoolId == schoolId.Value);

            if (classId.HasValue)   query = query.Where(t => t.ClassId == classId.Value);
            if (teacherId.HasValue) query = query.Where(t => t.TeacherId == teacherId.Value);

            var items = await query.OrderBy(t => t.DayOfWeek).ThenBy(t => t.StartTime).ToListAsync();
            return Ok(items.Select(Map));
        }

        [HttpPost]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Create([FromBody] TimetableSlotCreateDto dto)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            if (!await ValidateRefs(dto.ClassId, dto.SubjectId, dto.TeacherId, schoolId.Value))
                return BadRequest(ApiResponse<bool>.Fail("Référence invalide."));

            if (string.Compare(dto.StartTime, dto.EndTime, StringComparison.Ordinal) >= 0)
                return BadRequest(ApiResponse<bool>.Fail("L'heure de fin doit être après l'heure de début."));

            var conflict = await FindConflictAsync(schoolId.Value, dto.ClassId, dto.TeacherId,
                dto.DayOfWeek, dto.StartTime, dto.EndTime, ignoreSlotId: null);
            if (conflict != null)
                return BadRequest(ApiResponse<bool>.Fail(conflict));

            var entity = new TimetableSlot
            {
                SchoolId = schoolId.Value,
                ClassId = dto.ClassId,
                SubjectId = dto.SubjectId,
                TeacherId = dto.TeacherId,
                DayOfWeek = dto.DayOfWeek,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                Room = dto.Room
            };
            _context.TimetableSlots.Add(entity);
            await _context.SaveChangesAsync();

            var saved = await _context.TimetableSlots
                .Include(t => t.Class).Include(t => t.Subject).Include(t => t.Teacher)
                .FirstAsync(t => t.Id == entity.Id);
            return Ok(Map(saved));
        }

        [HttpPut("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Update(int id, [FromBody] TimetableSlotUpdateDto dto)
        {
            if (id != dto.Id) return BadRequest(ApiResponse<bool>.Fail("ID mismatch."));
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var entity = await _context.TimetableSlots
                .FirstOrDefaultAsync(t => t.Id == id && t.SchoolId == schoolId.Value);
            if (entity == null) return NotFound();

            if (!await ValidateRefs(dto.ClassId, dto.SubjectId, dto.TeacherId, schoolId.Value))
                return BadRequest(ApiResponse<bool>.Fail("Référence invalide."));

            if (string.Compare(dto.StartTime, dto.EndTime, StringComparison.Ordinal) >= 0)
                return BadRequest(ApiResponse<bool>.Fail("L'heure de fin doit être après l'heure de début."));

            var conflict = await FindConflictAsync(schoolId.Value, dto.ClassId, dto.TeacherId,
                dto.DayOfWeek, dto.StartTime, dto.EndTime, ignoreSlotId: id);
            if (conflict != null)
                return BadRequest(ApiResponse<bool>.Fail(conflict));

            entity.ClassId = dto.ClassId;
            entity.SubjectId = dto.SubjectId;
            entity.TeacherId = dto.TeacherId;
            entity.DayOfWeek = dto.DayOfWeek;
            entity.StartTime = dto.StartTime;
            entity.EndTime = dto.EndTime;
            entity.Room = dto.Room;
            await _context.SaveChangesAsync();

            var saved = await _context.TimetableSlots
                .Include(t => t.Class).Include(t => t.Subject).Include(t => t.Teacher)
                .FirstAsync(t => t.Id == entity.Id);
            return Ok(Map(saved));
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Delete(int id)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var entity = await _context.TimetableSlots
                .FirstOrDefaultAsync(t => t.Id == id && t.SchoolId == schoolId.Value);
            if (entity == null) return NotFound();

            _context.TimetableSlots.Remove(entity);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private async Task<bool> ValidateRefs(int classId, int subjectId, int? teacherId, int schoolId)
        {
            var cOk = await _context.Classes.AnyAsync(c => c.Id == classId && c.SchoolId == schoolId && !c.IsDeleted);
            var subOk = await _context.Subjects.AnyAsync(s => s.Id == subjectId && s.SchoolId == schoolId && !s.IsDeleted);
            if (teacherId.HasValue)
            {
                var tOk = await _context.Users.AnyAsync(u => u.Id == teacherId.Value && u.SchoolId == schoolId && u.Role == UserRoles.Teacher);
                if (!tOk) return false;
            }
            return cOk && subOk;
        }

        /// <summary>
        /// Vérifie qu'aucun autre créneau ne chevauche [start, end[ pour la même classe le même jour,
        /// et que l'enseignant (s'il est précisé) n'a pas déjà cours ailleurs sur ce créneau.
        /// </summary>
        private async Task<string?> FindConflictAsync(
            int schoolId, int classId, int? teacherId, int dayOfWeek,
            string start, string end, int? ignoreSlotId)
        {
            // Chevauchement : (start < otherEnd) AND (end > otherStart)
            var classConflict = await _context.TimetableSlots
                .Include(t => t.Subject)
                .Where(t => t.SchoolId == schoolId
                            && t.ClassId == classId
                            && t.DayOfWeek == dayOfWeek
                            && (ignoreSlotId == null || t.Id != ignoreSlotId)
                            && string.Compare(start, t.EndTime) < 0
                            && string.Compare(end, t.StartTime) > 0)
                .FirstOrDefaultAsync();
            if (classConflict != null)
                return $"Conflit : cette classe a déjà '{classConflict.Subject?.Name}' " +
                       $"de {classConflict.StartTime} à {classConflict.EndTime} ce jour.";

            if (teacherId.HasValue)
            {
                var teacherConflict = await _context.TimetableSlots
                    .Include(t => t.Class)
                    .Where(t => t.SchoolId == schoolId
                                && t.TeacherId == teacherId.Value
                                && t.DayOfWeek == dayOfWeek
                                && (ignoreSlotId == null || t.Id != ignoreSlotId)
                                && string.Compare(start, t.EndTime) < 0
                                && string.Compare(end, t.StartTime) > 0)
                    .FirstOrDefaultAsync();
                if (teacherConflict != null)
                    return $"Conflit : cet enseignant a déjà cours en '{teacherConflict.Class?.Name}' " +
                           $"de {teacherConflict.StartTime} à {teacherConflict.EndTime} ce jour.";
            }

            return null;
        }

        private static TimetableSlotDto Map(TimetableSlot t) => new()
        {
            Id = t.Id,
            ClassId = t.ClassId,
            ClassName = t.Class?.Name ?? string.Empty,
            SubjectId = t.SubjectId,
            SubjectName = t.Subject?.Name ?? string.Empty,
            TeacherId = t.TeacherId,
            TeacherName = t.Teacher?.FullName ?? t.Teacher?.Email,
            DayOfWeek = t.DayOfWeek,
            StartTime = t.StartTime,
            EndTime = t.EndTime,
            Room = t.Room
        };
    }
}
