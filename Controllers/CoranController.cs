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
    public class CoranController : ControllerBase
    {
        private readonly AppDbContext _context;
        public CoranController(AppDbContext context) => _context = context;

        // ----- Progress (1 par élève) -----

        [HttpGet("progress/{studentId}")]
        public async Task<IActionResult> GetProgress(int studentId)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            if (!await StudentBelongsToSchool(studentId, schoolId.Value))
                return NotFound();

            var p = await _context.CoranProgresses.FirstOrDefaultAsync(x => x.StudentId == studentId);
            if (p == null) return Ok((CoranProgressDto?)null);
            return Ok(MapProgress(p));
        }

        [HttpPut("progress/{studentId}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> UpsertProgress(int studentId, [FromBody] CoranProgressUpsertDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            if (!await StudentBelongsToSchool(studentId, schoolId.Value))
                return NotFound();

            var p = await _context.CoranProgresses.FirstOrDefaultAsync(x => x.StudentId == studentId);
            if (p == null)
            {
                p = new CoranProgress
                {
                    SchoolId = schoolId.Value,
                    StudentId = studentId,
                };
                _context.CoranProgresses.Add(p);
            }
            p.CurrentSurah = dto.CurrentSurah;
            p.CurrentVerse = dto.CurrentVerse;
            p.CurrentHizb = dto.CurrentHizb;
            p.CurrentJuz = dto.CurrentJuz;
            p.MemorizedFromSurah = dto.MemorizedFromSurah;
            p.MemorizedFromVerse = dto.MemorizedFromVerse;
            p.MemorizedToSurah = dto.MemorizedToSurah;
            p.MemorizedToVerse = dto.MemorizedToVerse;
            p.Notes = dto.Notes;
            p.UpdatedById = userId.Value;
            p.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(MapProgress(p));
        }

        // ----- Sessions -----

        [HttpGet("sessions")]
        public async Task<IActionResult> GetSessions([FromQuery] int? studentId, [FromQuery] int? classId)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var query = _context.CoranSessions
                .Include(s => s.Student).Include(s => s.Teacher)
                .Where(s => s.SchoolId == schoolId.Value);

            if (studentId.HasValue) query = query.Where(s => s.StudentId == studentId.Value);
            if (classId.HasValue) query = query.Where(s => s.Student.ClassId == classId.Value);

            var items = await query.OrderByDescending(s => s.Date).ToListAsync();
            return Ok(items.Select(MapSession));
        }

        [HttpPost("sessions")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> CreateSession([FromBody] CoranSessionCreateDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            if (!await StudentBelongsToSchool(dto.StudentId, schoolId.Value))
                return BadRequest(ApiResponse<bool>.Fail("Élève introuvable."));

            // Au moins un score doit être fourni (sinon la session n'a aucun sens
            // et ne mettra pas à jour les moyennes).
            if (!dto.RecitationScore.HasValue && !dto.TajwidScore.HasValue && !dto.MemorizationScore.HasValue)
                return BadRequest(ApiResponse<bool>.Fail(
                    "Au moins un score (récitation, tajwid ou mémorisation) doit être renseigné."));

            var entity = new CoranSession
            {
                SchoolId = schoolId.Value,
                StudentId = dto.StudentId,
                Date = dto.Date,
                Kind = dto.Kind,
                FromSurah = dto.FromSurah,
                FromVerse = dto.FromVerse,
                ToSurah = dto.ToSurah,
                ToVerse = dto.ToVerse,
                RecitationScore = dto.RecitationScore,
                TajwidScore = dto.TajwidScore,
                MemorizationScore = dto.MemorizationScore,
                Comment = dto.Comment,
                TeacherId = userId.Value
            };

            // Transaction : la session ET la mise à jour des moyennes doivent
            // réussir ensemble (sinon CoranProgress reste désynchronisé).
            using var tx = await _context.Database.BeginTransactionAsync();

            _context.CoranSessions.Add(entity);
            await _context.SaveChangesAsync();

            await UpdateProgressAverages(dto.StudentId, schoolId.Value, userId.Value);

            await tx.CommitAsync();

            var saved = await _context.CoranSessions
                .Include(s => s.Student).Include(s => s.Teacher)
                .FirstAsync(s => s.Id == entity.Id);
            return Ok(MapSession(saved));
        }

        [HttpDelete("sessions/{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> DeleteSession(int id)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var entity = await _context.CoranSessions
                .FirstOrDefaultAsync(s => s.Id == id && s.SchoolId == schoolId.Value);
            if (entity == null) return NotFound();

            using var tx = await _context.Database.BeginTransactionAsync();

            _context.CoranSessions.Remove(entity);
            await _context.SaveChangesAsync();

            // Les moyennes de progress doivent refléter la suppression.
            await UpdateProgressAverages(entity.StudentId, schoolId.Value, userId.Value);

            await tx.CommitAsync();
            return NoContent();
        }

        // ----- Helpers -----

        private async Task<bool> StudentBelongsToSchool(int studentId, int schoolId) =>
            await _context.Students.AnyAsync(s => s.Id == studentId && s.SchoolId == schoolId && !s.IsDeleted);

        private async Task UpdateProgressAverages(int studentId, int schoolId, int userId)
        {
            var sessions = await _context.CoranSessions
                .Where(s => s.StudentId == studentId && s.SchoolId == schoolId)
                .ToListAsync();
            if (sessions.Count == 0) return;

            var avgRec = sessions.Where(s => s.RecitationScore.HasValue).Select(s => (double)s.RecitationScore!.Value).DefaultIfEmpty().Average();
            var avgTaj = sessions.Where(s => s.TajwidScore.HasValue).Select(s => (double)s.TajwidScore!.Value).DefaultIfEmpty().Average();

            var p = await _context.CoranProgresses.FirstOrDefaultAsync(x => x.StudentId == studentId);
            if (p == null)
            {
                p = new CoranProgress { SchoolId = schoolId, StudentId = studentId };
                _context.CoranProgresses.Add(p);
            }
            p.AverageRecitation = Math.Round(avgRec, 2);
            p.AverageTajwid = Math.Round(avgTaj, 2);
            p.UpdatedAt = DateTime.UtcNow;
            p.UpdatedById = userId;
            await _context.SaveChangesAsync();
        }

        private static CoranProgressDto MapProgress(CoranProgress p) => new()
        {
            Id = p.Id,
            StudentId = p.StudentId,
            CurrentSurah = p.CurrentSurah,
            CurrentVerse = p.CurrentVerse,
            CurrentHizb = p.CurrentHizb,
            CurrentJuz = p.CurrentJuz,
            MemorizedFromSurah = p.MemorizedFromSurah,
            MemorizedFromVerse = p.MemorizedFromVerse,
            MemorizedToSurah = p.MemorizedToSurah,
            MemorizedToVerse = p.MemorizedToVerse,
            AverageRecitation = p.AverageRecitation,
            AverageTajwid = p.AverageTajwid,
            Notes = p.Notes,
            UpdatedAt = p.UpdatedAt
        };

        private static CoranSessionDto MapSession(CoranSession s) => new()
        {
            Id = s.Id,
            StudentId = s.StudentId,
            StudentName = s.Student != null ? $"{s.Student.FirstName} {s.Student.LastName}" : string.Empty,
            Date = s.Date,
            Kind = s.Kind,
            FromSurah = s.FromSurah,
            FromVerse = s.FromVerse,
            ToSurah = s.ToSurah,
            ToVerse = s.ToVerse,
            RecitationScore = s.RecitationScore,
            TajwidScore = s.TajwidScore,
            MemorizationScore = s.MemorizationScore,
            Comment = s.Comment,
            TeacherName = s.Teacher?.FullName ?? s.Teacher?.Email
        };
    }
}
