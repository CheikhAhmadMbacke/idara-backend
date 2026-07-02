using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Payment;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Endpoints école côté tarification :
    ///  - SchoolPaymentSettings (mode, fees payer, jour échéance)
    ///  - ClassFee (tarif par classe, versionné append-only)
    ///  - StudentFeeOverride (1-1, prime sur ClassFee)
    ///
    /// Tous limités à SchoolAdmin/SchoolStaff (lecture) et SchoolAdmin (écriture) —
    /// ce sont des décisions financières qui ne doivent pas pouvoir partir
    /// d'un Staff opérationnel.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/fees")]
    public class FeesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly MonthlyInvoiceGenerationJob _invoiceJob;
        private readonly ILogger<FeesController> _logger;

        public FeesController(
            AppDbContext context,
            MonthlyInvoiceGenerationJob invoiceJob,
            ILogger<FeesController> logger)
        {
            _context = context;
            _invoiceJob = invoiceJob;
            _logger = logger;
        }

        // ========================================================
        // ===== SchoolPaymentSettings =====
        // ========================================================

        [HttpGet("school-settings")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<SchoolPaymentSettingsDto>> GetSchoolSettings(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var settings = await _context.SchoolPaymentSettings
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId.Value, ct);

            // Si pas seedé (école très ancienne ou bug), on crée à la volée
            // pour ne jamais retourner 404 sur un endpoint de config.
            if (settings == null)
            {
                settings = new SchoolPaymentSettings
                {
                    SchoolId = schoolId.Value,
                    BillingMode = BillingMode.FixedAmount,
                    FeesPayer = FeesPayer.Parent,
                    MonthlyDueDay = 5,
                    BillingPeriod = BillingPeriod.Monthly,
                    CreatedAt = DateTime.UtcNow
                };
                _context.SchoolPaymentSettings.Add(settings);
                await _context.SaveChangesAsync(ct);
            }

            return Ok(MapSettings(settings));
        }

        [HttpPut("school-settings")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<SchoolPaymentSettingsDto>> UpdateSchoolSettings(
            [FromBody] UpdateSchoolPaymentSettingsDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var settings = await _context.SchoolPaymentSettings
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId.Value, ct);

            if (settings == null)
            {
                settings = new SchoolPaymentSettings
                {
                    SchoolId = schoolId.Value,
                    CreatedAt = DateTime.UtcNow
                };
                _context.SchoolPaymentSettings.Add(settings);
            }

            settings.BillingMode = dto.BillingMode;
            settings.FeesPayer = dto.FeesPayer;
            settings.MonthlyDueDay = dto.MonthlyDueDay;
            settings.BillingPeriod = dto.BillingPeriod;
            // 0 est traité comme "pas de tarif général" → on normalise en null.
            settings.GeneralMonthlyFeeFcfa =
                (dto.GeneralMonthlyFeeFcfa is > 0) ? dto.GeneralMonthlyFeeFcfa : null;
            settings.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            _logger.LogInformation(
                "[fees] SchoolId={SchoolId} settings updated: Mode={Mode} FeesPayer={Payer} DueDay={Day}",
                schoolId, dto.BillingMode, dto.FeesPayer, dto.MonthlyDueDay);
            return Ok(MapSettings(settings));
        }

        // ========================================================
        // ===== ClassFee =====
        // ========================================================

        /// <summary>
        /// Tableau récap par classe : nom + tarif courant + nb d'élèves +
        /// nombre d'historiques. Utilisé par l'écran "Tarifs par classe".
        /// </summary>
        [HttpGet("classes")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<IEnumerable<ClassCurrentFeeDto>>> GetCurrentClassFees(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var todayUtc = DateTime.UtcNow.Date;

            var classes = await _context.Classes
                .Where(c => c.SchoolId == schoolId.Value && !c.IsDeleted)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync(ct);

            if (classes.Count == 0) return Ok(Array.Empty<ClassCurrentFeeDto>());

            var classIds = classes.Select(c => c.Id).ToList();

            // Compteur d'élèves
            var counts = await _context.Students
                .Where(s => s.ClassId != null && classIds.Contains(s.ClassId.Value) && !s.IsDeleted)
                .GroupBy(s => s.ClassId!.Value)
                .Select(g => new { ClassId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ClassId, x => x.Count, ct);

            // Tarif courant par classe (max EffectiveFrom <= today)
            var current = await _context.ClassFees
                .Where(f =>
                    f.SchoolId == schoolId.Value &&
                    classIds.Contains(f.ClassId) &&
                    f.EffectiveFrom <= todayUtc)
                .GroupBy(f => f.ClassId)
                .Select(g => new
                {
                    ClassId = g.Key,
                    // ThenByDescending(Id) : départage les tarifs de même date
                    // de façon déterministe (la dernière saisie gagne) — cohérent
                    // avec MonthlyInvoiceGenerationJob (cf. bug du 200 FCFA).
                    Top = g.OrderByDescending(f => f.EffectiveFrom)
                           .ThenByDescending(f => f.Id)
                           .First()
                })
                .ToDictionaryAsync(x => x.ClassId, x => x.Top, ct);

            // Historique count par classe
            var history = await _context.ClassFees
                .Where(f => f.SchoolId == schoolId.Value && classIds.Contains(f.ClassId))
                .GroupBy(f => f.ClassId)
                .Select(g => new { ClassId = g.Key, N = g.Count() })
                .ToDictionaryAsync(x => x.ClassId, x => x.N, ct);

            var rows = classes.Select(c => new ClassCurrentFeeDto
            {
                ClassId = c.Id,
                ClassName = c.Name,
                StudentCount = counts.TryGetValue(c.Id, out var n) ? n : 0,
                CurrentAmountFcfa = current.TryGetValue(c.Id, out var f) ? f.AmountFcfa : null,
                CurrentEffectiveFrom = current.TryGetValue(c.Id, out var f2) ? f2.EffectiveFrom : null,
                HistoryCount = history.TryGetValue(c.Id, out var h) ? h : 0
            }).ToList();

            return Ok(rows);
        }

        /// <summary>Historique complet des tarifs d'une classe (du plus récent au plus ancien).</summary>
        [HttpGet("classes/{classId}/history")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<IEnumerable<ClassFeeDto>>> GetClassFeeHistory(int classId, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var classEntity = await _context.Classes.FirstOrDefaultAsync(
                c => c.Id == classId && c.SchoolId == schoolId.Value && !c.IsDeleted, ct);
            if (classEntity == null) return NotFound(ApiResponse<bool>.Fail("Classe introuvable."));

            var history = await _context.ClassFees
                .Where(f => f.ClassId == classId)
                .OrderByDescending(f => f.EffectiveFrom)
                .ThenByDescending(f => f.Id)
                .Select(f => new ClassFeeDto
                {
                    Id = f.Id,
                    ClassId = f.ClassId,
                    ClassName = classEntity.Name,
                    SchoolId = f.SchoolId,
                    AmountFcfa = f.AmountFcfa,
                    EffectiveFrom = f.EffectiveFrom,
                    CreatedAt = f.CreatedAt
                })
                .ToListAsync(ct);

            return Ok(history);
        }

        [HttpPost("classes/{classId}")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ClassFeeDto>> CreateClassFee(
            int classId, [FromBody] CreateClassFeeDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var classEntity = await _context.Classes.FirstOrDefaultAsync(
                c => c.Id == classId && c.SchoolId == schoolId.Value && !c.IsDeleted, ct);
            if (classEntity == null) return NotFound(ApiResponse<bool>.Fail("Classe introuvable."));

            var effectiveFrom = (dto.EffectiveFrom ?? DateTime.UtcNow).ToUtcDay();

            var fee = new ClassFee
            {
                ClassId = classId,
                SchoolId = schoolId.Value,
                AmountFcfa = dto.AmountFcfa,
                EffectiveFrom = effectiveFrom,
                CreatedById = userId.Value,
                CreatedAt = DateTime.UtcNow
            };
            _context.ClassFees.Add(fee);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[fees] ClassFee créé : SchoolId={SchoolId} ClassId={ClassId} Amount={Amount} EffectiveFrom={From:yyyy-MM-dd}",
                schoolId, classId, dto.AmountFcfa, effectiveFrom);

            return Ok(new ClassFeeDto
            {
                Id = fee.Id,
                ClassId = fee.ClassId,
                ClassName = classEntity.Name,
                SchoolId = fee.SchoolId,
                AmountFcfa = fee.AmountFcfa,
                EffectiveFrom = fee.EffectiveFrom,
                CreatedAt = fee.CreatedAt
            });
        }

        // ========================================================
        // ===== StudentFeeOverride =====
        // ========================================================

        [HttpGet("students/{studentId}/override")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<StudentFeeOverrideDto>> GetStudentOverride(int studentId, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var student = await _context.Students.FirstOrDefaultAsync(
                s => s.Id == studentId && s.SchoolId == schoolId.Value && !s.IsDeleted, ct);
            if (student == null) return NotFound(ApiResponse<bool>.Fail("Élève introuvable."));

            var ov = await _context.StudentFeeOverrides
                .FirstOrDefaultAsync(o => o.StudentId == studentId, ct);

            // 204 plutôt que 200 avec null (gotcha §48 : Ok((T?)null) est ambigu).
            if (ov == null) return NoContent();

            return Ok(new StudentFeeOverrideDto
            {
                StudentId = ov.StudentId,
                SchoolId = ov.SchoolId,
                AmountFcfa = ov.AmountFcfa,
                Reason = ov.Reason,
                CreatedAt = ov.CreatedAt,
                UpdatedAt = ov.UpdatedAt
            });
        }

        [HttpPut("students/{studentId}/override")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<StudentFeeOverrideDto>> UpsertStudentOverride(
            int studentId, [FromBody] UpsertStudentFeeOverrideDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var student = await _context.Students.FirstOrDefaultAsync(
                s => s.Id == studentId && s.SchoolId == schoolId.Value && !s.IsDeleted, ct);
            if (student == null) return NotFound(ApiResponse<bool>.Fail("Élève introuvable."));

            var ov = await _context.StudentFeeOverrides
                .FirstOrDefaultAsync(o => o.StudentId == studentId, ct);
            var isNew = ov == null;

            if (ov == null)
            {
                ov = new StudentFeeOverride
                {
                    StudentId = studentId,
                    SchoolId = schoolId.Value,
                    CreatedById = userId.Value,
                    CreatedAt = DateTime.UtcNow
                };
                _context.StudentFeeOverrides.Add(ov);
            }

            ov.AmountFcfa = dto.AmountFcfa;
            ov.Reason = dto.Reason;
            if (!isNew) ov.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            _logger.LogInformation(
                "[fees] StudentFeeOverride upsert SchoolId={SchoolId} StudentId={StudentId} Amount={Amount} (new={IsNew})",
                schoolId, studentId, dto.AmountFcfa, isNew);

            return Ok(new StudentFeeOverrideDto
            {
                StudentId = ov.StudentId,
                SchoolId = ov.SchoolId,
                AmountFcfa = ov.AmountFcfa,
                Reason = ov.Reason,
                CreatedAt = ov.CreatedAt,
                UpdatedAt = ov.UpdatedAt
            });
        }

        [HttpDelete("students/{studentId}/override")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<IActionResult> DeleteStudentOverride(int studentId, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var ov = await _context.StudentFeeOverrides
                .Include(o => o.Student)
                .FirstOrDefaultAsync(o => o.StudentId == studentId, ct);
            if (ov == null || ov.SchoolId != schoolId.Value) return NotFound();

            _context.StudentFeeOverrides.Remove(ov);
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation(
                "[fees] StudentFeeOverride supprimé SchoolId={SchoolId} StudentId={StudentId}",
                schoolId, studentId);
            return NoContent();
        }

        // ========================================================
        // ===== Vue école : Invoices émises =====
        // ========================================================

        /// <summary>
        /// Liste les Invoices émises par l'école courante. Filtres :
        ///  - status optionnel (Pending / Paid / Overdue / Cancelled)
        ///  - studentId optionnel
        ///  - from / to optionnels (sur PeriodStart)
        /// Tri : DueDate desc.
        /// </summary>
        [HttpGet("invoices")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<IEnumerable<InvoiceDto>>> GetSchoolInvoices(
            [FromQuery] InvoiceStatus? status,
            [FromQuery] int? studentId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var query = _context.Invoices
                .Where(i => i.SchoolId == schoolId.Value);

            if (status.HasValue) query = query.Where(i => i.Status == status.Value);
            if (studentId.HasValue) query = query.Where(i => i.StudentId == studentId.Value);
            if (from.HasValue) query = query.Where(i => i.PeriodStart >= from.Value.ToUtcDay());
            if (to.HasValue) query = query.Where(i => i.PeriodStart <= to.Value.ToUtcDay());

            var items = await query
                .OrderByDescending(i => i.DueDate)
                .Take(500)
                .Select(i => new InvoiceDto
                {
                    Id = i.Id,
                    SchoolId = i.SchoolId,
                    StudentId = i.StudentId,
                    StudentFirstName = i.Student.FirstName,
                    StudentLastName = i.Student.LastName,
                    StudentNumber = i.Student.StudentNumber,
                    ClassName = i.Student.Class != null ? i.Student.Class.Name : null,
                    PeriodStart = i.PeriodStart,
                    PeriodEnd = i.PeriodEnd,
                    DueDate = i.DueDate,
                    AmountDueFcfa = i.AmountDueFcfa,
                    AmountPaidFcfa = i.AmountPaidFcfa,
                    Status = i.Status,
                    CreatedAt = i.CreatedAt,
                    UpdatedAt = i.UpdatedAt
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        /// <summary>
        /// Roster « cahier d'appel des paiements » pour un mois donné : la liste
        /// de TOUS les élèves actifs de l'école avec leur statut de paiement
        /// (À jour / En attente / En retard / Sans facture) pour la période
        /// <c>year-month-01</c>. Compteurs par statut inclus.
        /// </summary>
        [HttpGet("roster")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<PaymentRosterResponseDto>> GetPaymentRoster(
            [FromQuery] int year,
            [FromQuery] int month,
            CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();
            if (month < 1 || month > 12 || year < 2020 || year > 2100)
                return BadRequest(ApiResponse<bool>.Fail("Mois ou année invalide."));

            var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var todayUtc = DateTime.UtcNow.Date;

            var students = await _context.Students
                .Where(s => s.SchoolId == schoolId.Value && !s.IsDeleted)
                .Select(s => new
                {
                    s.Id,
                    s.FirstName,
                    s.LastName,
                    s.StudentNumber,
                    ClassName = s.Class != null ? s.Class.Name : null
                })
                .ToListAsync(ct);

            // Factures du mois (hors annulées) — matérialisées pour faire la
            // comparaison d'échéance en mémoire (évite tout souci de traduction).
            var invoices = await _context.Invoices
                .Where(i => i.SchoolId == schoolId.Value
                    && i.PeriodStart == periodStart
                    && i.Status != InvoiceStatus.Cancelled)
                .Select(i => new
                {
                    i.Id,
                    i.StudentId,
                    i.AmountDueFcfa,
                    i.AmountPaidFcfa,
                    i.Status,
                    i.DueDate
                })
                .ToListAsync(ct);

            var byStudent = invoices
                .GroupBy(i => i.StudentId)
                .ToDictionary(g => g.Key, g => g.First());

            var entries = new List<PaymentRosterEntryDto>(students.Count);
            foreach (var s in students)
            {
                byStudent.TryGetValue(s.Id, out var inv);
                RosterPaymentStatus status;
                if (inv == null)
                    status = RosterPaymentStatus.NoInvoice;
                else if (inv.Status == InvoiceStatus.Paid || inv.AmountPaidFcfa >= inv.AmountDueFcfa)
                    status = RosterPaymentStatus.Paid;
                else if (inv.Status == InvoiceStatus.Overdue || todayUtc > inv.DueDate.Date)
                    status = RosterPaymentStatus.Overdue;
                else
                    status = RosterPaymentStatus.Pending;

                entries.Add(new PaymentRosterEntryDto
                {
                    StudentId = s.Id,
                    StudentFirstName = s.FirstName,
                    StudentLastName = s.LastName,
                    StudentNumber = s.StudentNumber,
                    ClassName = s.ClassName,
                    Status = status,
                    AmountDueFcfa = inv?.AmountDueFcfa ?? 0,
                    AmountPaidFcfa = inv?.AmountPaidFcfa ?? 0,
                    InvoiceId = inv?.Id,
                    DueDate = inv?.DueDate
                });
            }

            // Tri : en retard d'abord (à traiter en priorité), puis en attente,
            // puis à jour, puis sans facture ; ensuite par nom.
            static int Rank(RosterPaymentStatus s) => s switch
            {
                RosterPaymentStatus.Overdue => 0,
                RosterPaymentStatus.Pending => 1,
                RosterPaymentStatus.Paid => 2,
                _ => 3
            };
            entries = entries
                .OrderBy(e => Rank(e.Status))
                .ThenBy(e => e.StudentLastName)
                .ThenBy(e => e.StudentFirstName)
                .ToList();

            return Ok(new PaymentRosterResponseDto
            {
                Year = year,
                Month = month,
                PaidCount = entries.Count(e => e.Status == RosterPaymentStatus.Paid),
                PendingCount = entries.Count(e => e.Status == RosterPaymentStatus.Pending),
                OverdueCount = entries.Count(e => e.Status == RosterPaymentStatus.Overdue),
                NoInvoiceCount = entries.Count(e => e.Status == RosterPaymentStatus.NoInvoice),
                Entries = entries
            });
        }

        /// <summary>
        /// Annule une facture (passe son Status à <see cref="InvoiceStatus.Cancelled"/>).
        /// SchoolAdmin uniquement.
        ///
        /// <para>Ce n'est PAS un soft-delete déguisé : l'index unique du cron de
        /// génération est filtré sur <c>Status &lt;&gt; Cancelled</c> (gotcha §58),
        /// donc annuler LIBÈRE la période <c>(StudentId, PeriodStart)</c> — le cron
        /// recréera une facture neuve au prochain run (ou via le déclenchement
        /// manuel SuperAdmin). C'est le chemin officiel pour repartir proprement
        /// sur une mensualité erronée.</para>
        ///
        /// <para>⚠️ Annuler NE REMBOURSE RIEN : si la facture a déjà reçu un
        /// paiement, l'argent reste crédité au wallet école (Payment /
        /// WalletTransaction sont append-only et indépendants — gotcha §55). On
        /// bloque donc l'annulation d'une facture déjà (partiellement) payée sauf
        /// <c>?force=true</c> explicite (correction admin ou test assumé).</para>
        /// </summary>
        [HttpPost("invoices/{id}/cancel")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<InvoiceDto>>> CancelInvoice(
            int id,
            [FromQuery] bool force,
            CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var invoice = await _context.Invoices
                .Include(i => i.Student).ThenInclude(s => s.Class)
                .FirstOrDefaultAsync(i => i.Id == id && i.SchoolId == schoolId.Value, ct);

            if (invoice == null)
                return NotFound(ApiResponse<InvoiceDto>.Fail("Facture introuvable."));

            if (invoice.Status == InvoiceStatus.Cancelled)
                return BadRequest(ApiResponse<InvoiceDto>.Fail("Cette facture est déjà annulée."));

            // Garde-fou : un montant déjà payé = argent réellement encaissé.
            // L'annulation ne le rembourse pas, on exige un force explicite.
            if (invoice.AmountPaidFcfa > 0 && !force)
            {
                return BadRequest(ApiResponse<InvoiceDto>.Fail(
                    $"Cette facture a déjà reçu {invoice.AmountPaidFcfa} FCFA. " +
                    "L'annuler ne rembourse pas ce montant (il reste crédité au wallet école). " +
                    "Confirmez avec force=true si vous êtes sûr."));
            }

            invoice.Status = InvoiceStatus.Cancelled;
            invoice.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[fees] Invoice {InvoiceId} annulée par SchoolAdmin {UserId} (School {SchoolId}, montantPayé={Paid}, force={Force})",
                invoice.Id, User.GetUserId(), schoolId.Value, invoice.AmountPaidFcfa, force);

            return Ok(ApiResponse<InvoiceDto>.Ok(new InvoiceDto
            {
                Id = invoice.Id,
                SchoolId = invoice.SchoolId,
                StudentId = invoice.StudentId,
                StudentFirstName = invoice.Student.FirstName,
                StudentLastName = invoice.Student.LastName,
                StudentNumber = invoice.Student.StudentNumber,
                ClassName = invoice.Student.Class?.Name,
                PeriodStart = invoice.PeriodStart,
                PeriodEnd = invoice.PeriodEnd,
                DueDate = invoice.DueDate,
                AmountDueFcfa = invoice.AmountDueFcfa,
                AmountPaidFcfa = invoice.AmountPaidFcfa,
                Status = invoice.Status,
                CreatedAt = invoice.CreatedAt,
                UpdatedAt = invoice.UpdatedAt
            }, "Facture annulée."));
        }

        // ========================================================
        // ===== Vue école : Wallet + transactions =====
        // ========================================================

        [HttpGet("wallet")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<object>> GetWallet(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            // Filet de secours : une école créée entre deux redémarrages n'a
            // pas été seedée par DbInitializer → on crée ses fondations à la
            // volée pour ne jamais renvoyer 404 sur le wallet.
            await _context.EnsurePaymentFoundationsAsync(schoolId.Value, ct);

            var wallet = await _context.SchoolWallets
                .FirstOrDefaultAsync(w => w.SchoolId == schoolId.Value, ct);
            if (wallet == null) return NotFound(ApiResponse<bool>.Fail("Wallet introuvable."));

            var recentTx = await _context.WalletTransactions
                .Where(t => t.SchoolId == schoolId.Value)
                .OrderByDescending(t => t.OccurredAt)
                .Take(50)
                .Select(t => new
                {
                    t.Id,
                    t.Type,
                    t.Source,
                    t.AmountFcfa,
                    t.BalanceAfter,
                    t.RelatedEntity,
                    t.RelatedId,
                    t.Note,
                    t.OccurredAt
                })
                .ToListAsync(ct);

            return Ok(new
            {
                wallet.SchoolId,
                wallet.AvailableBalance,
                wallet.PendingBalance,
                // Deux poches du solde disponible (remarque produit) :
                //  - Solde don   = DonationBalanceFcfa
                //  - Solde paiement = Available − Don (dérivé)
                DonationBalance = wallet.DonationBalanceFcfa,
                FeeBalance = wallet.AvailableBalance - wallet.DonationBalanceFcfa,
                wallet.TotalCreditedLifetime,
                wallet.TotalWithdrawnLifetime,
                wallet.UpdatedAt,
                RecentTransactions = recentTx
            });
        }

        // ========================================================
        // ===== Vue école : Paiements reçus (audit) =====
        // ========================================================

        [HttpGet("payments")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<IEnumerable<PaymentDto>>> GetSchoolPayments(
            [FromQuery] PaymentStatus? status,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var query = _context.Payments
                .Where(p => p.SchoolId == schoolId.Value);

            if (status.HasValue)
                query = query.Where(p => p.Status == status.Value);
            else
                // Par défaut : on masque les paiements échoués / annulés / expirés
                // (bruit déroutant pour le public non-tech) — uniquement réussis +
                // en cours. Un statut explicite reste possible pour le debug.
                query = query.Where(p =>
                    p.Status == PaymentStatus.Completed || p.Status == PaymentStatus.Pending);

            if (from.HasValue) query = query.Where(p => p.InitiatedAt >= from.Value.ToUtcSafe());
            if (to.HasValue) query = query.Where(p => p.InitiatedAt <= to.Value.ToUtcSafe());

            var items = await query
                .OrderByDescending(p => p.InitiatedAt)
                .Take(500)
                .Select(p => new PaymentDto
                {
                    Id = p.Id,
                    SchoolId = p.SchoolId,
                    StudentId = p.StudentId,
                    StudentFirstName = p.Student != null ? p.Student.FirstName : null,
                    StudentLastName = p.Student != null ? p.Student.LastName : null,
                    StudentNumber = p.Student != null ? p.Student.StudentNumber : null,
                    GuardianId = p.GuardianId,
                    InvoiceId = p.InvoiceId,
                    Purpose = p.Purpose,
                    DonorId = p.DonorId,
                    DonorName = p.Donor != null ? p.Donor.FullName : null,
                    DonorType = p.Donor != null ? p.Donor.DonorType : null,
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
                })
                .ToListAsync(ct);

            // Vue par défaut : regroupe les tentatives multiples d'une même
            // facture (réessais parent) → 1 ligne/facture (Complété sinon la plus
            // récente). Évite la duplication des « en cours » côté école. Les
            // paiements sans facture (topup) gardent une clé unique. Pas de
            // regroupement si un statut explicite est demandé (liste brute debug).
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

            return Ok(items);
        }

        // ========================================================
        // ===== Cron manuel (SuperAdmin) =====
        // ========================================================

        /// <summary>
        /// Déclenche manuellement la génération des Invoices mensuelles.
        /// SuperAdmin uniquement. Comportement :
        ///  - sans paramètre : génère pour les écoles dont MonthlyDueDay tombe aujourd'hui ;
        ///  - <c>?forceDay=N</c> (1..28) : simule le jour N du mois courant
        ///    (génère pour les écoles dont MonthlyDueDay == N), pratique pour
        ///    tester ou rejouer un jour raté.
        /// </summary>
        [HttpPost("cron/invoices/run")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<InvoiceGenerationReport>> RunInvoiceCron(
            [FromQuery] int? forceDay,
            CancellationToken ct)
        {
            if (forceDay.HasValue && (forceDay.Value < 1 || forceDay.Value > 28))
            {
                return BadRequest(ApiResponse<bool>.Fail(
                    "forceDay doit être entre 1 et 28."));
            }
            _logger.LogInformation(
                "[fees] Cron Invoice déclenché manuellement par SuperAdmin {UserId} (forceDay={ForceDay})",
                User.GetUserId(), forceDay);
            var report = await _invoiceJob.RunOnceAsync(DateTime.UtcNow, ct, forceDay);
            return Ok(report);
        }

        /// <summary>
        /// `POST /api/fees/invoices/generate` — génère les factures du mois À LA
        /// DEMANDE pour SON école (SchoolAdmin), **sans attendre le jour
        /// d'échéance ni l'intervention du SuperAdmin**. Idempotent : un élève
        /// déjà facturé pour la période n'est pas re-facturé. Par défaut le mois
        /// courant ; <c>?year=&amp;month=</c> pour un autre mois (ex. générer le
        /// mois prochain pour un test). Réservé au mode FixedAmount.
        /// </summary>
        [HttpPost("invoices/generate")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<InvoiceGenerationReport>>> GenerateMyInvoices(
            [FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var now = DateTime.UtcNow;
            var y = year ?? now.Year;
            var m = month ?? now.Month;
            if (m < 1 || m > 12 || y < 2020 || y > now.Year + 1)
            {
                return BadRequest(ApiResponse<InvoiceGenerationReport>.Fail(
                    "Mois ou année invalide."));
            }

            _logger.LogInformation(
                "[fees] Génération factures à la demande par École {SchoolId} (user {UserId}) pour {Year}-{Month:D2}",
                schoolId, User.GetUserId(), y, m);

            var report = await _invoiceJob.GenerateForSchoolNowAsync(schoolId.Value, y, m, now, ct);

            string msg;
            if (report.InvoicesCreated > 0)
                msg = $"{report.InvoicesCreated} facture(s) générée(s).";
            else if (report.StudentsWithoutFee > 0 && report.InvoicesAlreadyExisting == 0)
                msg = "Aucune facture : aucun tarif configuré pour ces élèves. Définissez un tarif (général, par classe, ou par élève).";
            else if (report.InvoicesAlreadyExisting > 0)
                msg = "Les factures de ce mois existent déjà (aucune nouvelle créée).";
            else
                msg = "Aucune facture générée (aucun élève éligible).";

            return Ok(ApiResponse<InvoiceGenerationReport>.Ok(report, msg));
        }

        // ========================================================
        // ===== Helpers =====
        // ========================================================

        private static SchoolPaymentSettingsDto MapSettings(SchoolPaymentSettings s) => new()
        {
            SchoolId = s.SchoolId,
            BillingMode = s.BillingMode,
            FeesPayer = s.FeesPayer,
            MonthlyDueDay = s.MonthlyDueDay,
            BillingPeriod = s.BillingPeriod,
            GeneralMonthlyFeeFcfa = s.GeneralMonthlyFeeFcfa,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt
        };
    }
}
