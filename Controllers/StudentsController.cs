using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Student;
using Idara.API.Models;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class StudentsController : ControllerBase
    {
        private readonly IStudentService _studentService;
        private readonly AppDbContext _context;

        public StudentsController(IStudentService studentService, AppDbContext context)
        {
            _studentService = studentService;
            _context = context;
        }

        [HttpGet]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher},{UserRoles.Surveillant}")]
        public async Task<IActionResult> GetStudents([FromQuery] StudentPaginationDto pagination)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var result = await _studentService.GetStudentsAsync(schoolId.Value, pagination);
            return Ok(result);
        }

        [HttpGet("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher},{UserRoles.Surveillant}")]
        public async Task<IActionResult> GetStudent(int id)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var student = await _studentService.GetStudentByIdAsync(id, schoolId.Value);
            return student == null ? NotFound() : Ok(student);
        }

        [HttpPost]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> CreateStudent([FromBody] StudentCreateDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var student = await _studentService.CreateStudentAsync(schoolId.Value, userId.Value, dto);
            return CreatedAtAction(nameof(GetStudent), new { id = student.Id }, student);
        }

        [HttpPut("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> UpdateStudent(int id, [FromBody] StudentUpdateDto dto)
        {
            if (id != dto.Id)
                return BadRequest(ApiResponse<bool>.Fail("L'identifiant dans l'URL ne correspond pas à celui du corps de la requête."));

            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var updated = await _studentService.UpdateStudentAsync(schoolId.Value, userId.Value, dto);
            return updated == null ? NotFound() : Ok(updated);
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> DeleteStudent(int id)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var result = await _studentService.DeleteStudentAsync(id, schoolId.Value);
            return result ? NoContent() : NotFound();
        }

        // ----- Documents -----

        [HttpPost("{id}/documents")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> AddDocument(int id, [FromBody] StudentDocumentInputDto dto)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var doc = await _studentService.AddDocumentAsync(id, schoolId.Value, dto);
            return doc == null
                ? NotFound(ApiResponse<bool>.Fail("Élève introuvable ou contenu invalide."))
                : Ok(doc);
        }

        [HttpDelete("{id}/documents/{documentId}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> DeleteDocument(int id, int documentId)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var result = await _studentService.DeleteDocumentAsync(id, documentId, schoolId.Value);
            return result ? NoContent() : NotFound();
        }

        // ----- Guardians -----

        [HttpPost("{studentId}/guardians/{guardianId}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> LinkGuardianToStudent(int studentId, int guardianId, [FromBody] LinkGuardianRequest? request = null)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Id == studentId && s.SchoolId == schoolId.Value && !s.IsDeleted);
            if (student == null) return NotFound(ApiResponse<bool>.Fail("Élève non trouvé."));

            var guardian = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == guardianId && u.Role == UserRoles.Guardian);
            if (guardian == null) return NotFound(ApiResponse<bool>.Fail("Responsable non trouvé."));

            // Anti-energie sauvage : on n'autorise le link que si le guardian est deja connu
            // dans l'ecole (a au moins un autre eleve lie). Sinon il faut passer par
            // l'invitation /auth/invite-user (qui cree le guardian + envoie ses identifiants).
            // Sans ce check, un SchoolStaff malicieux pourrait lier n'importe quel Guardian
            // global a un eleve de son ecole.
            var guardianBelongsToSchool = await _context.StudentGuardians
                .AnyAsync(sg => sg.GuardianId == guardianId && sg.Student.SchoolId == schoolId.Value);
            if (!guardianBelongsToSchool)
                return BadRequest(ApiResponse<bool>.Fail(
                    "Responsable inconnu dans cette école. Utilisez l'invitation pour créer un nouveau responsable."));

            var exists = await _context.StudentGuardians
                .AnyAsync(sg => sg.StudentId == studentId && sg.GuardianId == guardianId);
            if (exists) return BadRequest(ApiResponse<bool>.Fail("Responsable déjà lié à cet élève."));

            _context.StudentGuardians.Add(new StudentGuardian
            {
                StudentId = studentId,
                GuardianId = guardianId,
                Relationship = request?.Relationship,
                IsPrimaryGuardian = request?.IsPrimaryGuardian ?? false
            });
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
