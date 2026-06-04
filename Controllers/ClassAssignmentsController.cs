using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Academic;
using Idara.API.DTOs.Common;
using Idara.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/class-assignments")]
    public class ClassAssignmentsController : ControllerBase
    {
        private readonly AppDbContext _context;
        public ClassAssignmentsController(AppDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int? classId, [FromQuery] int? teacherId)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var query = _context.ClassSubjectTeachers
                .Include(a => a.Class).Include(a => a.Subject).Include(a => a.Teacher)
                .Where(a => a.SchoolId == schoolId.Value);

            if (classId.HasValue)   query = query.Where(a => a.ClassId == classId.Value);
            if (teacherId.HasValue) query = query.Where(a => a.TeacherId == teacherId.Value);

            var items = await query.OrderBy(a => a.Class.Name).ThenBy(a => a.Subject.Name).ToListAsync();
            return Ok(items.Select(MapToDto));
        }

        [HttpPost]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Create([FromBody] ClassSubjectTeacherCreateDto dto)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            // Vérifier que les entités appartiennent à l'école
            var classExists = await _context.Classes.AnyAsync(c => c.Id == dto.ClassId && c.SchoolId == schoolId && !c.IsDeleted);
            var subjectExists = await _context.Subjects.AnyAsync(s => s.Id == dto.SubjectId && s.SchoolId == schoolId && !s.IsDeleted);
            var teacher = await _context.Users.FirstOrDefaultAsync(u => u.Id == dto.TeacherId && u.SchoolId == schoolId && u.Role == UserRoles.Teacher && !u.IsDeleted);

            if (!classExists)   return BadRequest(ApiResponse<bool>.Fail("Classe introuvable."));
            if (!subjectExists) return BadRequest(ApiResponse<bool>.Fail("Matière introuvable."));
            if (teacher == null) return BadRequest(ApiResponse<bool>.Fail("Enseignant introuvable."));

            var dup = await _context.ClassSubjectTeachers.AnyAsync(a =>
                a.ClassId == dto.ClassId && a.SubjectId == dto.SubjectId && a.TeacherId == dto.TeacherId);
            if (dup) return BadRequest(ApiResponse<bool>.Fail("Cette affectation existe déjà."));

            var entity = new ClassSubjectTeacher
            {
                SchoolId = schoolId.Value,
                ClassId = dto.ClassId,
                SubjectId = dto.SubjectId,
                TeacherId = dto.TeacherId,
                AssignedAt = DateTime.UtcNow
            };
            _context.ClassSubjectTeachers.Add(entity);
            await _context.SaveChangesAsync();

            // Recharger pour les noms
            var saved = await _context.ClassSubjectTeachers
                .Include(a => a.Class).Include(a => a.Subject).Include(a => a.Teacher)
                .FirstAsync(a => a.Id == entity.Id);
            return Ok(MapToDto(saved));
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Delete(int id)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var entity = await _context.ClassSubjectTeachers
                .FirstOrDefaultAsync(a => a.Id == id && a.SchoolId == schoolId.Value);
            if (entity == null) return NotFound();

            _context.ClassSubjectTeachers.Remove(entity);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private static ClassSubjectTeacherDto MapToDto(ClassSubjectTeacher a) => new()
        {
            Id = a.Id,
            ClassId = a.ClassId,
            ClassName = a.Class?.Name ?? string.Empty,
            SubjectId = a.SubjectId,
            SubjectName = a.Subject?.Name ?? string.Empty,
            SubjectKind = a.Subject?.Kind ?? Enums.SubjectKind.General,
            TeacherId = a.TeacherId,
            TeacherName = a.Teacher?.FullName ?? a.Teacher?.Email ?? string.Empty,
            AssignedAt = a.AssignedAt
        };
    }
}
