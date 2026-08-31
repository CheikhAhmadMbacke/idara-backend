using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Operations;
using Idara.API.Models;
using Idara.API.Services;
using Idara.API.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/report-cards")]
    public class ReportCardsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IReportCardPdfService _pdfService;
        private readonly IWebHostEnvironment _env;
        private readonly INotificationService _notif;
        private readonly ILogger<ReportCardsController> _logger;

        public ReportCardsController(
            AppDbContext context,
            IReportCardPdfService pdfService,
            IWebHostEnvironment env,
            INotificationService notif,
            ILogger<ReportCardsController> logger)
        {
            _context = context;
            _pdfService = pdfService;
            _env = env;
            _notif = notif;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Get(
            [FromQuery] int? studentId,
            [FromQuery] int? academicPeriodId,
            [FromQuery] int? classId)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // Périmètre de l'appelant : un enseignant ne voit que les bulletins
            // des élèves de ses classes (§150).
            var visible = await _context.VisibleClassIdsAsync(
                User.GetRole(), userId.Value, schoolId.Value);

            var query = _context.ReportCards
                .Include(r => r.Student).Include(r => r.AcademicPeriod).Include(r => r.Class)
                .Include(r => r.Lines).ThenInclude(l => l.Subject)
                .Where(r => r.SchoolId == schoolId.Value)
                .Where(r => visible == null
                    || (r.Student.ClassId != null && visible.Contains(r.Student.ClassId.Value)));

            if (studentId.HasValue) query = query.Where(r => r.StudentId == studentId.Value);
            if (academicPeriodId.HasValue) query = query.Where(r => r.AcademicPeriodId == academicPeriodId.Value);
            if (classId.HasValue) query = query.Where(r => r.ClassId == classId.Value);

            var items = await query.OrderByDescending(r => r.GeneratedAt).ToListAsync();
            return Ok(items.Select(Map));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var rc = await _context.ReportCards
                .Include(r => r.Student).Include(r => r.AcademicPeriod).Include(r => r.Class)
                .Include(r => r.Lines).ThenInclude(l => l.Subject)
                .FirstOrDefaultAsync(r => r.Id == id && r.SchoolId == schoolId.Value);
            if (rc == null) return NotFound();

            if (!await _context.CanAccessStudentAsync(
                    User.GetRole(), userId.Value, schoolId.Value, rc.StudentId))
                return NotFound();

            return Ok(Map(rc));
        }

        /// <summary>
        /// Génère (ou régénère) le bulletin d'un élève pour une période donnée
        /// à partir des notes existantes.
        /// </summary>
        [HttpPost("generate")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> Generate([FromBody] GenerateReportCardDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // Un enseignant ne génère de bulletin que pour ses classes (§150).
            if (!await _context.CanAccessStudentAsync(
                    User.GetRole(), userId.Value, schoolId.Value, dto.StudentId))
                return NotFound(ApiResponse<bool>.Fail("Élève introuvable."));

            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Id == dto.StudentId && s.SchoolId == schoolId.Value && !s.IsDeleted);
            if (student == null) return NotFound(ApiResponse<bool>.Fail("Élève introuvable."));

            var period = await _context.AcademicPeriods
                .FirstOrDefaultAsync(p => p.Id == dto.AcademicPeriodId && p.SchoolId == schoolId.Value);
            if (period == null) return NotFound(ApiResponse<bool>.Fail("Période introuvable."));

            // ----- 1) Charger TOUTES les notes de la classe en UNE seule requête -----
            //    (élimine le N+1 de l'ancienne implémentation).
            List<int> classmateIds;
            if (student.ClassId.HasValue)
            {
                // EnrolledDuring(début de période) : un élève parti AVANT la
                // période ne gonfle plus le rang ni le « /N élèves » ; celui qui
                // l'a suivie — dont l'élève sorti pour qui on génère ce bulletin
                // final (D4) — y reste classé. SchoolId ajouté au passage
                // (défense en profondeur : la classe vient de l'élève contrôlé).
                classmateIds = await _context.Students
                    .Where(s => s.ClassId == student.ClassId && s.SchoolId == schoolId.Value)
                    .EnrolledDuring(period.StartDate)
                    .Select(s => s.Id)
                    .ToListAsync();
                // L'élève cible appartient toujours à son propre classement,
                // même si sa sortie est antérieure au début de la période (cas
                // limite : bulletin de rattrapage) — sans lui, son rang n'existe pas.
                if (!classmateIds.Contains(student.Id)) classmateIds.Add(student.Id);
            }
            else
            {
                classmateIds = new List<int> { student.Id };
            }

            var allGrades = await _context.Grades
                .Include(g => g.Subject)
                .Where(g => g.SchoolId == schoolId.Value
                            && g.AcademicPeriodId == dto.AcademicPeriodId
                            && classmateIds.Contains(g.StudentId)
                            && !g.IsDeleted)
                .ToListAsync();

            // Moyenne d'un élève pour une matière (pondérée par coef de la note, ramenée /20).
            static double AvgForSubject(IEnumerable<Grade> grades)
            {
                var list = grades.ToList();
                if (list.Count == 0) return 0;
                var weighted = list.Sum(x => (x.Value / x.MaxValue) * 20 * x.Coefficient);
                var totalCoef = list.Sum(x => x.Coefficient);
                return totalCoef > 0 ? weighted / totalCoef : 0;
            }

            // Précalcul pour chaque (élève, matière) → moyenne /20.
            var perStudentSubject = allGrades
                .GroupBy(g => new { g.StudentId, g.SubjectId })
                .ToDictionary(
                    g => (g.Key.StudentId, g.Key.SubjectId),
                    g => AvgForSubject(g));

            // Précalcul du défaut coefficient par matière.
            var subjectMeta = allGrades
                .GroupBy(g => g.SubjectId)
                .ToDictionary(
                    g => g.Key,
                    g => new { g.First().Subject.Name, g.First().Subject.DefaultCoefficient, g.First().Subject.Kind });

            // ----- 2) Lignes du bulletin pour CET élève + rang par matière -----
            var ourGrades = allGrades.Where(g => g.StudentId == student.Id).ToList();
            var ourSubjects = ourGrades.Select(g => g.SubjectId).Distinct();

            var bySubject = new List<ReportCardLine>();
            foreach (var subId in ourSubjects)
            {
                var avg = perStudentSubject.TryGetValue((student.Id, subId), out var a) ? a : 0;
                avg = Math.Round(avg, 2);
                var meta = subjectMeta[subId];

                // Rang dans la classe pour cette matière.
                int? subjectRank = null;
                if (student.ClassId.HasValue && classmateIds.Count > 1)
                {
                    var subjectScores = classmateIds
                        .Select(sid => new
                        {
                            Sid = sid,
                            Avg = perStudentSubject.TryGetValue((sid, subId), out var v) ? v : (double?)null
                        })
                        .Where(x => x.Avg.HasValue)
                        .OrderByDescending(x => x.Avg!.Value)
                        .ToList();
                    var idx = subjectScores.FindIndex(x => x.Sid == student.Id);
                    if (idx >= 0) subjectRank = idx + 1;
                }

                bySubject.Add(new ReportCardLine
                {
                    SubjectId = subId,
                    SubjectName = meta.Name,
                    Average = avg,
                    Coefficient = meta.DefaultCoefficient,
                    SubjectKind = meta.Kind,
                    MaxValue = 20.0,
                    RankInClass = subjectRank,
                    Appreciation = ComputeAppreciation(avg)
                });
            }

            // ----- 3) Moyenne générale (pondérée par coefficient matière) -----
            var generalNum = bySubject.Sum(l => l.Average * l.Coefficient);
            var generalDen = bySubject.Sum(l => l.Coefficient);
            var general = generalDen > 0 ? Math.Round(generalNum / generalDen, 2) : 0;

            // Moyennes par DOMAINE (franco-arabe : « arabe et religieux » vs
            // « français et général »). Règle pure et testable, partagée avec
            // l'affichage et le PDF pour qu'ils ne divergent pas.
            var domainLines = bySubject.Select(l => (l.Average, l.Coefficient, l.SubjectKind));
            var arabicAvg = ReportCardDomains.Average(domainLines, arabicDomain: true);
            var generalSubjAvg = ReportCardDomains.Average(domainLines, arabicDomain: false);

            // ----- 4) Rang général dans la classe (recalculé sur les mêmes données) -----
            int? rank = null;
            int? totalInClass = null;
            if (student.ClassId.HasValue && classmateIds.Count > 0)
            {
                totalInClass = classmateIds.Count;

                var classmateAverages = classmateIds
                    .Select(sid =>
                    {
                        var grades = allGrades.Where(g => g.StudentId == sid).ToList();
                        var bySub = grades
                            .GroupBy(x => x.SubjectId)
                            .Select(grp => new
                            {
                                Avg = AvgForSubject(grp),
                                Coef = subjectMeta.TryGetValue(grp.Key, out var m) ? m.DefaultCoefficient : 1.0
                            })
                            .ToList();
                        var num = bySub.Sum(b => b.Avg * b.Coef);
                        var den = bySub.Sum(b => b.Coef);
                        return new { Sid = sid, Avg = den > 0 ? num / den : 0.0 };
                    })
                    .OrderByDescending(x => x.Avg)
                    .ToList();

                var idx = classmateAverages.FindIndex(x => x.Sid == student.Id);
                if (idx >= 0) rank = idx + 1;
            }

            // ----- 5) Upsert atomique du bulletin -----
            using var tx = await _context.Database.BeginTransactionAsync();

            var existing = await _context.ReportCards
                .Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.StudentId == dto.StudentId
                                       && r.AcademicPeriodId == dto.AcademicPeriodId);
            if (existing != null)
            {
                _context.ReportCardLines.RemoveRange(existing.Lines);
                _context.ReportCards.Remove(existing);
                await _context.SaveChangesAsync();
            }

            var card = new ReportCard
            {
                SchoolId = schoolId.Value,
                StudentId = dto.StudentId,
                AcademicPeriodId = dto.AcademicPeriodId,
                ClassId = student.ClassId,
                GeneratedAt = DateTime.UtcNow,
                GeneralAverage = general,
                ArabicAverage = arabicAvg,
                GeneralSubjectsAverage = generalSubjAvg,
                Rank = rank,
                TotalStudents = totalInClass,
                Mention = ComputeMention(general),
                Appreciation = ComputeAppreciation(general),
                Lines = bySubject
            };
            _context.ReportCards.Add(card);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            var saved = await _context.ReportCards
                .Include(r => r.Student).Include(r => r.AcademicPeriod).Include(r => r.Class)
                .Include(r => r.Lines).ThenInclude(l => l.Subject)
                .FirstAsync(r => r.Id == card.Id);

            // Génération PDF asynchrone — best-effort. Si elle échoue, on loggue
            // mais on retourne quand même le bulletin (le PDF pourra être
            // regénéré à la demande via /pdf).
            try
            {
                var school = await _context.Schools.FindAsync(schoolId.Value);
                if (school != null)
                {
                    var pdfPath = await _pdfService.GenerateAsync(saved, school);
                    saved.FilePath = pdfPath;
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Génération PDF échouée pour bulletin {Id}", saved.Id);
            }

            // Notif parents : bulletin disponible (push, best-effort, 1/élève/jour).
            var eleve = saved.Student != null
                ? $"{saved.Student.FirstName} {saved.Student.LastName}".Trim()
                : "votre enfant";
            await _notif.NotifyGuardiansOfStudentAsync(
                saved.StudentId, NotificationTemplates.ChildReportCardReady(eleve),
                "CHILD_REPORTCARD", $"/guardian/children/{saved.StudentId}", oncePerDay: true);

            return Ok(Map(saved));
        }

        /// <summary>
        /// Télécharge le PDF du bulletin. Si le fichier n'existe pas (suppression
        /// disque, première migration…) il est régénéré à la volée.
        /// </summary>
        [HttpGet("{id}/pdf")]
        public async Task<IActionResult> DownloadPdf(int id)
        {
            var schoolId = User.GetSchoolId();
            var role = User.GetRole();
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var card = await _context.ReportCards
                .Include(r => r.Student).Include(r => r.AcademicPeriod).Include(r => r.Class)
                .Include(r => r.Lines).ThenInclude(l => l.Subject)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (card == null) return NotFound();

            // Sécurité multi-tenant + Guardian.
            if (role == UserRoles.Guardian)
            {
                var isLinked = await _context.StudentGuardians
                    .AnyAsync(sg => sg.StudentId == card.StudentId && sg.GuardianId == userId.Value
                                    && !sg.Student.IsDeleted);
                if (!isLinked) return Forbid();
            }
            else
            {
                if (schoolId == null || card.SchoolId != schoolId.Value) return Forbid();

                // Périmètre pédagogique : sans ce contrôle, le PDF complet d'un
                // bulletin (notes, rang, appréciations) se téléchargerait en
                // devinant un identifiant, alors que la liste est filtrée (§150).
                if (!await _context.CanAccessStudentAsync(
                        role, userId.Value, schoolId.Value, card.StudentId))
                    return Forbid();
            }

            // Régénère si manquant.
            string? relativePath = card.FilePath;
            var fullPath = relativePath != null
                ? Path.GetFullPath(Path.Combine(_env.WebRootPath, relativePath.TrimStart('/')))
                : null;

            // Defense en profondeur contre un eventuel path traversal :
            // refuse tout chemin qui sortirait du wwwroot, meme si _pdfService
            // genere normalement un chemin sain.
            var webRootFull = Path.GetFullPath(_env.WebRootPath);
            if (fullPath != null && !fullPath.StartsWith(webRootFull, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Tentative de lecture hors wwwroot bloquee : carte {Id}, chemin {Path}",
                    card.Id, fullPath);
                return NotFound();
            }

            if (fullPath == null || !System.IO.File.Exists(fullPath))
            {
                var school = await _context.Schools.FindAsync(card.SchoolId);
                if (school == null) return NotFound();
                relativePath = await _pdfService.GenerateAsync(card, school);
                card.FilePath = relativePath;
                await _context.SaveChangesAsync();
                fullPath = Path.GetFullPath(Path.Combine(_env.WebRootPath, relativePath.TrimStart('/')));
                if (!fullPath.StartsWith(webRootFull, StringComparison.Ordinal))
                {
                    _logger.LogError(
                        "PDF genere hors wwwroot : carte {Id}, chemin {Path}",
                        card.Id, fullPath);
                    return StatusCode(500);
                }
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
            var fileName =
                $"bulletin-{card.Student?.LastName}-{card.AcademicPeriod?.Name}.pdf"
                    .Replace(' ', '_');
            return File(bytes, "application/pdf", fileName);
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Delete(int id)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var rc = await _context.ReportCards
                .Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == id && r.SchoolId == schoolId.Value);
            if (rc == null) return NotFound();

            _context.ReportCardLines.RemoveRange(rc.Lines);
            _context.ReportCards.Remove(rc);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private static string ComputeMention(double avg) => avg switch
        {
            >= 16 => "Très bien",
            >= 14 => "Bien",
            >= 12 => "Assez bien",
            >= 10 => "Passable",
            _ => "Insuffisant"
        };

        private static string ComputeAppreciation(double avg) => avg switch
        {
            >= 16 => "Excellent travail, continuez ainsi.",
            >= 14 => "Bons résultats, peut encore progresser.",
            >= 12 => "Travail honorable mais à approfondir.",
            >= 10 => "Résultats juste suffisants, plus d'efforts attendus.",
            _ => "Résultats insuffisants. Une remise à niveau est nécessaire."
        };

        private static ReportCardDto Map(ReportCard r) => new()
        {
            Id = r.Id,
            StudentId = r.StudentId,
            StudentName = r.Student != null ? $"{r.Student.FirstName} {r.Student.LastName}" : string.Empty,
            AcademicPeriodId = r.AcademicPeriodId,
            AcademicPeriodName = r.AcademicPeriod?.Name ?? string.Empty,
            ClassName = r.Class?.Name,
            GeneratedAt = r.GeneratedAt,
            GeneralAverage = r.GeneralAverage,
            ArabicAverage = r.ArabicAverage,
            GeneralSubjectsAverage = r.GeneralSubjectsAverage,
            Rank = r.Rank,
            TotalStudents = r.TotalStudents,
            Mention = r.Mention,
            Appreciation = r.Appreciation,
            FilePath = r.FilePath,
            Lines = r.Lines.Select(l => new ReportCardLineDto
            {
                Id = l.Id,
                SubjectId = l.SubjectId,
                SubjectName = string.IsNullOrEmpty(l.SubjectName) ? l.Subject?.Name ?? string.Empty : l.SubjectName,
                Average = l.Average,
                Coefficient = l.Coefficient,
                MaxValue = l.MaxValue,
                RankInClass = l.RankInClass,
                Appreciation = l.Appreciation
            }).ToList()
        };
    }

    public class GenerateReportCardDto
    {
        [System.ComponentModel.DataAnnotations.Required]
        public int StudentId { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int AcademicPeriodId { get; set; }
    }
}
