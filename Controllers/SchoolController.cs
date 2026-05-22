using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.School;
using Idara.API.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolModel = Idara.API.Models.School;

namespace Idara.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SchoolController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SchoolController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("my-info")]
        public async Task<IActionResult> GetMySchoolInfo()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var user = await _context.Users
                .Include(u => u.School)
                    .ThenInclude(s => s!.Users)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user?.School == null)
                return NotFound(ApiResponse<bool>.Fail("Aucune école associée à cet utilisateur."));

            return Ok(MapToSchoolInfoResponse(user.School));
        }

        [Authorize(Roles = UserRoles.SuperAdmin)]
        [HttpGet("all")]
        public async Task<IActionResult> GetAllSchools([FromQuery] KycStatus? status = null)
        {
            var query = _context.Schools.Include(s => s.Users).AsQueryable();
            if (status.HasValue)
                query = query.Where(s => s.KycStatus == status.Value);

            var schools = await query.ToListAsync();
            return Ok(schools.Select(MapToSchoolInfoResponse));
        }

        [Authorize(Roles = UserRoles.SuperAdmin)]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetSchoolById(int id)
        {
            var school = await _context.Schools
                .Include(s => s.Users)
                .FirstOrDefaultAsync(s => s.Id == id);
            if (school == null)
                return NotFound(ApiResponse<bool>.Fail("École non trouvée."));

            return Ok(MapToSchoolInfoResponse(school));
        }

        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        [HttpGet("stats")]
        public async Task<ActionResult<SchoolStatsDto>> GetStats()
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var totalStudents = await _context.Students
                .CountAsync(s => s.SchoolId == schoolId.Value && !s.IsDeleted);
            var totalTeachers = await _context.Users
                .CountAsync(u => u.SchoolId == schoolId.Value && u.Role == UserRoles.Teacher);
            var totalGuardians = await _context.StudentGuardians
                .Where(sg => sg.Student.SchoolId == schoolId.Value)
                .Select(sg => sg.GuardianId)
                .Distinct()
                .CountAsync();
            var totalClasses = await _context.Classes
                .CountAsync(c => c.SchoolId == schoolId.Value && !c.IsDeleted);
            var totalSubjects = await _context.Subjects
                .CountAsync(s => s.SchoolId == schoolId.Value && !s.IsDeleted);
            var currentYear = await _context.AcademicYears
                .Where(y => y.SchoolId == schoolId.Value && y.IsCurrent)
                .Select(y => y.Name)
                .FirstOrDefaultAsync();
            var currentPeriod = await _context.AcademicPeriods
                .Where(p => p.SchoolId == schoolId.Value && p.IsCurrent)
                .Select(p => p.Name)
                .FirstOrDefaultAsync();

            return Ok(new SchoolStatsDto
            {
                TotalStudents = totalStudents,
                TotalTeachers = totalTeachers,
                TotalGuardians = totalGuardians,
                TotalClasses = totalClasses,
                TotalSubjects = totalSubjects,
                CurrentAcademicYearName = currentYear,
                CurrentAcademicPeriodName = currentPeriod
            });
        }

        private static SchoolInfoResponse MapToSchoolInfoResponse(SchoolModel school) => new()
        {
            Id = school.Id,
            Name = school.Name ?? string.Empty,
            Address = school.Address ?? string.Empty,
            PhoneNumber = school.PhoneNumber ?? string.Empty,
            LegalDocumentsUrls = school.LegalDocumentsUrl?
                .Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new(),
            KycStatus = school.KycStatus,
            RejectionReason = school.RejectionReason,
            SubmittedAt = school.SubmittedAt,
            ValidatedAt = school.ValidatedAt,
            RepresentativeFirstName = school.RepresentativeFirstName ?? string.Empty,
            RepresentativeLastName = school.RepresentativeLastName ?? string.Empty,
            RepresentativePhone = school.RepresentativePhone ?? string.Empty,
            RepresentativeIdDocumentUrls = school.RepresentativeIdDocumentUrl?
                .Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new(),
            Users = school.Users.Select(u => new UserInfoDto
            {
                Id = u.Id,
                Email = u.Email,
                FullName = u.FullName,
                Role = u.Role,
                AccountStatus = u.AccountStatus,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt
            }).ToList()
        };
    }
}
