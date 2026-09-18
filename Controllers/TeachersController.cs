using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Academic;
using Idara.API.DTOs.School;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class TeachersController : ControllerBase
    {
        private readonly AppDbContext _context;
        public TeachersController(AppDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var schoolId = User.GetSchoolId();
            // 🔴 Un compte AUTHENTIFIÉ mais sans école (parent, donateur, SuperAdmin)
            // n'a pas une session morte : lui répondre 401 fait tenter au client une
            // rotation de jeton, qui réussit, puis rejouer — et le second 401 purge la
            // session. C'est un refus d'ACCÈS (403), pas d'identité (§287).
            if (schoolId == null) return Forbid();

            var teachers = await _context.Users
                .Where(u => u.SchoolId == schoolId.Value && u.Role == UserRoles.Teacher && !u.IsDeleted)
                .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                .Select(u => new UserInfoDto
                {
                    Id = u.Id,
                    Email = u.Email,
                    FullName = u.FullName,
                    PhoneNumber = u.PhoneNumber,
                    Role = u.Role,
                    AccountStatus = u.AccountStatus,
                    CreatedAt = u.CreatedAt
                })
                .ToListAsync();
            return Ok(teachers);
        }

        /// <summary>
        /// Retourne les affectations (classe + matière) de l'enseignant connecté.
        /// Utilisé par la home Teacher pour lister ses classes.
        /// </summary>
        [HttpGet("me/classes")]
        [Authorize(Roles = UserRoles.Teacher)]
        public async Task<IActionResult> GetMyAssignments()
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var assignments = await _context.ClassSubjectTeachers
                .Include(a => a.Class).Include(a => a.Subject).Include(a => a.Teacher)
                .Where(a => a.SchoolId == schoolId.Value && a.TeacherId == userId.Value)
                .OrderBy(a => a.Class.Name).ThenBy(a => a.Subject.Name)
                .ToListAsync();

            return Ok(assignments.Select(a => new ClassSubjectTeacherDto
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
            }));
        }
    }
}
