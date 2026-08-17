using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Export;
using Idara.API.DTOs.Operations;
using Idara.API.DTOs.Payment;
using Idara.API.DTOs.Student;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services;
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
        private readonly IExportPdfService _exportPdf;

        public GuardianController(AppDbContext context, IExportPdfService exportPdf)
        {
            _context = context;
            _exportPdf = exportPdf;
        }

        [HttpGet("my-children")]
        public async Task<IActionResult> GetMyChildren()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            // 1) Liste des enfants liés au guardian. Les enfants SORTIS restent
            // inclus (D2) : le parent garde bulletins et historique, et sa dette
            // reste payable — l'app les regroupe sous « N'est plus inscrit ».
            var todayForExit = DateTime.UtcNow.Date;
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
                    SchoolNameAr = sg.Student.School.NameAr,
                    SchoolId = sg.Student.SchoolId,
                    StudentNumber = sg.Student.StudentNumber,
                    IsPrimaryGuardian = sg.IsPrimaryGuardian,
                    ExitDate = sg.Student.ExitDate,
                    // Calculé côté serveur (§151) — une sortie PROGRAMMÉE n'est
                    // pas « sorti » : l'enfant est encore en classe.
                    IsExited = sg.Student.ExitDate != null && sg.Student.ExitDate <= todayForExit,
                })
                .ToListAsync();

            // 2) Hydrate avec le BillingMode de l'école (utile au front pour
            //    décider si on affiche le bouton "paiement libre" — sans ça
            //    le parent ne sait pas si l'école est en FixedAmount).
            //    Une requête en lot, mappée par schoolId.
            var schoolIds = students.Select(s => s.SchoolId).Distinct().ToList();
            if (schoolIds.Count > 0)
            {
                var settings = await _context.SchoolPaymentSettings
                    .Where(sps => schoolIds.Contains(sps.SchoolId))
                    .ToDictionaryAsync(
                        sps => sps.SchoolId,
                        sps => new { sps.BillingMode, sps.FeesPayer });
                foreach (var s in students)
                {
                    if (settings.TryGetValue(s.SchoolId, out var cfg))
                    {
                        s.SchoolBillingMode = cfg.BillingMode;
                        s.SchoolFeesPayer = cfg.FeesPayer;
                    }
                }
            }

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

        /// <summary>
        /// Récapitulatif du suivi quotidien Coran pour le parent : UNIQUEMENT les
        /// cycles de 22 jours TERMINÉS (le parent ne voit pas le suivi en cours
        /// ni les évaluations qualité — décision produit). Du plus récent au plus
        /// ancien.
        /// </summary>
        [HttpGet("students/{studentId}/coran-cycles")]
        public async Task<IActionResult> GetChildCoranCycles(int studentId)
        {
            if (!await IsLinked(studentId)) return Forbid();

            var cycles = await _context.CoranCycles
                .Where(c => c.StudentId == studentId && c.IsComplete)
                .OrderByDescending(c => c.Number)
                .Select(c => new CoranCycleDto
                {
                    Id = c.Id,
                    StudentId = c.StudentId,
                    Number = c.Number,
                    StartDate = c.StartDate,
                    CompletedDate = c.CompletedDate,
                    IsComplete = c.IsComplete,
                    DayCount = c.Records.Count
                })
                .ToListAsync();
            return Ok(cycles);
        }

        /// <summary>Détail d'un cycle TERMINÉ (les 22 jours) pour le parent.</summary>
        [HttpGet("students/{studentId}/coran-cycles/{cycleId}")]
        public async Task<IActionResult> GetChildCoranCycleDetail(int studentId, int cycleId)
        {
            if (!await IsLinked(studentId)) return Forbid();

            var cycle = await _context.CoranCycles
                .FirstOrDefaultAsync(c => c.Id == cycleId && c.StudentId == studentId && c.IsComplete);
            if (cycle == null) return NotFound();

            var records = await _context.CoranDailyRecords
                .Include(r => r.Portions)
                .Where(r => r.CycleId == cycleId)
                .OrderBy(r => r.Date)
                .ToListAsync();

            var dto = new CoranCycleDetailDto
            {
                Cycle = new CoranCycleDto
                {
                    Id = cycle.Id,
                    StudentId = cycle.StudentId,
                    Number = cycle.Number,
                    StartDate = cycle.StartDate,
                    CompletedDate = cycle.CompletedDate,
                    IsComplete = cycle.IsComplete,
                    DayCount = records.Count
                },
                Records = records.Select((r, i) => new CoranDailyRecordDto
                {
                    Id = r.Id,
                    StudentId = r.StudentId,
                    CycleId = r.CycleId,
                    CycleNumber = cycle.Number,
                    DayIndex = i + 1,
                    Date = r.Date,
                    Remarks = r.Remarks,
                    Portions = r.Portions.OrderBy(p => p.Kind).Select(p => new CoranDailyPortionDto
                    {
                        Kind = p.Kind,
                        FromSurah = p.FromSurah,
                        FromAyah = p.FromAyah,
                        FromWordIndex = p.FromWordIndex,
                        FromWordText = p.FromWordText,
                        ToSurah = p.ToSurah,
                        ToAyah = p.ToAyah,
                        ToWordIndex = p.ToWordIndex,
                        ToWordText = p.ToWordText,
                        Status = p.Status
                    }).ToList()
                }).ToList()
            };
            return Ok(dto);
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

        /// <summary>
        /// Emploi du temps de la classe de l'enfant (lecture seule). Vide si
        /// l'élève n'est rattaché à aucune classe.
        /// </summary>
        [HttpGet("students/{studentId}/timetable")]
        public async Task<IActionResult> GetChildTimetable(int studentId)
        {
            if (!await IsLinked(studentId)) return Forbid();

            var student = await _context.Students
                .Where(s => s.Id == studentId)
                .Select(s => new { s.ClassId, s.SchoolId })
                .FirstOrDefaultAsync();
            if (student?.ClassId == null) return Ok(Array.Empty<TimetableSlotDto>());

            var items = await _context.TimetableSlots
                .Include(t => t.Class).Include(t => t.Subject).Include(t => t.Teacher)
                .Where(t => t.ClassId == student.ClassId.Value && t.SchoolId == student.SchoolId)
                .OrderBy(t => t.DayOfWeek).ThenBy(t => t.StartTime)
                .ToListAsync();

            return Ok(items.Select(t => new TimetableSlotDto
            {
                Id = t.Id,
                ClassId = t.ClassId,
                ClassName = t.Class?.Name ?? string.Empty,
                SubjectId = t.SubjectId,
                SubjectName = t.Subject?.Name ?? string.Empty,
                TeacherId = t.TeacherId,
                TeacherName = t.Teacher?.FullName ?? t.Teacher?.Email,
                DayOfWeek = t.DayOfWeek,
                StartTime = t.StartTime,
                EndTime = t.EndTime,
                Room = t.Room
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
        /// Liste des Invoices d'un de ses enfants. Comportement par défaut :
        /// ne retourne QUE les invoices encore à payer (Pending + Overdue) —
        /// les Paid et Cancelled sont exclues pour ne pas polluer l'onglet
        /// "À payer" côté parent. Le client peut demander explicitement les
        /// Paid (ou Cancelled) via le query `?status=Paid` s'il veut un
        /// historique.
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
            {
                query = query.Where(i => i.Status == status.Value);
            }
            else
            {
                // Defaut "A payer" : Pending + Overdue, MAIS on cache aussi les
                // invoices essentiellement payees (remaining < 200) — c'est le
                // minimum SenePay, donc impayable par les voies normales. Sinon
                // l'invoice apparait en zombie avec "Montant a payer 0 FCFA" et
                // bloque le UI. Cas typique : facture legacy crediree avec
                // netAmount (196) au lieu de targetAmount (200) avant gotcha §59.
                const long senepayMinAmountFcfa = 200;
                query = query.Where(i =>
                    (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Overdue)
                    && (i.AmountDueFcfa - i.AmountPaidFcfa) >= senepayMinAmountFcfa);
            }

            var items = await query
                .OrderByDescending(i => i.DueDate)
                .Take(60)
                .ToListAsync(ct);

            return Ok(items.Select(i => new InvoiceDto
            {
                Id = i.Id,
                SchoolId = i.SchoolId,
                SchoolName = i.Student.School.Name,
                SchoolNameAr = i.Student.School.NameAr,
                SchoolLogoUrl = i.Student.School.LogoUrl,
                StudentId = i.StudentId,
                StudentFirstName = i.Student.FirstName,
                StudentLastName = i.Student.LastName,
                StudentNumber = i.Student.StudentNumber,
                ClassName = i.Student.Class?.Name,
                Type = i.Type,
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
            [FromQuery] string? q,
            [FromQuery] PaymentPurpose? purpose,
            CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            // Recherche = sur tout l'historique, pas seulement les 100 derniers.
            var take = string.IsNullOrWhiteSpace(q) ? 100 : 400;
            var items = await LoadMyPaymentsAsync(
                userId.Value, status, null, null, take, ct, q, purpose);

            // Nom + logo du daara par ligne (le parent peut avoir des paiements
            // dans plusieurs daaras) — un seul lookup en lot, mappé par schoolId.
            var paySchoolIds = items.Select(p => p.SchoolId).Distinct().ToList();
            var paySchools = await _context.Schools
                .Where(s => paySchoolIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name, s.NameAr, s.LogoUrl })
                .ToDictionaryAsync(s => s.Id, ct);

            return Ok(items.Select(p => new PaymentDto
            {
                Id = p.Id,
                Reference = IdaraReference.Payment(p.Id),
                SchoolId = p.SchoolId,
                SchoolName = paySchools.TryGetValue(p.SchoolId, out var ps) ? ps.Name : null,
                SchoolNameAr = paySchools.TryGetValue(p.SchoolId, out var psa) ? psa.NameAr : null,
                SchoolLogoUrl = paySchools.TryGetValue(p.SchoolId, out var pl) ? pl.LogoUrl : null,
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

        /// <summary>
        /// Charge les paiements d'un parent avec le filtrage EXACT de la vue
        /// « Historique » : échecs masqués par défaut, tentatives multiples d'une
        /// même facture regroupées. SOURCE UNIQUE de l'onglet et de son export PDF.
        /// </summary>
        private async Task<List<Payment>> LoadMyPaymentsAsync(
            int guardianId, PaymentStatus? status, DateTime? from, DateTime? to,
            int take, CancellationToken ct,
            string? search = null, PaymentPurpose? purpose = null)
        {
            var query = _context.Payments
                .Include(p => p.Student)
                .Where(p => p.GuardianId == guardianId);

            if (purpose.HasValue)
                query = query.Where(p => p.Purpose == purpose.Value);

            // Recherche : enfant, daara, référence SenePay. Le nom du daara vit
            // sur School, d'où la sous-requête (le parent peut avoir des enfants
            // dans plusieurs daaras).
            if (TransactionSearch.Pattern(search) is string pattern)
            {
                query = query.Where(p =>
                    (p.Student != null && (EF.Functions.ILike(p.Student!.FirstName, pattern)
                                        || EF.Functions.ILike(p.Student!.LastName, pattern)))
                    || (p.SenePayTransactionId != null
                        && EF.Functions.ILike(p.SenePayTransactionId, pattern))
                    || _context.Schools.Any(sc => sc.Id == p.SchoolId
                        && EF.Functions.ILike(sc.Name!, pattern)));
            }

            if (status.HasValue)
                query = query.Where(p => p.Status == status.Value);
            else
                // Par défaut : masquer les paiements échoués / annulés / expirés
                // (un essai Wave raté puis réessayé affiche un « ÉCHEC » anxiogène
                // au parent) — uniquement réussis + en cours.
                query = query.Where(p =>
                    p.Status == PaymentStatus.Completed || p.Status == PaymentStatus.Pending);

            // Bornes en jour civil : `to` inclut toute sa journée.
            if (from.HasValue) query = query.Where(p => p.InitiatedAt >= from.Value.ToUtcDay());
            if (to.HasValue) query = query.Where(p => p.InitiatedAt < to.Value.ToUtcDay().AddDays(1));

            var items = await query
                .OrderByDescending(p => p.InitiatedAt)
                .Take(take)
                .ToListAsync(ct);

            // Vue par défaut : on REGROUPE les tentatives multiples pour une même
            // facture (un parent qui réessaie crée plusieurs Payment) → on n'affiche
            // qu'UNE ligne par facture : le paiement Complété s'il existe, sinon la
            // tentative la plus récente (« en cours »). Évite la duplication des
            // badges « en cours » signalée côté parent. Les paiements sans facture
            // (montant libre / topup) gardent une clé unique → jamais regroupés.
            // Si un statut explicite est demandé, on ne regroupe pas (liste brute).
            if (!status.HasValue)
            {
                items = items
                    .GroupBy(p => p.InvoiceId ?? -p.Id)
                    .Select(g => g
                        .OrderByDescending(p => p.Status == PaymentStatus.Completed)
                        .ThenByDescending(p => p.InitiatedAt)
                        .First())
                    .OrderByDescending(p => p.InitiatedAt)
                    .ToList();
            }

            return items;
        }

        /// <summary>
        /// `GET /api/guardian/payments/pdf?from&to` — l'historique de paiements du
        /// parent en PDF (tableau), à conserver ou montrer à l'école. Reprend le
        /// même filtrage que l'onglet « Historique » (échecs masqués, réessais
        /// regroupés), borné à la période choisie.
        /// </summary>
        [HttpGet("payments/pdf")]
        public async Task<IActionResult> ExportMyPaymentsPdf(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to,
            [FromQuery] string? q,
            [FromQuery] PaymentStatus? status,
            [FromQuery] PaymentPurpose? purpose,
            CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var items = await LoadMyPaymentsAsync(
                userId.Value, status, from, to, FinanceLabels.MaxExportRows, ct, q, purpose);

            // Nom du/des enfant(s) pour les paiements GLOBAUX (StudentId null,
            // factures portées par les allocations) : c'est devenu le mode de
            // paiement par défaut, sans ça ces lignes n'auraient aucun libellé.
            var consolidatedIds = items.Where(p => p.StudentId == null).Select(p => p.Id).ToList();
            var childrenByPayment = new Dictionary<int, string>();
            if (consolidatedIds.Count > 0)
            {
                var allocs = await _context.PaymentInvoiceAllocations
                    .Where(a => consolidatedIds.Contains(a.PaymentId))
                    .Select(a => new
                    {
                        a.PaymentId,
                        Name = a.Invoice.Student.FirstName + " " + a.Invoice.Student.LastName
                    })
                    .ToListAsync(ct);
                childrenByPayment = allocs
                    .GroupBy(a => a.PaymentId)
                    .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name.Trim()).Distinct()));
            }

            var schoolIds = items.Select(p => p.SchoolId).Distinct().ToList();
            var schoolNames = await _context.Schools
                .Where(s => schoolIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

            var rows = items.Select(p =>
            {
                var child = p.Student != null
                    ? $"{p.Student.FirstName} {p.Student.LastName}".Trim()
                    : childrenByPayment.TryGetValue(p.Id, out var kids) ? kids : null;
                var school = schoolNames.TryGetValue(p.SchoolId, out var sn) ? sn : null;
                var subtitle = string.Join(" - ",
                    new[] { child, school }.Where(x => !string.IsNullOrWhiteSpace(x)));

                return new TransactionPdfRow
                {
                    Date = p.PaidAt ?? p.InitiatedAt,
                    Title = FinanceLabels.PaymentPurpose(p.Purpose),
                    Subtitle = string.IsNullOrWhiteSpace(subtitle) ? null : subtitle,
                    Method = FinanceLabels.Operator(p.Operator),
                    Reference = p.SenePayTransactionId,
                    Status = FinanceLabels.PaymentStatus(p.Status),
                    // Côté parent : ce qui a été RÉELLEMENT débité (frais inclus).
                    AmountFcfa = p.AmountFcfa
                };
            }).ToList();

            var paid = items.Where(p => p.Status == PaymentStatus.Completed).Sum(p => p.AmountFcfa);
            var summary = new List<(string, string, bool)>
            {
                ("Total paye", $"{paid:N0}", false),
                ("Paiements", $"{rows.Count:N0}", false)
            };

            var myName = await _context.Users
                .Where(u => u.Id == userId.Value).Select(u => u.FullName).FirstOrDefaultAsync(ct)
                ?? "Parent";

            var bytes = _exportPdf.BuildTransactionsPdf(
                myName,
                FinanceLabels.ExportTitle("Historique de mes paiements", rows.Count),
                from, to, rows, summary,
                // Vu du parent : pour quel enfant, dans quel daara.
                counterpartyHeader: "Élève / Daara");

            return File(bytes, "application/pdf", "mes-paiements-idara.pdf");
        }

        /// <summary>
        /// « Paiement global » : total des mensualités impayées des enfants du
        /// parent, REGROUPÉ PAR ÉCOLE (un paiement crédite un seul wallet). Seules
        /// les écoles en mode FixedAmount ont des factures — les FreeAmount sont
        /// donc naturellement absentes. Sert à afficher une carte « Tout payer »
        /// par école côté parent.
        /// </summary>
        [HttpGet("outstanding")]
        public async Task<ActionResult<IEnumerable<GuardianOutstandingSchoolDto>>> GetOutstanding(
            CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var invoices = await _context.Invoices
                .Include(i => i.Student).ThenInclude(s => s.School)
                .Include(i => i.Student).ThenInclude(s => s.Class)
                .Where(i => (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Overdue)
                            && i.AmountDueFcfa - i.AmountPaidFcfa > 0
                            && _context.StudentGuardians.Any(sg =>
                                sg.StudentId == i.StudentId
                                && sg.GuardianId == userId.Value
                                && !sg.Student.IsDeleted))
                .ToListAsync(ct);

            // Qui porte les frais par école + majoration courante (dynamique) :
            // le montant réellement débité = cible × multiplier si FeesPayer=Parent,
            // sinon la cible (école absorbe les frais). Calculé SERVEUR → l'écran
            // de confirmation parent affiche le bon total sans calcul côté client.
            var schoolIds = invoices.Select(i => i.SchoolId).Distinct().ToList();
            var feesPayerBySchool = await _context.SchoolPaymentSettings
                .Where(s => schoolIds.Contains(s.SchoolId))
                .ToDictionaryAsync(s => s.SchoolId, s => s.FeesPayer, ct);
            var platform = await _context.GetPlatformSettingsAsync(ct);

            var result = invoices
                .GroupBy(i => i.SchoolId)
                .Select(schoolGroup =>
                {
                    var first = schoolGroup.First();
                    var totalDue = schoolGroup.Sum(i => i.AmountDueFcfa - i.AmountPaidFcfa);
                    var feesPayer = feesPayerBySchool.TryGetValue(schoolGroup.Key, out var fp)
                        ? fp : FeesPayer.Parent;
                    var amountToCharge = feesPayer == FeesPayer.Parent
                        ? (long)Math.Ceiling(totalDue * platform.ParentFeeMultiplier)
                        : totalDue;
                    var children = schoolGroup
                        .GroupBy(i => i.StudentId)
                        .Select(childGroup =>
                        {
                            var s = childGroup.First().Student;
                            return new GuardianOutstandingChildDto
                            {
                                StudentId = childGroup.Key,
                                StudentName = $"{s.FirstName} {s.LastName}".Trim(),
                                ClassName = s.Class?.Name,
                                DueFcfa = childGroup.Sum(i => i.AmountDueFcfa - i.AmountPaidFcfa),
                                InvoiceCount = childGroup.Count()
                            };
                        })
                        .OrderBy(c => c.StudentName)
                        .ToList();

                    return new GuardianOutstandingSchoolDto
                    {
                        SchoolId = schoolGroup.Key,
                        SchoolName = first.Student.School.Name ?? string.Empty,
                        SchoolNameAr = first.Student.School.NameAr,
                        SchoolLogoUrl = first.Student.School.LogoUrl,
                        TotalDueFcfa = totalDue,
                        AmountToChargeFcfa = amountToCharge,
                        FeesPayer = feesPayer,
                        InvoiceCount = schoolGroup.Count(),
                        ChildCount = children.Count,
                        Children = children
                    };
                })
                .OrderByDescending(s => s.TotalDueFcfa)
                .ToList();

            return Ok(result);
        }

        /// <summary>
        /// `GET /api/guardian/daara-events` — la vie du daara vue par un parent.
        ///
        /// Ne renvoie QUE les événements dont l'école a explicitement ouvert la
        /// visibilité aux parents. ⚠️ Le filtre porte sur l'égalité stricte à
        /// <see cref="EventVisibility.Guardians"/> : écrire <c>&gt;=</c> ici
        /// serait juste aujourd'hui mais exposerait tout niveau plus ouvert
        /// ajouté un jour au-dessus (« public », « donateurs »…) sans que
        /// personne ne repasse par ce fichier.
        ///
        /// Les écoles concernées sont celles des enfants du parent (il n'a pas
        /// de SchoolId dans son jeton), enfants supprimés exclus.
        /// </summary>
        [HttpGet("daara-events")]
        public async Task<ActionResult<DaaraEventListDto>> GetDaaraEvents(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 30,
            CancellationToken ct = default)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var schoolIds = await _context.StudentGuardians
                .Where(sg => sg.GuardianId == userId.Value && !sg.Student.IsDeleted)
                .Select(sg => sg.Student.SchoolId)
                .Distinct()
                .ToListAsync(ct);

            if (schoolIds.Count == 0)
                return Ok(new DaaraEventListDto { Page = 1, PageSize = pageSize });

            var query = _context.DaaraEvents
                .Where(e => schoolIds.Contains(e.SchoolId)
                            && e.Visibility == EventVisibility.Guardians);

            var total = await query.CountAsync(ct);
            var p = page < 1 ? 1 : page;
            var size = Math.Clamp(pageSize, 1, 100);

            var items = await query
                .OrderByDescending(e => e.Date)
                .ThenByDescending(e => e.Id)
                .Skip((p - 1) * size)
                .Take(size)
                .Include(e => e.Photos)
                .ToListAsync(ct);

            // Nom du daara : un parent dont les enfants sont dans deux daara
            // doit savoir lequel a organisé la cérémonie.
            var schoolNames = await _context.Schools
                .Where(s => schoolIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name, s.NameAr })
                .ToListAsync(ct);
            var namesById = schoolNames.ToDictionary(s => s.Id, s => (s.Name, s.NameAr));

            return Ok(new DaaraEventListDto
            {
                Items = items.Select(e => new DaaraEventDto
                {
                    Id = e.Id,
                    Date = e.Date,
                    Title = e.Title,
                    Description = e.Description,
                    Category = e.Category,
                    Visibility = e.Visibility,
                    // L'auteur n'est PAS exposé aux parents : savoir quel membre
                    // de l'équipe a saisi la note ne les concerne pas, et
                    // pourrait le mettre en cause sur un événement mal reçu.
                    CreatedByName = null,
                    CreatedAt = e.CreatedAt,
                    UpdatedAt = e.UpdatedAt,
                    SchoolName = namesById.TryGetValue(e.SchoolId, out var n) ? n.Name : null,
                    SchoolNameAr = namesById.TryGetValue(e.SchoolId, out var na) ? na.NameAr : null,
                    Photos = e.Photos.OrderBy(ph => ph.Id).Select(ph => new DaaraEventPhotoDto
                    {
                        Id = ph.Id,
                        FilePath = ph.FilePath,
                        ContentType = ph.ContentType,
                        FileSize = ph.FileSize,
                        UploadedAt = ph.UploadedAt,
                    }).ToList(),
                }).ToList(),
                TotalCount = total,
                Page = p,
                PageSize = size,
            });
        }

        private async Task<bool> IsLinked(int studentId)
        {
            var userId = User.GetUserId();
            if (userId == null) return false;
            // Filtre !IsDeleted : un enfant supprimé par l'école ne doit plus être
            // consultable par le parent (notes/coran/bulletins…), même si le lien
            // StudentGuardian subsiste (review §F3).
            return await _context.StudentGuardians
                .AnyAsync(sg => sg.GuardianId == userId.Value
                                && sg.StudentId == studentId
                                && !sg.Student.IsDeleted);
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
        /// <summary>Nom du daara en arabe (affiché sous le nom français). Null si absent.</summary>
        public string? SchoolNameAr { get; set; }
        public int SchoolId { get; set; }
        public string? StudentNumber { get; set; }
        public bool IsPrimaryGuardian { get; set; }

        /// <summary>Date de sortie de l'effectif (éventuellement future = programmée). Null = inscrit.</summary>
        public DateTime? ExitDate { get; set; }

        /// <summary>« N'est plus inscrit » — calculé PAR LE SERVEUR (§151), jamais
        /// recomparé à l'horloge du téléphone. L'app regroupe ces enfants dans une
        /// section dédiée, en lecture seule, dette payable.</summary>
        public bool IsExited { get; set; }

        /// <summary>
        /// Mode de facturation de l'école de cet enfant. Le client l'utilise
        /// pour décider d'afficher ou non l'option « paiement libre ».
        /// </summary>
        public BillingMode? SchoolBillingMode { get; set; }

        /// <summary>
        /// Qui porte les frais dans l'école de cet enfant. Le client l'utilise
        /// pour n'afficher la note « frais de service inclus » que si le parent
        /// les porte (FeesPayer=Parent) — utile au flux « paiement libre » où le
        /// montant global (qui, lui, porte déjà FeesPayer) n'est pas disponible.
        /// </summary>
        public FeesPayer? SchoolFeesPayer { get; set; }
    }

    /// <summary>Total des mensualités impayées d'un parent dans une école (paiement global).</summary>
    public class GuardianOutstandingSchoolDto
    {
        public int SchoolId { get; set; }
        public string SchoolName { get; set; } = string.Empty;
        /// <summary>Nom du daara en arabe (affiché sous le nom français). Null si absent.</summary>
        public string? SchoolNameAr { get; set; }
        /// <summary>Logo du daara (chemin relatif /uploads/...) pour l'afficher sur la carte « à payer ».</summary>
        public string? SchoolLogoUrl { get; set; }

        /// <summary>Total dû (dette de la famille, avant majoration).</summary>
        public long TotalDueFcfa { get; set; }

        /// <summary>Montant réellement débité au parent (= TotalDue × majoration si FeesPayer=Parent, sinon TotalDue).</summary>
        public long AmountToChargeFcfa { get; set; }

        /// <summary>Qui porte les frais (détermine si une majoration s'applique).</summary>
        public FeesPayer FeesPayer { get; set; }

        public int InvoiceCount { get; set; }
        public int ChildCount { get; set; }
        public List<GuardianOutstandingChildDto> Children { get; set; } = new();
    }

    /// <summary>Détail par enfant dans une carte « paiement global ».</summary>
    public class GuardianOutstandingChildDto
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string? ClassName { get; set; }
        public long DueFcfa { get; set; }
        public int InvoiceCount { get; set; }
    }
}
