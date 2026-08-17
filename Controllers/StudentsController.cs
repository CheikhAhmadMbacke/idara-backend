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
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // Un enseignant ne voit que les élèves de ses classes affectées (§150).
            var visible = await _context.VisibleClassIdsAsync(
                User.GetRole(), userId.Value, schoolId.Value);

            var result = await _studentService.GetStudentsAsync(schoolId.Value, pagination, visible);
            return Ok(result);
        }

        [HttpGet("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher},{UserRoles.Surveillant}")]
        public async Task<IActionResult> GetStudent(int id)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // Accès unitaire : sans ce contrôle, la fiche complète d'un élève
            // d'une autre classe (santé, responsables, documents) s'ouvrirait en
            // devinant son identifiant, malgré la liste filtrée.
            if (!await _context.CanAccessStudentAsync(
                    User.GetRole(), userId.Value, schoolId.Value, id))
                return NotFound();

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

        // ----- Sortie de l'effectif (2026-08-17) -----
        // « Sorti » ≠ « supprimé » : la fiche reste consultable, l'historique
        // lisible, la dette payable. Décisions produit D1-D5 du plan
        // STUDENT_LIFECYCLE_PLAN.md.

        /// <summary>
        /// Ce que la sortie impliquerait pour les dettes de l'élève à la date
        /// donnée (défaut : aujourd'hui) — appelé par le formulaire de sortie
        /// pour que la case d'annulation ne soit jamais un choix aveugle.
        /// </summary>
        [HttpGet("{id}/exit-preview")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> GetExitPreview(
            int id, [FromQuery] DateTime? exitDate, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var preview = await _studentService.GetExitPreviewAsync(
                id, schoolId.Value, exitDate ?? DateTime.UtcNow, ct);
            return preview == null
                ? NotFound(ApiResponse<bool>.Fail("Élève introuvable."))
                : Ok(preview);
        }

        [HttpPost("{id}/exit")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> ExitStudent(
            int id, [FromBody] StudentExitRequestDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // L'annulation des mensualités est réservée au SchoolAdmin (alignée
            // sur l'annulation unitaire de facture) ; le service reçoit le droit,
            // jamais le rôle — il ne fait aucun contrôle d'autorisation (§77).
            var canCancel = User.GetRole() == UserRoles.SchoolAdmin;
            var result = await _studentService.ExitStudentAsync(
                id, schoolId.Value, userId.Value, canCancel, dto, ct);

            if (!result.Ok) return BadRequest(ApiResponse<bool>.Fail(result.Error!));

            var updated = await _studentService.GetStudentByIdAsync(id, schoolId.Value);
            return Ok(ApiResponse<StudentResponseDto>.Ok(updated!,
                result.CancelledInvoices > 0
                    ? $"Sortie enregistrée. {result.CancelledInvoices} mensualité(s) impayée(s) annulée(s)."
                    : "Sortie enregistrée."));
        }

        /// <summary>
        /// Annule la sortie (prévue ou effective). SchoolAdmin SEUL : réintégrer
        /// remet l'élève dans l'effectif FACTURABLE — le cron le refacturera et
        /// il recompte dans le palier d'abonnement (§101).
        /// </summary>
        [HttpPost("{id}/reinstate")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<IActionResult> ReinstateStudent(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var result = await _studentService.ReinstateStudentAsync(id, schoolId.Value, ct);
            if (!result.Ok) return BadRequest(ApiResponse<bool>.Fail(result.Error!));

            var updated = await _studentService.GetStudentByIdAsync(id, schoolId.Value);
            return Ok(ApiResponse<StudentResponseDto>.Ok(updated!, "Élève réintégré dans l'effectif."));
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
            // !Student.IsDeleted (oubli préexistant corrigé le 2026-08-17) : un
            // lien vers un élève supprimé ne doit pas suffire à rattacher le
            // responsable. Un lien vers un élève SORTI, si — la fratrie reste.
            var guardianBelongsToSchool = await _context.StudentGuardians
                .AnyAsync(sg => sg.GuardianId == guardianId
                                && sg.Student.SchoolId == schoolId.Value
                                && !sg.Student.IsDeleted);
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
