using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Exports CSV (élèves, notes, présences) pour les admins / personnel.
    /// Restreint aux Guardian — un parent voit déjà ces infos via /guardian/...
    /// </summary>
    [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
    [ApiController]
    [Route("api/exports")]
    public class ExportsController : ControllerBase
    {
        private readonly AppDbContext _context;
        public ExportsController(AppDbContext context) => _context = context;

        // ----- Élèves -----

        [HttpGet("students.csv")]
        public async Task<IActionResult> ExportStudents([FromQuery] int? classId)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            // Enrolled() : l'export suit la liste (§116) — l'effectif, pas les sortis.
            var query = _context.Students
                .Include(s => s.Class)
                .Where(s => s.SchoolId == schoolId.Value)
                .Enrolled();

            if (classId.HasValue)
                query = query.Where(s => s.ClassId == classId.Value);

            var students = await query
                .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
                .ToListAsync();

            var csv = new CsvBuilder()
                .AppendRow("Matricule", "Nom", "Prénom", "Classe", "Date de naissance",
                           "Sexe", "Téléphone père", "Téléphone mère", "Adresse",
                           "Ville", "Date d'inscription");

            foreach (var s in students)
            {
                csv.AppendRow(
                    s.StudentNumber,
                    s.LastName,
                    s.FirstName,
                    s.Class?.Name,
                    s.DateOfBirth,
                    s.Gender?.ToString(),
                    s.FatherPhone,
                    s.MotherPhone,
                    s.Address,
                    s.City,
                    s.EnrollmentDate);
            }

            return BuildCsvFile(csv, $"eleves-{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        // ----- Notes -----

        [HttpGet("grades.csv")]
        public async Task<IActionResult> ExportGrades(
            [FromQuery] int? academicPeriodId,
            [FromQuery] int? classId,
            [FromQuery] int? subjectId)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var query = _context.Grades
                .Include(g => g.Student)
                .Include(g => g.Subject)
                .Include(g => g.AcademicPeriod)
                .Include(g => g.Class)
                .Where(g => g.SchoolId == schoolId.Value);

            if (academicPeriodId.HasValue) query = query.Where(g => g.AcademicPeriodId == academicPeriodId.Value);
            if (classId.HasValue) query = query.Where(g => g.ClassId == classId.Value);
            if (subjectId.HasValue) query = query.Where(g => g.SubjectId == subjectId.Value);

            var grades = await query
                .OrderBy(g => g.AcademicPeriod.Name)
                .ThenBy(g => g.Student.LastName)
                .ThenBy(g => g.Subject.Name)
                .ToListAsync();

            var csv = new CsvBuilder()
                .AppendRow("Période", "Matricule", "Élève", "Classe", "Matière",
                           "Évaluation", "Date", "Note", "Sur", "Coefficient",
                           "Note /20", "Commentaire");

            foreach (var g in grades)
            {
                var over20 = g.MaxValue == 0 ? 0 : g.Value / g.MaxValue * 20;
                csv.AppendRow(
                    g.AcademicPeriod?.Name,
                    g.Student?.StudentNumber,
                    g.Student != null ? $"{g.Student.LastName} {g.Student.FirstName}" : null,
                    g.Class?.Name,
                    g.Subject?.Name,
                    g.EvaluationType.ToString(),
                    g.Date,
                    g.Value,
                    g.MaxValue,
                    g.Coefficient,
                    Math.Round(over20, 2),
                    g.Comment);
            }

            return BuildCsvFile(csv, $"notes-{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        // ----- Présences -----

        [HttpGet("attendance.csv")]
        public async Task<IActionResult> ExportAttendance(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? classId)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var query = _context.Attendances
                .Include(a => a.Student).ThenInclude(s => s.Class)
                .Where(a => a.SchoolId == schoolId.Value && !a.IsDeleted);

            if (from.HasValue) query = query.Where(a => a.Date >= from.Value.ToUtcDay());
            if (to.HasValue) query = query.Where(a => a.Date <= to.Value.ToUtcDay());
            if (classId.HasValue) query = query.Where(a => a.Student.ClassId == classId.Value);

            var items = await query
                .OrderBy(a => a.Date)
                .ThenBy(a => a.Student.LastName)
                .ToListAsync();

            var csv = new CsvBuilder()
                .AppendRow("Date", "Matricule", "Élève", "Classe", "Statut", "Motif");

            foreach (var a in items)
            {
                csv.AppendRow(
                    a.Date,
                    a.Student?.StudentNumber,
                    a.Student != null ? $"{a.Student.LastName} {a.Student.FirstName}" : null,
                    a.Student?.Class?.Name,
                    a.Status.ToString(),
                    a.Reason);
            }

            return BuildCsvFile(csv, $"presences-{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        // ----- Helper -----

        private FileContentResult BuildCsvFile(CsvBuilder csv, string fileName)
        {
            return File(csv.ToBytes(), "text/csv; charset=utf-8", fileName);
        }
    }
}
