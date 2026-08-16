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
    [Route("api/[controller]")]
    public class GradesController : ControllerBase
    {
        private readonly AppDbContext _context;
        public GradesController(AppDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> Get(
            [FromQuery] int? studentId,
            [FromQuery] int? subjectId,
            [FromQuery] int? academicPeriodId,
            [FromQuery] int? classId)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // Périmètre de l'appelant : un enseignant ne voit que les notes des
            // élèves de ses classes (§150). Le filtre porte sur la classe ACTUELLE
            // de l'élève, comme le contrôle d'accès unitaire — les deux doivent
            // répondre pareil, sinon une note listée deviendrait inéditable.
            var visible = await _context.VisibleClassIdsAsync(
                User.GetRole(), userId.Value, schoolId.Value);

            var query = _context.Grades
                .Include(g => g.Student).Include(g => g.Subject).Include(g => g.AcademicPeriod)
                .Where(g => g.SchoolId == schoolId.Value && !g.IsDeleted)
                .Where(g => visible == null
                    || (g.Student.ClassId != null && visible.Contains(g.Student.ClassId.Value)));

            if (studentId.HasValue)        query = query.Where(g => g.StudentId == studentId.Value);
            if (subjectId.HasValue)        query = query.Where(g => g.SubjectId == subjectId.Value);
            if (academicPeriodId.HasValue) query = query.Where(g => g.AcademicPeriodId == academicPeriodId.Value);
            if (classId.HasValue)          query = query.Where(g => g.ClassId == classId.Value);

            var items = await query.OrderByDescending(g => g.Date).ToListAsync();
            return Ok(items.Select(Map));
        }

        [HttpPost]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> Create([FromBody] GradeCreateDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            if (!await ValidateOwnership(dto.StudentId, dto.SubjectId, dto.AcademicPeriodId, dto.ClassId, schoolId.Value))
                return BadRequest(ApiResponse<bool>.Fail("Référence invalide."));

            // Un enseignant ne note que les élèves de ses classes (§150).
            if (!await _context.CanAccessStudentAsync(
                    User.GetRole(), userId.Value, schoolId.Value, dto.StudentId))
                return BadRequest(ApiResponse<bool>.Fail("Cet élève n'est pas dans vos classes."));

            var entity = new Grade
            {
                SchoolId = schoolId.Value,
                StudentId = dto.StudentId,
                SubjectId = dto.SubjectId,
                ClassId = dto.ClassId,
                AcademicPeriodId = dto.AcademicPeriodId,
                Value = dto.Value,
                MaxValue = dto.MaxValue,
                Coefficient = dto.Coefficient,
                EvaluationType = dto.EvaluationType,
                Date = dto.Date.ToUtcSafe(),
                Comment = dto.Comment,
                RecordedById = userId.Value,
                RecordedAt = DateTime.UtcNow
            };
            _context.Grades.Add(entity);
            await _context.SaveChangesAsync();

            var saved = await _context.Grades
                .Include(g => g.Student).Include(g => g.Subject).Include(g => g.AcademicPeriod)
                .FirstAsync(g => g.Id == entity.Id);
            return Ok(Map(saved));
        }

        [HttpPut("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> Update(int id, [FromBody] GradeUpdateDto dto)
        {
            if (id != dto.Id) return BadRequest(ApiResponse<bool>.Fail("ID mismatch."));
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var entity = await _context.Grades
                .FirstOrDefaultAsync(g => g.Id == id && g.SchoolId == schoolId.Value && !g.IsDeleted);
            if (entity == null) return NotFound();

            if (!await _context.CanAccessStudentAsync(
                    User.GetRole(), userId.Value, schoolId.Value, entity.StudentId))
                return NotFound();

            entity.Value = dto.Value;
            entity.MaxValue = dto.MaxValue;
            entity.Coefficient = dto.Coefficient;
            entity.EvaluationType = dto.EvaluationType;
            entity.Date = dto.Date.ToUtcSafe();
            entity.Comment = dto.Comment;
            await _context.SaveChangesAsync();

            var saved = await _context.Grades
                .Include(g => g.Student).Include(g => g.Subject).Include(g => g.AcademicPeriod)
                .FirstAsync(g => g.Id == entity.Id);
            return Ok(Map(saved));
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> Delete(int id)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var entity = await _context.Grades
                .FirstOrDefaultAsync(g => g.Id == id && g.SchoolId == schoolId.Value);
            if (entity == null) return NotFound();

            if (!await _context.CanAccessStudentAsync(
                    User.GetRole(), userId.Value, schoolId.Value, entity.StudentId))
                return NotFound();

            // Soft-delete + audit (conformité : impact direct sur les bulletins).
            entity.IsDeleted = true;
            entity.DeletedAt = DateTime.UtcNow;
            entity.DeletedById = userId.Value;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Synthèse des notes d'un élève sur une période (moyennes par matière + moyenne générale).</summary>
        [HttpGet("summary")]
        public async Task<IActionResult> Summary([FromQuery] int studentId, [FromQuery] int academicPeriodId)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            if (!await _context.CanAccessStudentAsync(
                    User.GetRole(), userId.Value, schoolId.Value, studentId))
                return NotFound();

            var grades = await _context.Grades
                .Include(g => g.Subject)
                .Where(g => g.SchoolId == schoolId.Value
                            && g.StudentId == studentId
                            && g.AcademicPeriodId == academicPeriodId
                            && !g.IsDeleted)
                .ToListAsync();

            var bySubject = grades
                .GroupBy(g => new { g.SubjectId, g.Subject.Name, g.Subject.DefaultCoefficient, g.Subject.DefaultMaxValue })
                .Select(g =>
                {
                    var weighted = g.Sum(x => (x.Value / x.MaxValue) * 20 * x.Coefficient);
                    var totalCoef = g.Sum(x => x.Coefficient);
                    var avgOver20 = totalCoef > 0 ? weighted / totalCoef : 0;
                    return new SubjectAverageDto
                    {
                        SubjectId = g.Key.SubjectId,
                        SubjectName = g.Key.Name,
                        Average = Math.Round(avgOver20, 2),
                        MaxValue = 20.0,
                        Coefficient = g.Key.DefaultCoefficient,
                        GradeCount = g.Count()
                    };
                })
                .ToList();

            double? general = null;
            if (bySubject.Count > 0)
            {
                var generalNum = bySubject.Sum(s => s.Average * s.Coefficient);
                var generalDen = bySubject.Sum(s => s.Coefficient);
                general = generalDen > 0 ? Math.Round(generalNum / generalDen, 2) : (double?)null;
            }

            return Ok(new StudentGradesSummaryDto
            {
                StudentId = studentId,
                AcademicPeriodId = academicPeriodId,
                Subjects = bySubject,
                GeneralAverage = general,
                HasNoGrades = bySubject.Count == 0
            });
        }

        // ----- Helpers -----

        private async Task<bool> ValidateOwnership(int studentId, int subjectId, int periodId, int? classId, int schoolId)
        {
            var sOk = await _context.Students.AnyAsync(s => s.Id == studentId && s.SchoolId == schoolId && !s.IsDeleted);
            var subOk = await _context.Subjects.AnyAsync(s => s.Id == subjectId && s.SchoolId == schoolId && !s.IsDeleted);
            var pOk = await _context.AcademicPeriods.AnyAsync(p => p.Id == periodId && p.SchoolId == schoolId);
            if (classId.HasValue)
            {
                var cOk = await _context.Classes.AnyAsync(c => c.Id == classId.Value && c.SchoolId == schoolId && !c.IsDeleted);
                if (!cOk) return false;
            }
            return sOk && subOk && pOk;
        }

        private static GradeDto Map(Grade g) => new()
        {
            Id = g.Id,
            StudentId = g.StudentId,
            StudentName = $"{g.Student.FirstName} {g.Student.LastName}",
            SubjectId = g.SubjectId,
            SubjectName = g.Subject?.Name ?? string.Empty,
            ClassId = g.ClassId,
            AcademicPeriodId = g.AcademicPeriodId,
            AcademicPeriodName = g.AcademicPeriod?.Name ?? string.Empty,
            Value = g.Value,
            MaxValue = g.MaxValue,
            Coefficient = g.Coefficient,
            EvaluationType = g.EvaluationType,
            Date = g.Date,
            Comment = g.Comment
        };
    }
}
