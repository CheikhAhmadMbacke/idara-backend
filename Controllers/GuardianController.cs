using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Operations;
using Idara.API.DTOs.Payment;
using Idara.API.DTOs.Student;
using Idara.API.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Endpoints réservés au rôle Guardian. Le gardien n'a pas de SchoolId
    /// dans son token : la liste des écoles auxquelles il a accès est
    /// dérivée des liens StudentGuardian.
    /// </summary>
    [Authorize(Roles = UserRoles.Guardian)]
    [ApiController]
    [Route("api/guardian")]
    public class GuardianController : ControllerBase
    {
        private readonly AppDbContext _context;
        public GuardianController(AppDbContext context) => _context = context;

        [HttpGet("my-children")]
        public async Task<IActionResult> GetMyChildren()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var students = await _context.StudentGuardians
                .Include(sg => sg.Student).ThenInclude(s => s.Class)
                .Include(sg => sg.Student).ThenInclude(s => s.School)
                .Where(sg => sg.GuardianId == userId.Value && !sg.Student.IsDeleted)
                .Select(sg => new GuardianChildDto
                {
                    StudentId = sg.Student.Id,
                    FirstName = sg.Student.FirstName,
                    LastName = sg.Student.LastName,
                    PhotoUrl = sg.Student.PhotoUrl,
                    ClassName = sg.Student.Class != null ? sg.Student.Class.Name : null,
                    SchoolName = sg.Student.School.Name,
                    StudentNumber = sg.Student.StudentNumber,
                    IsPrimaryGuardian = sg.IsPrimaryGuardian
                })
                .ToListAsync();

            return Ok(students);
        }

        [HttpGet("students/{studentId}")]
        public async Task<IActionResult> GetChildDetails(int studentId)
        {
            if (!await IsLinked(studentId)) return Forbid();

            var s = await _context.Students
                .Include(x => x.Class)
                .Include(x => x.StudentGuardians).ThenInclude(g => g.Guardian)
                .FirstOrDefaultAsync(x => x.Id == studentId && !x.IsDeleted);
            if (s == null) return NotFound();

            return Ok(new
            {
                s.Id,
                s.FirstName, s.LastName, s.MiddleName,
                s.DateOfBirth, s.PlaceOfBirth, s.Nationality,
                s.PhotoUrl, s.StudentNumber,
                s.EnrollmentDate,
                ClassName = s.Class?.Name,
                Allergies = s.Allergies,
                ChronicConditions = s.ChronicConditions,
                BloodType = s.BloodType
            });
        }

        [HttpGet("students/{studentId}/grades")]
        public async Task<IActionResult> GetChildGrades(int studentId, [FromQuery] int? academicPeriodId)
        {
            if (!await IsLinked(studentId)) return Forbid();

            var query = _context.Grades
                .Include(g => g.Subject).Include(g => g.AcademicPeriod)
                .Where(g => g.StudentId == studentId);
            if (academicPeriodId.HasValue)
                query = query.Where(g => g.AcademicPeriodId == academicPeriodId.Value);

            var items = await query.OrderByDescending(g => g.Date).ToListAsync();
            return Ok(items.Select(g => new GradeDto
            {
                Id = g.Id,
                StudentId = g.StudentId,
                StudentName = string.Empty,
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
            }));
        }

        [HttpGet("students/{studentId}/attendance")]
        public async Task<IActionResult> GetChildAttendance(int studentId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (!await IsLinked(studentId)) return Forbid();

            var query = _context.Attendances.Where(a => a.StudentId == studentId && !a.IsDeleted);
            if (from.HasValue) query = query.Where(a => a.Date >= from.Value.ToUtcDay());
            if (to.HasValue) query = query.Where(a => a.Date <= to.Value.ToUtcDay());

            var items = await query.OrderByDescending(a => a.Date).ToListAsync();
            return Ok(items.Select(a => new AttendanceDto
            {
                Id = a.Id,
                StudentId = a.StudentId,
                StudentName = string.Empty,
                Date = a.Date,
                Status = a.Status,
                Reason = a.Reason
            }));
        }

        [HttpGet("students/{studentId}/coran")]
        public async Task<IActionResult> GetChildCoran(int studentId)
        {
            if (!await IsLinked(studentId)) return Forbid();

            var p = await _context.CoranProgresses.FirstOrDefaultAsync(x => x.StudentId == studentId);
            var sessions = await _context.CoranSessions
                .Where(s => s.StudentId == studentId)
                .OrderByDescending(s => s.Date)
                .Take(20)
                .ToListAsync();

            return Ok(new
            {
                Progress = p == null ? null : new CoranProgressDto
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
                },
                Sessions = sessions.Select(s => new CoranSessionDto
                {
                    Id = s.Id,
                    StudentId = s.StudentId,
                    StudentName = string.Empty,
                    Date = s.Date,
                    Kind = s.Kind,
                    FromSurah = s.FromSurah,
                    FromVerse = s.FromVerse,
                    ToSurah = s.ToSurah,
                    ToVerse = s.ToVerse,
                    RecitationScore = s.RecitationScore,
                    TajwidScore = s.TajwidScore,
                    MemorizationScore = s.MemorizationScore,
                    Comment = s.Comment
                })
            });
        }

        [HttpGet("students/{studentId}/journal")]
        public async Task<IActionResult> GetChildJournal(int studentId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (!await IsLinked(studentId)) return Forbid();

            var query = _context.DailyJournalEntries
                .Include(j => j.Teacher).Include(j => j.Subject)
                .Where(j => j.StudentId == studentId && !j.IsDeleted);
            if (from.HasValue) query = query.Where(j => j.Date >= from.Value.ToUtcDay());
            if (to.HasValue) query = query.Where(j => j.Date <= to.Value.ToUtcDay());

            var items = await query
                .OrderByDescending(j => j.Date).ThenByDescending(j => j.CreatedAt)
                .ToListAsync();
            return Ok(items.Select(j => new DailyJournalEntryDto
            {
                Id = j.Id,
                StudentId = j.StudentId,
                StudentName = string.Empty,
                TeacherId = j.TeacherId,
                TeacherName = j.Teacher?.FullName ?? j.Teacher?.Email ?? string.Empty,
                SubjectId = j.SubjectId,
                SubjectName = j.Subject?.Name,
                Date = j.Date,
                LearnedToday = j.LearnedToday,
                BehaviorScore = j.BehaviorScore,
                EffortScore = j.EffortScore,
                CreatedAt = j.CreatedAt,
                UpdatedAt = j.UpdatedAt
            }));
        }

        [HttpGet("students/{studentId}/report-cards")]
        public async Task<IActionResult> GetChildReportCards(int studentId)
        {
            if (!await IsLinked(studentId)) return Forbid();

            var cards = await _context.ReportCards
                .Include(r => r.AcademicPeriod).Include(r => r.Class)
                .Include(r => r.Lines).ThenInclude(l => l.Subject)
                .Where(r => r.StudentId == studentId)
                .OrderByDescending(r => r.GeneratedAt)
                .ToListAsync();
            return Ok(cards.Select(r => new ReportCardDto
            {
                Id = r.Id,
                StudentId = r.StudentId,
                StudentName = string.Empty,
                AcademicPeriodId = r.AcademicPeriodId,
                AcademicPeriodName = r.AcademicPeriod?.Name ?? string.Empty,
                ClassName = r.Class?.Name,
                GeneratedAt = r.GeneratedAt,
                GeneralAverage = r.GeneralAverage,
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
            }));
        }

        // ===== Phase 1.7 : Paiements parent =====

        /// <summary>
        /// Liste des Invoices d'un de ses enfants. Inclut les non-Cancelled
        /// par défaut. Tri : DueDate desc (les plus récentes/échues en haut).
        /// Filtre status optionnel.
        /// </summary>
        [HttpGet("students/{studentId}/invoices")]
        public async Task<ActionResult<IEnumerable<InvoiceDto>>> GetChildInvoices(
            int studentId,
            [FromQuery] InvoiceStatus? status,
            CancellationToken ct)
        {
            if (!await IsLinked(studentId)) return Forbid();

            var query = _context.Invoices
                .Include(i => i.Student).ThenInclude(s => s.Class)
                .Include(i => i.Student).ThenInclude(s => s.School)
                .Where(i => i.StudentId == studentId);

            if (status.HasValue)
                query = query.Where(i => i.Status == status.Value);
            else
                query = query.Where(i => i.Status != InvoiceStatus.Cancelled);

            var items = await query
                .OrderByDescending(i => i.DueDate)
                .Take(60)
                .ToListAsync(ct);

            return Ok(items.Select(i => new InvoiceDto
            {
                Id = i.Id,
                SchoolId = i.SchoolId,
                SchoolName = i.Student.School.Name,
                StudentId = i.StudentId,
                StudentFirstName = i.Student.FirstName,
                StudentLastName = i.Student.LastName,
                StudentNumber = i.Student.StudentNumber,
                ClassName = i.Student.Class?.Name,
                PeriodStart = i.PeriodStart,
                PeriodEnd = i.PeriodEnd,
                DueDate = i.DueDate,
                AmountDueFcfa = i.AmountDueFcfa,
                AmountPaidFcfa = i.AmountPaidFcfa,
                Status = i.Status,
                CreatedAt = i.CreatedAt,
                UpdatedAt = i.UpdatedAt
            }));
        }

        /// <summary>
        /// Historique paiements du Guardian (tous enfants confondus).
        /// </summary>
        [HttpGet("payments")]
        public async Task<ActionResult<IEnumerable<PaymentDto>>> GetMyPayments(
            [FromQuery] PaymentStatus? status,
            CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var query = _context.Payments
                .Include(p => p.Student)
                .Where(p => p.GuardianId == userId.Value);
            if (status.HasValue) query = query.Where(p => p.Status == status.Value);

            var items = await query
                .OrderByDescending(p => p.InitiatedAt)
                .Take(100)
                .ToListAsync(ct);

            return Ok(items.Select(p => new PaymentDto
            {
                Id = p.Id,
                SchoolId = p.SchoolId,
                StudentId = p.StudentId,
                StudentFirstName = p.Student?.FirstName,
                StudentLastName = p.Student?.LastName,
                StudentNumber = p.Student?.StudentNumber,
                GuardianId = p.GuardianId,
                InvoiceId = p.InvoiceId,
                AmountFcfa = p.AmountFcfa,
                FeesFcfa = p.FeesFcfa,
                NetCreditedFcfa = p.NetCreditedFcfa,
                Operator = p.Operator,
                FeesPayer = p.FeesPayer,
                Status = p.Status,
                SenePayTransactionId = p.SenePayTransactionId,
                FailureReason = p.FailureReason,
                InitiatedAt = p.InitiatedAt,
                PaidAt = p.PaidAt,
                FailedAt = p.FailedAt,
                ReceiptPdfUrl = p.ReceiptPdfPath
            }));
        }

        private async Task<bool> IsLinked(int studentId)
        {
            var userId = User.GetUserId();
            if (userId == null) return false;
            return await _context.StudentGuardians
                .AnyAsync(sg => sg.GuardianId == userId.Value && sg.StudentId == studentId);
        }
    }

    public class GuardianChildDto
    {
        public int StudentId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? PhotoUrl { get; set; }
        public string? ClassName { get; set; }
        public string? SchoolName { get; set; }
        public string? StudentNumber { get; set; }
        public bool IsPrimaryGuardian { get; set; }
    }
}
