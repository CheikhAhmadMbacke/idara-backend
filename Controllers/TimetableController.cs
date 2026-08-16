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
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // Périmètre de l'appelant (§150). L'enseignant dispose en plus de
            // `my-classes`, qui inclut les classes où il assure un créneau sans
            // affectation matière — cet endpoint-ci s'en tient aux affectations.
            var visible = await _context.VisibleClassIdsAsync(
                User.GetRole(), userId.Value, schoolId.Value);

            var query = _context.TimetableSlots
                .Include(t => t.Class).Include(t => t.Subject).Include(t => t.Teacher)
                .Where(t => t.SchoolId == schoolId.Value)
                .Where(t => visible == null || visible.Contains(t.ClassId));

            if (classId.HasValue)   query = query.Where(t => t.ClassId == classId.Value);
            if (teacherId.HasValue) query = query.Where(t => t.TeacherId == teacherId.Value);

            var items = await query.OrderBy(t => t.DayOfWeek).ThenBy(t => t.StartTime).ToListAsync();
            return Ok(items.Select(Map));
        }

        /// <summary>
        /// Emploi du temps des classes de l'enseignant connecté (lecture seule).
        /// Renvoie tous les créneaux des classes auxquelles il est affecté (via
        /// ses affectations matière OU les créneaux qu'il assure directement) —
        /// y compris les créneaux assurés par d'autres maîtres dans ces classes.
        /// </summary>
        [HttpGet("my-classes")]
        [Authorize(Roles = UserRoles.Teacher)]
        public async Task<IActionResult> MyClasses()
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // Classes où l'enseignant est affecté (affectations matière) OU déjà
            // présent dans un créneau.
            var assignedClassIds = await _context.ClassSubjectTeachers
                .Where(a => a.SchoolId == schoolId.Value && a.TeacherId == userId.Value)
                .Select(a => a.ClassId)
                .ToListAsync();
            var slotClassIds = await _context.TimetableSlots
                .Where(t => t.SchoolId == schoolId.Value && t.TeacherId == userId.Value)
                .Select(t => t.ClassId)
                .ToListAsync();

            var classIds = assignedClassIds.Concat(slotClassIds).Distinct().ToList();
            if (classIds.Count == 0) return Ok(Array.Empty<TimetableSlotDto>());

            var items = await _context.TimetableSlots
                .Include(t => t.Class).Include(t => t.Subject).Include(t => t.Teacher)
                .Where(t => t.SchoolId == schoolId.Value && classIds.Contains(t.ClassId))
                .OrderBy(t => t.DayOfWeek).ThenBy(t => t.StartTime)
                .ToListAsync();
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

        /// <summary>
        /// Duplique tous les créneaux d'un jour source vers un/plusieurs jours
        /// cibles pour une même classe. Les créneaux qui entreraient en conflit
        /// (classe ou enseignant déjà occupé) sont ignorés, pas écrasés.
        /// </summary>
        [HttpPost("duplicate")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Duplicate([FromBody] TimetableDuplicateDto dto)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var classOk = await _context.Classes
                .AnyAsync(c => c.Id == dto.ClassId && c.SchoolId == schoolId.Value && !c.IsDeleted);
            if (!classOk) return BadRequest(ApiResponse<bool>.Fail("Classe invalide."));

            var sourceSlots = await _context.TimetableSlots
                .Where(t => t.SchoolId == schoolId.Value
                            && t.ClassId == dto.ClassId
                            && t.DayOfWeek == dto.SourceDay)
                .OrderBy(t => t.StartTime)
                .ToListAsync();

            var result = new TimetableDuplicateResultDto();
            if (sourceSlots.Count == 0)
                return Ok(ApiResponse<TimetableDuplicateResultDto>.Ok(
                    result, "Aucun créneau à dupliquer pour le jour source."));

            var createdIds = new List<int>();
            foreach (var day in dto.TargetDays.Distinct())
            {
                foreach (var src in sourceSlots)
                {
                    // Conflit vérifié contre l'état DB courant — on SaveChanges
                    // après chaque insert pour que les créneaux ajoutés dans
                    // cette même opération soient pris en compte.
                    var conflict = await FindConflictAsync(
                        schoolId.Value, dto.ClassId, src.TeacherId, day,
                        src.StartTime, src.EndTime, ignoreSlotId: null);
                    if (conflict != null)
                    {
                        result.SkippedConflicts++;
                        continue;
                    }

                    var entity = new TimetableSlot
                    {
                        SchoolId = schoolId.Value,
                        ClassId = dto.ClassId,
                        SubjectId = src.SubjectId,
                        TeacherId = src.TeacherId,
                        DayOfWeek = day,
                        StartTime = src.StartTime,
                        EndTime = src.EndTime,
                        Room = src.Room
                    };
                    _context.TimetableSlots.Add(entity);
                    await _context.SaveChangesAsync();
                    createdIds.Add(entity.Id);
                    result.Copied++;
                }
            }

            if (createdIds.Count > 0)
            {
                var created = await _context.TimetableSlots
                    .Include(t => t.Class).Include(t => t.Subject).Include(t => t.Teacher)
                    .Where(t => createdIds.Contains(t.Id))
                    .OrderBy(t => t.DayOfWeek).ThenBy(t => t.StartTime)
                    .ToListAsync();
                result.CreatedSlots = created.Select(Map).ToList();
            }

            return Ok(ApiResponse<TimetableDuplicateResultDto>.Ok(
                result,
                $"{result.Copied} créneau(x) copié(s), {result.SkippedConflicts} ignoré(s) pour conflit."));
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
                var tOk = await _context.Users.AnyAsync(u => u.Id == teacherId.Value && u.SchoolId == schoolId && u.Role == UserRoles.Teacher && !u.IsDeleted);
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
