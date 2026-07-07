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

            // 1) Liste des enfants liés au guardian.
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
                    SchoolId = sg.Student.SchoolId,
                    StudentNumber = sg.Student.StudentNumber,
                    IsPrimaryGuardian = sg.IsPrimaryGuardian,
                })
                .ToListAsync();

            // 2) Hydrate avec le BillingMode de l'école (utile au front pour
            //    décider si on affiche le bouton "paiement libre" — sans ça
            //    le parent ne sait pas si l'école est en FixedAmount).
            //    Une requête en lot, mappée par schoolId.
            var schoolIds = students.Select(s => s.SchoolId).Distinct().ToList();
            if (schoolIds.Count > 0)
            {
                var modes = await _context.SchoolPaymentSettings
                    .Where(sps => schoolIds.Contains(sps.SchoolId))
                    .ToDictionaryAsync(sps => sps.SchoolId, sps => sps.BillingMode);
                foreach (var s in students)
                {
                    if (modes.TryGetValue(s.SchoolId, out var mode))
                    {
                        s.SchoolBillingMode = mode;
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
            if (status.HasValue)
                query = query.Where(p => p.Status == status.Value);
            else
                // Par défaut : masquer les paiements échoués / annulés / expirés
                // (un essai Wave raté puis réessayé affiche un « ÉCHEC » anxiogène
                // au parent) — uniquement réussis + en cours.
                query = query.Where(p =>
                    p.Status == PaymentStatus.Completed || p.Status == PaymentStatus.Pending);

            var items = await query
                .OrderByDescending(p => p.InitiatedAt)
                .Take(100)
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
        public int SchoolId { get; set; }
        public string? StudentNumber { get; set; }
        public bool IsPrimaryGuardian { get; set; }

        /// <summary>
        /// Mode de facturation de l'école de cet enfant. Le client l'utilise
        /// pour décider d'afficher ou non l'option « paiement libre ».
        /// </summary>
        public BillingMode? SchoolBillingMode { get; set; }
    }

    /// <summary>Total des mensualités impayées d'un parent dans une école (paiement global).</summary>
    public class GuardianOutstandingSchoolDto
    {
        public int SchoolId { get; set; }
        public string SchoolName { get; set; } = string.Empty;

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
