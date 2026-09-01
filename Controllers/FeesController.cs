using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Export;
using Idara.API.DTOs.Payment;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services;
using Idara.API.Services.Notifications;
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
        private readonly IInvoiceRepricingService _repricing;
        private readonly ICashPaymentService _cashPayments;
        private readonly INotificationService _notif;
        private readonly IReceiptPdfService _receiptPdf;
        private readonly IExportPdfService _exportPdf;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<FeesController> _logger;

        public FeesController(
            AppDbContext context,
            MonthlyInvoiceGenerationJob invoiceJob,
            IInvoiceRepricingService repricing,
            ICashPaymentService cashPayments,
            INotificationService notif,
            IReceiptPdfService receiptPdf,
            IExportPdfService exportPdf,
            IWebHostEnvironment env,
            ILogger<FeesController> logger)
        {
            _context = context;
            _invoiceJob = invoiceJob;
            _repricing = repricing;
            _cashPayments = cashPayments;
            _notif = notif;
            _receiptPdf = receiptPdf;
            _exportPdf = exportPdf;
            _env = env;
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
                    DonationFeesPayer = FeesPayer.School,
                    MonthlyDueDay = 5,
                    PaymentDeadlineDay = 15,
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
            settings.DonationFeesPayer = dto.DonationFeesPayer;
            settings.MonthlyDueDay = dto.MonthlyDueDay;
            // Jour limite : appliqué UNIQUEMENT s'il est fourni. Une version de
            // l'application antérieure au 2026-08-23 ne l'envoie pas ; le
            // recopier tel quel ramènerait la limite au 1er du mois et
            // déclencherait une vague de rappels de retard (cf. le DTO).
            if (dto.PaymentDeadlineDay is int deadline)
                settings.PaymentDeadlineDay = deadline;
            settings.BillingPeriod = dto.BillingPeriod;
            // 0 est traité comme "pas de tarif général" → on normalise en null.
            settings.GeneralMonthlyFeeFcfa =
                (dto.GeneralMonthlyFeeFcfa is > 0) ? dto.GeneralMonthlyFeeFcfa : null;

            // Tarifs par régime d'hébergement : appliqués UNIQUEMENT si le
            // client a déclaré les gérer. Une version de l'application antérieure
            // à ces tarifs n'envoie pas les champs (donc null) ; sans ce garde-
            // fou, un simple enregistrement des réglages depuis une vieille APK
            // effacerait les trois tarifs — et toutes les factures impayées des
            // internes retomberaient au tarif de classe, sans un mot.
            if (dto.IncludesBoardingFees)
            {
                settings.BoardingMonthlyFeeFcfa =
                    (dto.BoardingMonthlyFeeFcfa is > 0) ? dto.BoardingMonthlyFeeFcfa : null;
                settings.HalfBoardingMonthlyFeeFcfa =
                    (dto.HalfBoardingMonthlyFeeFcfa is > 0) ? dto.HalfBoardingMonthlyFeeFcfa : null;
                settings.DayMonthlyFeeFcfa =
                    (dto.DayMonthlyFeeFcfa is > 0) ? dto.DayMonthlyFeeFcfa : null;
            }

            // Frais d'inscription : même garde-fou de capacité que les tarifs de
            // statut (3ᵉ occurrence du piège §137/§140/§152) — une version
            // antérieure de l'application n'envoie pas ce champ, le recopier tel
            // quel effacerait le montant à chaque enregistrement des réglages.
            if (dto.IncludesRegistrationFee)
            {
                settings.RegistrationFeeFcfa =
                    (dto.RegistrationFeeFcfa is > 0) ? dto.RegistrationFeeFcfa : null;
            }

            settings.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            _logger.LogInformation(
                "[fees] SchoolId={SchoolId} settings updated: Mode={Mode} FeesPayer={Payer} DueDay={Day} TarifsStatut={Boarding}",
                schoolId, dto.BillingMode, dto.FeesPayer, dto.MonthlyDueDay, dto.IncludesBoardingFees);

            // Le tarif général ou un tarif de statut a pu changer → re-tarifer
            // les factures impayées (portée = toute l'école ; le résolveur laisse
            // à chaque élève le tarif le plus spécifique qui le concerne).
            await _repricing.RepriceUnpaidInvoicesAsync(schoolId.Value, null, ct);

            return Ok(MapSettings(settings));
        }

        // ========================================================
        // ===== Masquage d'historique (affichage seul) =====
        // ========================================================

        /// <summary>
        /// `POST /api/fees/payments/{id}/hide` — masque un paiement de l'affichage
        /// école (le daara ne veut pas que cette ligne, souvent un échec, soit
        /// comptabilisée par ses partenaires). NON destructif : la ligne reste en
        /// base (aucun impact compta/réconciliation) et sort seulement des listes.
        /// Non réversible côté UI (décision produit 2026-07-11).
        /// </summary>
        [HttpPost("payments/{id:int}/hide")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<bool>>> HidePayment(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var n = await _context.Payments
                .Where(p => p.Id == id && p.SchoolId == schoolId.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsHidden, true), ct);
            if (n == 0) return NotFound(ApiResponse<bool>.Fail("Paiement introuvable."));

            return Ok(ApiResponse<bool>.Ok(true, "Transaction masquée."));
        }

        /// <summary>
        /// `POST /api/fees/wallet/transactions/{id}/hide` — masque une transaction
        /// wallet de l'affichage (même principe : cosmétique, ne touche pas au solde).
        /// </summary>
        [HttpPost("wallet/transactions/{id:int}/hide")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<bool>>> HideWalletTransaction(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var n = await _context.WalletTransactions
                .Where(t => t.Id == id && t.SchoolId == schoolId.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsHidden, true), ct);
            if (n == 0) return NotFound(ApiResponse<bool>.Fail("Transaction introuvable."));

            return Ok(ApiResponse<bool>.Ok(true, "Transaction masquée."));
        }

        /// <summary>
        /// `GET /api/fees/payments/{id}/receipt` — reçu PDF d'un paiement, côté
        /// ÉCOLE (SchoolAdmin/Staff), scopé à SON école. Sert au daara à partager
        /// le reçu (WhatsApp). Génère à la volée si le fichier manque (recovery).
        /// </summary>
        [HttpGet("payments/{id:int}/receipt")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> DownloadPaymentReceipt(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var p = await _context.Payments
                .Include(x => x.Student)
                .Include(x => x.Donor)
                .Include(x => x.InvoiceAllocations).ThenInclude(a => a.Invoice).ThenInclude(i => i.Student)
                .Include(x => x.StudentAllocations).ThenInclude(a => a.Student)
                .FirstOrDefaultAsync(x => x.Id == id && x.SchoolId == schoolId.Value, ct);
            if (p == null) return NotFound(ApiResponse<bool>.Fail("Paiement introuvable."));
            if (p.Status != PaymentStatus.Completed)
                return BadRequest(ApiResponse<bool>.Fail("Reçu disponible uniquement pour les paiements complétés."));

            var relativePath = p.ReceiptPdfPath ?? _receiptPdf.RelativePathFor(p.Id);
            var webRootFull = Path.GetFullPath(_env.WebRootPath);
            var fullPath = Path.GetFullPath(Path.Combine(
                webRootFull, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(webRootFull, StringComparison.Ordinal))
                return BadRequest(ApiResponse<bool>.Fail("Chemin de reçu invalide."));

            if (!System.IO.File.Exists(fullPath))
            {
                // Recovery : régénère selon la nature (parent / consolidé / don / topup).
                var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == p.SchoolId, ct);
                if (school == null) return NotFound();

                var invoice = p.InvoiceId.HasValue
                    ? await _context.Invoices.FirstOrDefaultAsync(x => x.Id == p.InvoiceId.Value, ct)
                    : null;
                var lines = ReceiptConsolidatedLine.LinesFor(p);

                var regenerated = await _receiptPdf.GenerateAsync(
                    p, school, p.Student, invoice, p.Donor, lines);
                if (string.IsNullOrEmpty(p.ReceiptPdfPath))
                    await _context.Payments.Where(x => x.Id == p.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReceiptPdfPath, regenerated), ct);
                fullPath = Path.GetFullPath(Path.Combine(
                    webRootFull, regenerated.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath, ct);
            return File(bytes, "application/pdf", $"recu-idara-{p.Id:D6}.pdf");
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

            // Compteur d'élèves (effectif : mêmes chiffres que la liste des classes)
            var counts = await _context.Students
                .Where(s => s.ClassId != null && classIds.Contains(s.ClassId.Value))
                .Enrolled()
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

            // Nouveau tarif courant pour la classe → re-tarifer les factures
            // impayées de ses élèves (sauf ceux à override, préservés par le résolveur).
            var classStudentIds = await _context.Students
                .Where(s => s.ClassId == classId && s.SchoolId == schoolId.Value)
                .Enrolled()
                .Select(s => s.Id)
                .ToListAsync(ct);
            await _repricing.RepriceUnpaidInvoicesAsync(schoolId.Value, classStudentIds, ct);

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

            // Override modifié → re-tarifer les factures impayées de cet élève.
            await _repricing.RepriceUnpaidInvoicesAsync(schoolId.Value, new[] { studentId }, ct);

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

            // Override retiré → l'élève retombe sur le tarif classe/général :
            // re-tarifer ses factures impayées au nouveau montant résolu.
            await _repricing.RepriceUnpaidInvoicesAsync(schoolId.Value, new[] { studentId }, ct);

            return NoContent();
        }

        /// <summary>
        /// Facture les frais d'inscription d'un élève APRÈS COUP — le chemin
        /// explicite quand l'école a oublié le montant à la création. Jamais un
        /// effet de bord de l'enregistrement de la fiche : modifier une fiche ne
        /// crée pas de facture.
        /// </summary>
        [HttpPost("students/{studentId}/registration-invoice")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<InvoiceDto>>> CreateRegistrationInvoice(
            int studentId, [FromQuery] long? amountFcfa, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var student = await _context.Students
                .Include(s => s.Class)
                .FirstOrDefaultAsync(
                    s => s.Id == studentId && s.SchoolId == schoolId.Value && !s.IsDeleted, ct);
            if (student == null)
                return NotFound(ApiResponse<InvoiceDto>.Fail("Élève introuvable."));

            // Garde anti double facturation : l'index unique ne bloque que le même
            // JOUR (PeriodStart) — deux appels à des dates différentes créeraient
            // deux factures d'inscription. Une inscription se facture UNE fois ;
            // pour recommencer, annuler d'abord l'existante.
            var already = await _context.Invoices.AnyAsync(
                i => i.StudentId == studentId
                     && i.Type == InvoiceType.Registration
                     && i.Status != InvoiceStatus.Cancelled, ct);
            if (already)
                return BadRequest(ApiResponse<InvoiceDto>.Fail(
                    "Cet élève a déjà une facture d'inscription. Annulez-la d'abord si le montant est à reprendre."));

            var amount = amountFcfa
                ?? await _context.SchoolPaymentSettings
                    .Where(ps => ps.SchoolId == schoolId.Value)
                    .Select(ps => ps.RegistrationFeeFcfa)
                    .FirstOrDefaultAsync(ct);
            if (amount is not > 0)
                return BadRequest(ApiResponse<InvoiceDto>.Fail(
                    "Aucun montant : passez amountFcfa ou configurez les frais d'inscription de l'école."));

            // PeriodStart = AUJOURD'HUI, pas le jour d'inscription de l'élève :
            // une échéance calée sur une inscription ancienne naîtrait « en
            // retard » et déclencherait un rappel immédiat.
            var today = DateTime.UtcNow.ToUtcDay();
            var invoice = new Invoice
            {
                SchoolId = schoolId.Value,
                StudentId = studentId,
                Type = InvoiceType.Registration,
                PeriodStart = today,
                PeriodEnd = today,
                DueDate = today.AddDays(7),
                AmountDueFcfa = amount.Value,
                AmountPaidFcfa = 0,
                Status = InvoiceStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[fees] Facture d'inscription {InvoiceId} créée après coup SchoolId={SchoolId} StudentId={StudentId} Montant={Amount}",
                invoice.Id, schoolId, studentId, amount.Value);

            // SMS aux responsables joignables — post-commit, best-effort, même
            // motif que la facture d'inscription créée à l'inscription de
            // l'élève (StudentService) : les deux chemins doivent prévenir.
            try
            {
                var guardians = await _context.StudentGuardians
                    .Where(sg => sg.StudentId == studentId
                                 && !sg.Guardian.IsDeleted
                                 && sg.Guardian.PhoneNumber != null)
                    .Select(sg => new
                    {
                        sg.GuardianId,
                        sg.Guardian.PhoneNumber,
                        sg.Guardian.PreferredLanguage
                    })
                    .ToListAsync(ct);
                if (guardians.Count > 0)
                {
                    var platform = await _context.GetPlatformSettingsAsync(ct);
                    var eleve = $"{student.FirstName} {student.LastName}".Trim();
                    var msg = NotificationTemplates.RegistrationFeeDue(eleve, amount.Value);
                    foreach (var g in guardians)
                    {
                        await _notif.SendSmsAsync(new NotificationSmsRequest(
                            UserId: g.GuardianId,
                            RawPhone: g.PhoneNumber,
                            PreferredLanguage: g.PreferredLanguage ?? "fr",
                            Message: msg,
                            Bilingual: platform.SmsBilingual,
                            TemplateCode: "REGISTRATION_DUE",
                            RelatedEntityId: invoice.Id,
                            PushRoute: "/guardian/invoices",
                            SchoolId: schoolId,
                            TriggerSource: "api:fees/registration-invoice",
                            TriggerUserId: User.GetUserId()), ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[fees] SMS frais d'inscription non envoyé (facture {InvoiceId}) — pas bloquant",
                    invoice.Id);
            }

            return Ok(ApiResponse<InvoiceDto>.Ok(new InvoiceDto
            {
                Id = invoice.Id,
                SchoolId = invoice.SchoolId,
                StudentId = invoice.StudentId,
                StudentFirstName = student.FirstName,
                StudentLastName = student.LastName,
                StudentNumber = student.StudentNumber,
                ClassName = student.Class?.Name,
                Type = invoice.Type,
                PeriodStart = invoice.PeriodStart,
                PeriodEnd = invoice.PeriodEnd,
                DueDate = invoice.DueDate,
                AmountDueFcfa = invoice.AmountDueFcfa,
                AmountPaidFcfa = invoice.AmountPaidFcfa,
                Status = invoice.Status,
                CreatedAt = invoice.CreatedAt
            }, "Facture d'inscription créée."));
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

            // !IsDeleted : un élève SUPPRIMÉ (doublon, erreur de saisie) ne doit pas
            // laisser ses impayés dans le recouvrement — oubli préexistant corrigé
            // le 2026-08-17 (les DEUX natures de facture restent listées, avec badge).
            var query = _context.Invoices
                .Where(i => i.SchoolId == schoolId.Value && !i.Student.IsDeleted);

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
                    Type = i.Type,
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

            return Ok(await BuildRosterAsync(schoolId.Value, year, month, ct));
        }

        /// <summary>Calcule le roster d'un mois (partagé par la vue JSON et l'export PDF).</summary>
        private async Task<PaymentRosterResponseDto> BuildRosterAsync(
            int schoolId, int year, int month, CancellationToken ct)
        {
            var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var todayUtc = DateTime.UtcNow.Date;

            // EnrolledDuring(periodStart), PAS Enrolled() : le suivi d'octobre
            // doit encore montrer un élève parti en novembre (il devait sa
            // mensualité d'octobre), et ne plus montrer celui parti en septembre.
            var students = await _context.Students
                .Where(s => s.SchoolId == schoolId)
                .EnrolledDuring(periodStart)
                .Select(s => new
                {
                    s.Id,
                    s.FirstName,
                    s.LastName,
                    s.StudentNumber,
                    ClassName = s.Class != null ? s.Class.Name : null,
                    // Parent à contacter : le responsable LIÉ au compte (celui qui
                    // paie dans l'app) d'abord — principal s'il y en a un —, sinon
                    // le père puis la mère du dossier élève.
                    LinkedGuardian = s.StudentGuardians
                        .Where(g => !g.Guardian.IsDeleted)
                        .OrderByDescending(g => g.IsPrimaryGuardian)
                        .ThenBy(g => g.GuardianId)
                        .Select(g => new { g.GuardianId, g.Guardian.FullName, g.Guardian.PhoneNumber })
                        .FirstOrDefault(),
                    s.FatherFullName,
                    s.FatherPhone,
                    s.MotherFullName,
                    s.MotherPhone
                })
                .ToListAsync(ct);

            // Factures du mois (hors annulées) — matérialisées pour faire la
            // comparaison d'échéance en mémoire (évite tout souci de traduction).
            // MENSUALITÉS uniquement : le roster est le cahier d'appel des
            // mensualités — une facture d'inscription impayée ne doit pas faire
            // apparaître l'élève « en retard » sur son mois, ni payée le faire
            // apparaître « à jour » alors que sa mensualité n'existe pas.
            var invoices = await _context.Invoices
                .Where(i => i.SchoolId == schoolId
                    && i.PeriodStart == periodStart
                    && i.Type == InvoiceType.MonthlyFee
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

            // Date du dernier paiement COMPLÉTÉ imputé à chaque facture : soit un
            // paiement direct (InvoiceId), soit une part de paiement global
            // consolidé (allocation). Les deux chemins existent depuis le
            // « paiement global » parent — n'en oublier aucun sinon des élèves
            // payés apparaîtraient sans date.
            var invoiceIds = invoices.Select(i => i.Id).ToList();
            var paidDirect = await _context.Payments
                .Where(p => p.InvoiceId != null && invoiceIds.Contains(p.InvoiceId.Value)
                            && p.Status == PaymentStatus.Completed && p.PaidAt != null)
                .Select(p => new { InvoiceId = p.InvoiceId!.Value, p.PaidAt })
                .ToListAsync(ct);
            var paidAllocated = await _context.PaymentInvoiceAllocations
                .Where(a => invoiceIds.Contains(a.InvoiceId)
                            && a.Payment.Status == PaymentStatus.Completed && a.Payment.PaidAt != null)
                .Select(a => new { a.InvoiceId, a.Payment.PaidAt })
                .ToListAsync(ct);
            var paidAtByInvoice = paidDirect.Concat(paidAllocated)
                .GroupBy(x => x.InvoiceId)
                .ToDictionary(g => g.Key, g => g.Max(x => x.PaidAt));

            // Liens de paiement ACTIFS des responsables liés (lien = par
            // responsable, donc partagé entre frères et sœurs) : « lien envoyé
            // le… / ouvert » dans le roster, pour savoir qui relancer.
            var linkedGuardianIds = students
                .Where(s => s.LinkedGuardian != null)
                .Select(s => s.LinkedGuardian!.GuardianId)
                .Distinct()
                .ToList();
            var linksByGuardian = await _context.PaymentLinks
                .Where(l => l.SchoolId == schoolId && l.RevokedAt == null && linkedGuardianIds.Contains(l.GuardianId))
                .Select(l => new { l.GuardianId, l.CreatedAt, l.LastSharedAt, l.LastOpenedAt })
                .ToDictionaryAsync(l => l.GuardianId, ct);

            var entries = new List<PaymentRosterEntryDto>(students.Count);
            foreach (var s in students)
            {
                var link = s.LinkedGuardian != null && linksByGuardian.TryGetValue(s.LinkedGuardian.GuardianId, out var lk)
                    ? lk : null;
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

                // Cascade parent : responsable lié au compte > père > mère. Le nom
                // et le numéro viennent TOUJOURS de la même source (sinon on
                // afficherait le nom du père avec le numéro de la mère).
                var guardianName = s.LinkedGuardian?.FullName;
                var guardianPhone = s.LinkedGuardian?.PhoneNumber;
                if (string.IsNullOrWhiteSpace(guardianName))
                    (guardianName, guardianPhone) = (s.FatherFullName, s.FatherPhone);
                if (string.IsNullOrWhiteSpace(guardianName))
                    (guardianName, guardianPhone) = (s.MotherFullName, s.MotherPhone);

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
                    DueDate = inv?.DueDate,
                    GuardianFullName = string.IsNullOrWhiteSpace(guardianName) ? null : guardianName.Trim(),
                    GuardianPhone = string.IsNullOrWhiteSpace(guardianPhone) ? null : guardianPhone.Trim(),
                    PaidAt = inv != null && paidAtByInvoice.TryGetValue(inv.Id, out var pa) ? pa : null,
                    HasLinkedGuardian = s.LinkedGuardian != null,
                    PaymentLinkSentAt = link?.LastSharedAt ?? link?.CreatedAt,
                    PaymentLinkOpenedAt = link?.LastOpenedAt
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

            // Le calendrier du mois affiché : c'est lui qui dit au daara s'il
            // doit encore attendre ou s'il peut relancer et clore son mois.
            //
            // ⚠️ UNIQUEMENT en montant fixe. Une école en montant libre n'a pas
            // de mensualité générée, donc pas d'échéance : lui annoncer une date
            // limite serait une promesse que rien ne tient — aucune facture ne
            // basculera « en retard » ce jour-là, et personne ne sera relancé.
            var schedule = await _context.SchoolPaymentSettings
                .Where(s => s.SchoolId == schoolId)
                .Select(s => new { s.BillingMode, s.MonthlyDueDay, s.PaymentDeadlineDay })
                .FirstOrDefaultAsync(ct);
            var hasSchedule = schedule?.BillingMode == BillingMode.FixedAmount;
            var openingDay = schedule?.MonthlyDueDay ?? 5;
            var deadlineDay = schedule?.PaymentDeadlineDay ?? 15;

            return new PaymentRosterResponseDto
            {
                Year = year,
                Month = month,
                OpeningDay = hasSchedule ? openingDay : 0,
                DeadlineDate = hasSchedule
                    ? PaymentSchedule.DeadlineFor(periodStart, openingDay, deadlineDay)
                    : null,
                PaidCount = entries.Count(e => e.Status == RosterPaymentStatus.Paid),
                PendingCount = entries.Count(e => e.Status == RosterPaymentStatus.Pending),
                OverdueCount = entries.Count(e => e.Status == RosterPaymentStatus.Overdue),
                NoInvoiceCount = entries.Count(e => e.Status == RosterPaymentStatus.NoInvoice),
                Entries = entries
            };
        }

        /// <summary>
        /// `GET /api/fees/roster/pdf?year&month` — le suivi des mensualités en PDF
        /// (tableau façon Excel) à partager sur WhatsApp. SchoolAdmin + Staff.
        /// </summary>
        [HttpGet("roster/pdf")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> GetPaymentRosterPdf(
            [FromQuery] int year, [FromQuery] int month, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();
            if (month < 1 || month > 12 || year < 2020 || year > 2100)
                return BadRequest(ApiResponse<bool>.Fail("Mois ou année invalide."));

            var roster = await BuildRosterAsync(schoolId.Value, year, month, ct);
            var schoolName = await _context.Schools.Where(s => s.Id == schoolId.Value)
                .Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "Ecole";

            var bytes = _exportPdf.BuildRosterPdf(schoolName, year, month, roster);
            return File(bytes, "application/pdf", $"suivi-paiements-{year}-{month:D2}.pdf");
        }

        // ========================================================
        // ===== Dons reçus (côté école) : liste donateurs + rapport =====
        // ========================================================

        /// <summary>
        /// `GET /api/fees/donations/donors?from&to` — donateurs ayant fait un don
        /// (complété) au daara sur la période, avec cumul. SchoolAdmin + Staff.
        /// </summary>
        [HttpGet("donations/donors")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<List<DTOs.Donation.DonationDonorSummaryDto>>> GetDonationDonors(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var q = _context.Payments.Where(p => p.SchoolId == schoolId.Value
                && p.Purpose == PaymentPurpose.Donation
                && p.Status == PaymentStatus.Completed
                && p.DonorId != null);
            if (from.HasValue) q = q.Where(p => p.PaidAt >= from.Value.ToUtcDay());
            if (to.HasValue) q = q.Where(p => p.PaidAt < to.Value.ToUtcDay().AddDays(1));

            var donors = await q
                .GroupBy(p => new { p.DonorId, Name = p.Donor!.FullName })
                .Select(g => new DTOs.Donation.DonationDonorSummaryDto
                {
                    DonorId = g.Key.DonorId!.Value,
                    DonorName = g.Key.Name ?? "Donateur",
                    DonationCount = g.Count(),
                    TotalFcfa = g.Sum(p => p.TargetAmountFcfa)
                })
                .OrderByDescending(d => d.TotalFcfa)
                .ToListAsync(ct);

            return Ok(donors);
        }

        /// <summary>
        /// `GET /api/fees/donations/report/pdf?donorId&from&to` — rapport PDF des
        /// dons reçus d'UN donateur sur la période (dons datés + total). Sert au
        /// rapport annuel du daara à son donateur (ex. Nafsi wa dini).
        /// </summary>
        [HttpGet("donations/report/pdf")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> GetDonorReportPdf(
            [FromQuery] int donorId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var q = _context.Payments.Where(p => p.SchoolId == schoolId.Value
                && p.Purpose == PaymentPurpose.Donation
                && p.Status == PaymentStatus.Completed
                && p.DonorId == donorId);
            if (from.HasValue) q = q.Where(p => p.PaidAt >= from.Value.ToUtcDay());
            if (to.HasValue) q = q.Where(p => p.PaidAt < to.Value.ToUtcDay().AddDays(1));

            var rows = await q
                .OrderBy(p => p.PaidAt)
                .Select(p => new { p.PaidAt, p.TargetAmountFcfa })
                .ToListAsync(ct);

            var donorName = await _context.Users.Where(u => u.Id == donorId)
                .Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "Donateur";
            var schoolName = await _context.Schools.Where(s => s.Id == schoolId.Value)
                .Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "Ecole";

            var donations = rows
                .Select(r => (Date: r.PaidAt ?? DateTime.UtcNow, Amount: r.TargetAmountFcfa))
                .ToList();
            var total = donations.Sum(d => d.Amount);

            var bytes = _exportPdf.BuildDonorReportPdf(schoolName, donorName, from, to, donations, total);
            return File(bytes, "application/pdf", $"rapport-dons-{donorId}.pdf");
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
                Type = invoice.Type,
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
        // ===== Encaissement en ESPÈCES au guichet =====
        // ========================================================

        /// <summary>
        /// 💵 `POST /api/fees/invoices/{id}/cash` — la famille a payé en liquide.
        /// </summary>
        /// <remarks>
        /// <para>La plupart des inscriptions se règlent sur place, le jour où
        /// l'enfant arrive. Sans ce chemin, la facture restait impayée : l'élève
        /// était affiché « en retard », compté dans les impayés de l'accueil et
        /// <b>relancé par SMS alors que sa famille avait payé</b>.</para>
        /// <para>Ouvert au <b>personnel</b> autant qu'à la direction : c'est
        /// souvent lui qui tient l'accueil au moment de l'inscription. L'auteur de
        /// l'encaissement est tracé sur le paiement.</para>
        /// </remarks>
        [HttpPost("invoices/{id:int}/cash")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<ApiResponse<PaymentDto>>> CollectCash(
            int id, [FromBody] CollectCashDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var result = await _cashPayments.CollectAsync(
                schoolId.Value, id, dto.AmountFcfa, dto.Note,
                dto.OccurredAt?.ToUtcDay(), User.GetUserId(), ct);

            if (!result.Ok || result.Payment == null)
                return BadRequest(ApiResponse<PaymentDto>.Fail(result.Error ?? "Encaissement impossible."));

            // Effets POST-COMMIT, best-effort : ni un reçu ni un SMS ne doivent
            // faire échouer un encaissement déjà enregistré (§42/§57).
            await RunCashPaymentEffectsAsync(result.Payment, ct);

            return Ok(ApiResponse<PaymentDto>.Ok(
                await LoadPaymentDtoAsync(result.Payment.Id, ct),
                "Paiement en espèces enregistré."));
        }

        /// <summary>
        /// `POST /api/fees/payments/{id}/cash/cancel` — encaissement saisi par erreur.
        /// </summary>
        /// <remarks>
        /// Réservé au <b>SchoolAdmin</b> : encaisser fait partie du quotidien du
        /// personnel, défaire un mouvement d'argent est une décision de direction.
        /// Rien n'est supprimé — le paiement passe en « annulé », la facture est
        /// décréditée et l'écriture de caisse retirée.
        /// </remarks>
        [HttpPost("payments/{id:int}/cash/cancel")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<bool>>> CancelCash(
            int id, [FromBody] CancelCashDto? dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var result = await _cashPayments.CancelAsync(
                schoolId.Value, id, dto?.Reason, User.GetUserId(), ct);

            if (!result.Ok)
                return BadRequest(ApiResponse<bool>.Fail(result.Error ?? "Annulation impossible."));

            // SMS au responsable — post-commit, best-effort. Sans lui, la
            // famille recevait « Paiement recu » puis, si la direction annulait,
            // n'apprenait le retour de sa dette qu'au rappel de retard.
            if (result.Payment?.GuardianId is int guardianId)
            {
                try
                {
                    var guardian = await _context.Users
                        .FirstOrDefaultAsync(u => u.Id == guardianId && !u.IsDeleted, ct);
                    if (guardian?.PhoneNumber != null)
                    {
                        var student = await _context.Students
                            .Where(s => s.Id == result.Payment.StudentId)
                            .Select(s => new { s.FirstName, s.LastName })
                            .FirstOrDefaultAsync(ct);
                        var eleve = student == null
                            ? "votre enfant"
                            : $"{student.FirstName} {student.LastName}".Trim();

                        var platform = await _context.GetPlatformSettingsAsync(ct);
                        await _notif.SendSmsAsync(new NotificationSmsRequest(
                            UserId: guardian.Id,
                            RawPhone: guardian.PhoneNumber,
                            PreferredLanguage: guardian.PreferredLanguage ?? "fr",
                            Message: NotificationTemplates.CashPaymentCancelled(
                                eleve, result.Payment.AmountFcfa),
                            Bilingual: platform.SmsBilingual,
                            TemplateCode: "CASH_PAYMENT_CANCELLED",
                            RelatedEntityId: result.Payment.Id,
                            PushRoute: "/guardian/invoices",
                            SchoolId: schoolId,
                            TriggerSource: "api:fees/cash-cancel",
                            TriggerUserId: User.GetUserId()), ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[cash] SMS d'annulation non envoyé (paiement {Id}) — pas bloquant", id);
                }
            }

            return Ok(ApiResponse<bool>.Ok(true, "Encaissement annulé."));
        }

        /// <summary>Recharge un paiement pour le renvoyer à l'appelant.</summary>
        private async Task<PaymentDto> LoadPaymentDtoAsync(int paymentId, CancellationToken ct)
        {
            var p = await _context.Payments
                .Include(x => x.Student)
                .Include(x => x.Guardian)
                .FirstAsync(x => x.Id == paymentId, ct);

            return new PaymentDto
            {
                Id = p.Id,
                Reference = IdaraReference.Payment(p.Id),
                SchoolId = p.SchoolId,
                StudentId = p.StudentId,
                StudentFirstName = p.Student?.FirstName,
                StudentLastName = p.Student?.LastName,
                StudentNumber = p.Student?.StudentNumber,
                GuardianId = p.GuardianId,
                GuardianName = p.Guardian?.FullName,
                InvoiceId = p.InvoiceId,
                Purpose = p.Purpose,
                AmountFcfa = p.AmountFcfa,
                FeesFcfa = p.FeesFcfa,
                NetCreditedFcfa = p.NetCreditedFcfa,
                Operator = p.Operator,
                FeesPayer = p.FeesPayer,
                Status = p.Status,
                InitiatedAt = p.InitiatedAt,
                PaidAt = p.PaidAt,
                ReceiptPdfUrl = p.ReceiptPdfPath
            };
        }

        /// <summary>Reçu + information du responsable, aucun n'étant bloquant.</summary>
        private async Task RunCashPaymentEffectsAsync(Payment payment, CancellationToken ct)
        {
            try
            {
                var school = await _context.Schools
                    .FirstOrDefaultAsync(x => x.Id == payment.SchoolId, ct);
                var student = payment.StudentId is int sid
                    ? await _context.Students.FirstOrDefaultAsync(x => x.Id == sid, ct)
                    : null;
                var invoice = payment.InvoiceId is int iid
                    ? await _context.Invoices.FirstOrDefaultAsync(x => x.Id == iid, ct)
                    : null;
                if (school != null)
                {
                    var path = await _receiptPdf.GenerateAsync(
                        payment, school, student, invoice);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        await _context.Payments
                            .Where(p => p.Id == payment.Id)
                            .ExecuteUpdateAsync(
                                s => s.SetProperty(p => p.ReceiptPdfPath, path), ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[cash] Reçu non généré pour le paiement {Id} — pas bloquant", payment.Id);
            }

            if (payment.GuardianId is not int guardianId) return;
            try
            {
                var guardian = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == guardianId && !u.IsDeleted, ct);
                if (guardian?.PhoneNumber == null) return;

                var student = await _context.Students
                    .Where(s => s.Id == payment.StudentId)
                    .Select(s => new { s.FirstName, s.LastName })
                    .FirstOrDefaultAsync(ct);
                var eleve = student == null
                    ? "votre enfant"
                    : $"{student.FirstName} {student.LastName}".Trim();

                var platform = await _context.GetPlatformSettingsAsync(ct);
                await _notif.SendSmsAsync(new NotificationSmsRequest(
                    UserId: guardian.Id,
                    RawPhone: guardian.PhoneNumber,
                    PreferredLanguage: guardian.PreferredLanguage ?? "fr",
                    Message: NotificationTemplates.PaymentReceived(eleve, payment.AmountFcfa),
                    Bilingual: platform.SmsBilingual,
                    TemplateCode: "PAYMENT_RECEIVED",
                    RelatedEntityId: payment.Id,
                    PushRoute: "/guardian/invoices",
                    SchoolId: payment.SchoolId,
                    TriggerSource: "api:fees/cash-payment"), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[cash] SMS « paiement reçu » non envoyé pour {Id} — pas bloquant", payment.Id);
            }
        }

        // ========================================================
        // ===== Vue école : Wallet + transactions =====
        // ========================================================

        [HttpGet("wallet")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<object>> GetWallet(
            [FromQuery] string? q,
            [FromQuery] WalletSource? source,
            [FromQuery] string? txStatus,
            CancellationToken ct)
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

            // 50 lignes par défaut ; dès qu'une recherche est active on élargit,
            // sinon chercher dans « tout l'historique » ne ramènerait que les
            // correspondances des 50 dernières.
            var take = string.IsNullOrWhiteSpace(q) ? 50 : 300;
            var recentTx = await LoadWalletTransactionsAsync(
                schoolId.Value, null, null, take, ct, q, source, txStatus);

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

        /// <summary>
        /// Une ligne d'historique wallet, libellé et STATUT déjà résolus.
        ///
        /// <para><c>Status</c> ("Completed" / "Pending" / "Failed") est indispensable
        /// à l'affichage : sans lui, le client ne pouvait colorer une ligne que par
        /// le SENS du montant (débit = rouge), et un retrait RÉUSSI apparaissait en
        /// rouge — lu comme un échec par les écoles (retour terrain 2026-07-26).
        /// La couleur d'une transaction porte son STATUT, jamais son sens (gotcha §118).</para>
        /// </summary>
        /// <param name="Reference">
        /// Référence Idara de l'opération à l'origine du mouvement (« RET-000087 »,
        /// « PAY-000042 »). Remplace <c>Note</c> — un libellé interne du type
        /// « Réservation retrait #87 » — comme référence affichée : il ne permettait
        /// de retrouver l'opération nulle part.
        /// </param>
        /// <param name="ProviderReference">
        /// Référence du prestataire (décaissement SenePay, transaction SenePay).
        /// Null tant que le prestataire ne l'a pas renvoyée. C'est ELLE qui permet
        /// de rapprocher une ligne Idara d'une ligne du tableau de bord SenePay.
        /// </param>
        private sealed record WalletTxRow(
            int Id, WalletTransactionType Type, WalletSource Source, long AmountFcfa, long BalanceAfter,
            WalletRelatedEntity RelatedEntity, int? RelatedId, string? Note, DateTime OccurredAt, string? Label,
            string Status, string? Reference, string? ProviderReference);

        /// <summary>
        /// Charge l'historique wallet visible d'une école (récent d'abord), libellés
        /// résolus. SOURCE UNIQUE de l'onglet « Wallet » et de son export PDF : les
        /// deux affichent donc exactement les mêmes lignes et les mêmes noms.
        /// </summary>
        private async Task<List<WalletTxRow>> LoadWalletTransactionsAsync(
            int schoolId, DateTime? from, DateTime? to, int take, CancellationToken ct,
            string? search = null, WalletSource? source = null, string? status = null)
        {
            var query = _context.WalletTransactions
                .Where(t => t.SchoolId == schoolId)
                .Where(t => !t.IsHidden); // masquées par le daara → jamais affichées

            if (from.HasValue) query = query.Where(t => t.OccurredAt >= from.Value.ToUtcDay());
            if (to.HasValue) query = query.Where(t => t.OccurredAt < to.Value.ToUtcDay().AddDays(1));

            // Nature du mouvement (paiement, don, recharge, abonnement, retrait…).
            if (source.HasValue)
                query = query.Where(t => t.Source == source.Value);

            // Filtre STATUT traduit en SQL. Le statut d'une ligne wallet est dérivé
            // (cf. ResolveStatus) : seules les lignes de RETRAIT peuvent être autre
            // chose que « réussi », puisqu'elles sont écrites avant que l'opérateur
            // ne tranche. On le traduit ici en conditions SQL plutôt que de filtrer
            // en mémoire — sinon le `Take` s'appliquerait AVANT le filtre et la
            // page reviendrait à moitié vide.
            if (!string.IsNullOrWhiteSpace(status))
            {
                switch (status.Trim().ToLowerInvariant())
                {
                    case "completed":
                        query = query.Where(t => t.Source != WalletSource.Withdrawal
                            || _context.Withdrawals.Any(w => w.Id == t.RelatedId
                                && w.Status == WithdrawalStatus.Completed));
                        break;
                    case "pending":
                        query = query.Where(t => t.Source == WalletSource.Withdrawal
                            && _context.Withdrawals.Any(w => w.Id == t.RelatedId
                                && (w.Status == WithdrawalStatus.Initiated
                                    || w.Status == WithdrawalStatus.UnderVerification)));
                        break;
                    case "failed":
                        query = query.Where(t => t.Source == WalletSource.Withdrawal
                            && _context.Withdrawals.Any(w => w.Id == t.RelatedId
                                && (w.Status == WithdrawalStatus.Failed
                                    || w.Status == WithdrawalStatus.Cancelled)));
                        break;
                }
            }

            // Recherche libre. Le libellé affiché (nom de l'élève, du donateur, du
            // bénéficiaire) est résolu APRÈS la requête : chercher dessus en
            // mémoire ne ramènerait que ce qui est déjà chargé. On interroge donc
            // les entités liées en SQL, pour couvrir TOUT l'historique.
            if (TransactionSearch.Pattern(search) is string pattern)
            {
                query = query.Where(t =>
                    (t.Note != null && EF.Functions.ILike(t.Note, pattern))
                    || _context.Payments.Any(p => p.Id == t.RelatedId
                        && (t.Source == WalletSource.Payment || t.Source == WalletSource.Donation
                            || t.Source == WalletSource.Topup)
                        && ((p.Student != null && (EF.Functions.ILike(p.Student!.FirstName, pattern)
                                                || EF.Functions.ILike(p.Student!.LastName, pattern)))
                            || (p.Guardian != null && EF.Functions.ILike(p.Guardian!.FullName!, pattern))
                            || (p.Donor != null && EF.Functions.ILike(p.Donor!.FullName!, pattern))
                            || (p.SenePayTransactionId != null
                                && EF.Functions.ILike(p.SenePayTransactionId, pattern))))
                    || _context.Withdrawals.Any(w => w.Id == t.RelatedId
                        && t.Source == WalletSource.Withdrawal
                        && (EF.Functions.ILike(w.RecipientName, pattern)
                            || EF.Functions.ILike(w.RecipientPhone, pattern)
                            || (w.Motif != null && EF.Functions.ILike(w.Motif, pattern)))));
            }

            var recentRaw = await query
                .OrderByDescending(t => t.OccurredAt)
                .Take(take)
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

            // Libellés lisibles (read-time) : on résout les NOMS des entités liées
            // — paiement → élève/payeur, don → donateur, retrait → bénéficiaire —
            // pour ne plus afficher la référence technique SenePay comme titre
            // (cf. §2d). La réf reste dans `Note` (visible dans les détails). Ceci
            // nettoie l'historique EXISTANT sans migration (le libellé est calculé
            // à la lecture). Les catégories génériques (recharge, abonnement…) sont
            // laissées à null → traduites côté client selon `Source`.
            // La RECHARGE est incluse ici (en plus du paiement et du don) : c'est
            // aussi une ligne de `Payments`, donc elle a une référence prestataire
            // à afficher. Elle n'a simplement pas de nom à résoudre.
            var payIds = recentRaw
                .Where(t => t.Source is WalletSource.Payment or WalletSource.Donation or WalletSource.Topup)
                .Where(t => t.RelatedId != null).Select(t => t.RelatedId!.Value).Distinct().ToList();
            var payNames = await _context.Payments
                .Where(p => payIds.Contains(p.Id))
                .Select(p => new
                {
                    p.Id,
                    Student = p.Student != null ? (p.Student.FirstName + " " + p.Student.LastName) : null,
                    Guardian = p.Guardian != null ? p.Guardian.FullName : null,
                    Donor = p.Donor != null ? p.Donor.FullName : null,
                    p.SenePayTransactionId
                })
                .ToDictionaryAsync(p => p.Id, ct);

            var wIds = recentRaw
                .Where(t => t.Source == WalletSource.Withdrawal)
                .Where(t => t.RelatedId != null).Select(t => t.RelatedId!.Value).Distinct().ToList();
            var wNames = await _context.Withdrawals
                .Where(w => wIds.Contains(w.Id))
                .Select(w => new { w.Id, w.RecipientName, w.Status, w.SenePayDisbursementId })
                .ToDictionaryAsync(w => w.Id, ct);

            string? ResolveLabel(WalletSource source, int? relatedId)
            {
                switch (source)
                {
                    case WalletSource.Payment:
                        if (relatedId != null && payNames.TryGetValue(relatedId.Value, out var pp))
                            return !string.IsNullOrWhiteSpace(pp.Student) ? pp.Student!.Trim()
                                 : !string.IsNullOrWhiteSpace(pp.Guardian) ? pp.Guardian
                                 : null;
                        return null;
                    case WalletSource.Donation:
                        if (relatedId != null && payNames.TryGetValue(relatedId.Value, out var dp)
                            && !string.IsNullOrWhiteSpace(dp.Donor))
                            return dp.Donor;
                        return null;
                    case WalletSource.Withdrawal:
                        if (relatedId != null && wNames.TryGetValue(relatedId.Value, out var w)
                            && !string.IsNullOrWhiteSpace(w.RecipientName))
                            return w.RecipientName;
                        return null;
                    default:
                        return null; // Topup / Subscription / Adjustment / Other → i18n client
                }
            }

            // Statut de la ligne (gotcha §118). Une écriture wallet est append-only :
            // elle n'existe QUE si l'opération a abouti — sauf pour un retrait, dont
            // la réservation est posée AVANT que l'opérateur ne tranche. Une ligne de
            // retrait porte donc le statut du retrait lié (en cours / réussi / échoué),
            // tout le reste est réussi par construction.
            string ResolveStatus(WalletSource source, int? relatedId)
            {
                if (source != WalletSource.Withdrawal) return "Completed";
                // Retrait introuvable (purgé / donnée ancienne) : l'écriture existe,
                // on ne l'affiche pas comme un échec.
                if (relatedId == null || !wNames.TryGetValue(relatedId.Value, out var w))
                    return "Completed";
                return w.Status switch
                {
                    Enums.WithdrawalStatus.Completed => "Completed",
                    Enums.WithdrawalStatus.Initiated => "Pending",
                    Enums.WithdrawalStatus.UnderVerification => "Pending",
                    _ => "Failed" // Failed / Cancelled : la restitution suit (ligne Release)
                };
            }

            // Références affichées (§120bis) : celle d'Idara identifie la ligne dans
            // NOTRE base, celle du prestataire la rapproche du tableau de bord
            // SenePay. Les deux ensemble suppriment l'aller-retour de la
            // réconciliation. Une ligne sans opération rattachée (ajustement manuel,
            // prélèvement d'abonnement) n'a pas de référence : on n'en invente pas.
            (string? Reference, string? Provider) ResolveReferences(WalletSource source, int? relatedId)
            {
                if (relatedId == null) return (null, null);
                switch (source)
                {
                    case WalletSource.Payment:
                    case WalletSource.Donation:
                    case WalletSource.Topup:
                        return payNames.TryGetValue(relatedId.Value, out var p)
                            ? (IdaraReference.Payment(relatedId.Value), p.SenePayTransactionId)
                            : (null, null);
                    case WalletSource.Withdrawal:
                        return wNames.TryGetValue(relatedId.Value, out var w)
                            ? (IdaraReference.Withdrawal(relatedId.Value), w.SenePayDisbursementId)
                            : (null, null);
                    default:
                        return (null, null);
                }
            }

            return recentRaw.Select(t =>
            {
                var (reference, provider) = ResolveReferences(t.Source, t.RelatedId);
                return new WalletTxRow(
                    t.Id, t.Type, t.Source, t.AmountFcfa, t.BalanceAfter,
                    t.RelatedEntity, t.RelatedId, t.Note, t.OccurredAt,
                    ResolveLabel(t.Source, t.RelatedId),
                    ResolveStatus(t.Source, t.RelatedId),
                    reference, provider);
            }).ToList();
        }

        /// <summary>
        /// `GET /api/fees/wallet/transactions/pdf?from&to` — l'historique des
        /// mouvements du wallet en PDF (tableau), à partager/archiver. Mêmes
        /// lignes que l'onglet « Wallet », sur la période choisie.
        /// </summary>
        [HttpGet("wallet/transactions/pdf")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> GetWalletTransactionsPdf(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to,
            [FromQuery] string? q,
            [FromQuery] WalletSource? source,
            [FromQuery] string? txStatus,
            CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var txs = await LoadWalletTransactionsAsync(
                schoolId.Value, from, to, FinanceLabels.MaxExportRows, ct, q, source, txStatus);

            var rows = txs.Select(t => new TransactionPdfRow
            {
                Date = t.OccurredAt,
                Title = FinanceLabels.WalletSource(t.Source),
                Subtitle = t.Label,
                // Le type n'est précisé que quand il ne va pas de soi (une
                // réservation ou une restitution, sinon c'est juste un crédit/débit).
                Method = t.Type is WalletTransactionType.Credit or WalletTransactionType.Debit
                    ? null
                    : FinanceLabels.WalletTransactionType(t.Type),
                // La référence du prestataire d'abord (c'est elle qu'on rapproche
                // du tableau de bord SenePay), à défaut la nôtre. `Note` est un
                // libellé interne (« Réservation retrait #87 ») : il n'a jamais eu
                // sa place sous l'étiquette « Référence ».
                Reference = t.ProviderReference ?? t.Reference,
                // Chaque information dans SA colonne (§120) : le statut réel d'un
                // côté, le solde après opération de l'autre. Le PDF dit exactement
                // ce que dit l'écran (§116 + §118).
                Status = t.Status switch
                {
                    "Pending" => "En cours",
                    "Failed" => "Échoué",
                    _ => "Réussi"
                },
                Balance = $"{t.BalanceAfter:N0}",
                AmountFcfa = t.AmountFcfa
            }).ToList();

            var schoolName = await GetSchoolNameAsync(schoolId.Value, ct);
            var bytes = _exportPdf.BuildTransactionsPdf(
                schoolName,
                FinanceLabels.ExportTitle("Historique du wallet", rows.Count),
                from, to, rows, BuildFlowSummary(rows),
                // SEUL export mixte (paiements, dons, retraits, abonnement...) :
                // l'intitule reste generique, la colonne « Type » indiquant s'il
                // s'agit d'un payeur ou d'un beneficiaire (§120).
                counterpartyHeader: "Contrepartie");

            return File(bytes, "application/pdf", "historique-wallet-idara.pdf");
        }

        /// <summary>
        /// `GET /api/fees/payments/pdf?from&to&status` — l'historique des paiements
        /// reçus (parents, dons, recharges) en PDF. Reprend EXACTEMENT le filtrage
        /// de l'onglet « Paiements » (échecs masqués, tentatives regroupées).
        /// </summary>
        [HttpGet("payments/pdf")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> GetSchoolPaymentsPdf(
            [FromQuery] PaymentStatus? status,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? q,
            [FromQuery] PaymentPurpose? purpose,
            CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var payments = await LoadSchoolPaymentsAsync(schoolId.Value, status, from, to,
                FinanceLabels.MaxExportRows, ct, q, purpose);

            var rows = payments.Select(p => new TransactionPdfRow
            {
                Date = p.PaidAt ?? p.InitiatedAt,
                Title = FinanceLabels.PaymentPurpose(p.Purpose),
                Subtitle = PayerLabel(p),
                Method = FinanceLabels.Operator(p.Operator),
                Reference = p.SenePayTransactionId,
                Status = FinanceLabels.PaymentStatus(p.Status),
                // Vu du daara, un paiement reçu est une ENTRÉE : on liste ce que la
                // famille a réglé (le montant cible), pas le net après frais SenePay.
                AmountFcfa = p.TargetAmountFcfa > 0 ? p.TargetAmountFcfa : p.AmountFcfa
            }).ToList();

            var completed = payments
                .Where(p => p.Status == PaymentStatus.Completed)
                .Sum(p => p.TargetAmountFcfa > 0 ? p.TargetAmountFcfa : p.AmountFcfa);
            var summary = new List<(string, string, bool)>
            {
                ("Total encaisse", $"{completed:N0}", false)
            };

            var schoolName = await GetSchoolNameAsync(schoolId.Value, ct);
            var bytes = _exportPdf.BuildTransactionsPdf(
                schoolName,
                FinanceLabels.ExportTitle("Historique des paiements recus", rows.Count),
                from, to, rows, summary,
                // Toujours de l'argent entrant : la contrepartie est celui qui paie.
                counterpartyHeader: "Payeur");

            return File(bytes, "application/pdf", "historique-paiements-idara.pdf");
        }

        /// <summary>Qui a payé : élève (+ classe) pour une mensualité, donateur pour un don.</summary>
        private static string? PayerLabel(Payment p)
        {
            if (p.Purpose == PaymentPurpose.Donation)
                return p.Donor?.FullName;
            var student = $"{p.Student?.FirstName} {p.Student?.LastName}".Trim();
            if (!string.IsNullOrWhiteSpace(student))
                return string.IsNullOrWhiteSpace(p.Student?.Class?.Name)
                    ? student
                    : $"{student} - {p.Student!.Class!.Name}";
            return p.Guardian?.FullName;
        }

        /// <summary>Nom de l'école (repli neutre) pour l'en-tête des documents.</summary>
        private async Task<string> GetSchoolNameAsync(int schoolId, CancellationToken ct) =>
            await _context.Schools.Where(s => s.Id == schoolId)
                .Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "Daara";

        /// <summary>Bandeau Entrées / Sorties / Net d'une liste de mouvements signés.</summary>
        private static List<(string Label, string Value, bool Danger)> BuildFlowSummary(
            IReadOnlyList<TransactionPdfRow> rows)
        {
            var credits = rows.Where(r => r.AmountFcfa > 0).Sum(r => r.AmountFcfa);
            var debits = rows.Where(r => r.AmountFcfa < 0).Sum(r => -r.AmountFcfa);
            var net = credits - debits;
            return new List<(string, string, bool)>
            {
                ("Entrees", $"{credits:N0}", false),
                ("Sorties", $"{debits:N0}", true),
                ("Net", $"{net:N0}", net < 0)
            };
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
            [FromQuery] string? q,
            [FromQuery] PaymentPurpose? purpose,
            CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var payments = await LoadSchoolPaymentsAsync(
                schoolId.Value, status, from, to, 500, ct, q, purpose);

            return Ok(payments.Select(p => new PaymentDto
            {
                Id = p.Id,
                Reference = IdaraReference.Payment(p.Id),
                SchoolId = p.SchoolId,
                StudentId = p.StudentId,
                StudentFirstName = p.Student?.FirstName,
                StudentLastName = p.Student?.LastName,
                StudentNumber = p.Student?.StudentNumber,
                GuardianId = p.GuardianId,
                GuardianName = p.Guardian?.FullName,
                InvoiceId = p.InvoiceId,
                Purpose = p.Purpose,
                DonorId = p.DonorId,
                DonorName = p.Donor?.FullName,
                DonorType = p.Donor?.DonorType,
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
        /// Charge les paiements reçus d'une école avec le filtrage EXACT de la vue
        /// « Paiements » : masqués exclus, échecs cachés par défaut, tentatives
        /// multiples d'une même facture regroupées en une ligne. SOURCE UNIQUE de
        /// l'onglet et de son export PDF — les deux ne peuvent pas diverger.
        /// </summary>
        private async Task<List<Payment>> LoadSchoolPaymentsAsync(
            int schoolId, PaymentStatus? status, DateTime? from, DateTime? to,
            int take, CancellationToken ct,
            string? search = null, PaymentPurpose? purpose = null)
        {
            var query = _context.Payments
                .Include(p => p.Student).ThenInclude(s => s!.Class)
                .Include(p => p.Guardian)
                .Include(p => p.Donor)
                .Where(p => p.SchoolId == schoolId)
                .Where(p => !p.IsHidden); // masqués par le daara → jamais affichés (ni Observateur)

            // Nature (mensualité / recharge / don) — « toutes » par défaut.
            if (purpose.HasValue)
                query = query.Where(p => p.Purpose == purpose.Value);

            // Recherche libre : élève, payeur, donateur, référence SenePay.
            if (TransactionSearch.Pattern(search) is string pattern)
            {
                query = query.Where(p =>
                    (p.Student != null && (
                        EF.Functions.ILike(p.Student!.FirstName, pattern) ||
                        EF.Functions.ILike(p.Student!.LastName, pattern) ||
                        (p.Student.StudentNumber != null && EF.Functions.ILike(p.Student!.StudentNumber!, pattern))))
                    || (p.Guardian != null && EF.Functions.ILike(p.Guardian!.FullName!, pattern))
                    || (p.Donor != null && EF.Functions.ILike(p.Donor!.FullName!, pattern))
                    || (p.SenePayTransactionId != null && EF.Functions.ILike(p.SenePayTransactionId, pattern)));
            }

            if (status.HasValue)
                query = query.Where(p => p.Status == status.Value);
            else
                // Par défaut : on masque les paiements échoués / annulés / expirés
                // (bruit déroutant pour le public non-tech) — uniquement réussis +
                // en cours. Un statut explicite reste possible pour le debug.
                query = query.Where(p =>
                    p.Status == PaymentStatus.Completed || p.Status == PaymentStatus.Pending);

            // Bornes en JOUR CIVIL (le client envoie une date, pas un instant) :
            // `to` inclut donc toute sa journée.
            if (from.HasValue) query = query.Where(p => p.InitiatedAt >= from.Value.ToUtcDay());
            if (to.HasValue) query = query.Where(p => p.InitiatedAt < to.Value.ToUtcDay().AddDays(1));

            var items = await query
                .OrderByDescending(p => p.InitiatedAt)
                .Take(take)
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

            return items;
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
            DonationFeesPayer = s.DonationFeesPayer,
            MonthlyDueDay = s.MonthlyDueDay,
            PaymentDeadlineDay = s.PaymentDeadlineDay,
            BillingPeriod = s.BillingPeriod,
            GeneralMonthlyFeeFcfa = s.GeneralMonthlyFeeFcfa,
            BoardingMonthlyFeeFcfa = s.BoardingMonthlyFeeFcfa,
            HalfBoardingMonthlyFeeFcfa = s.HalfBoardingMonthlyFeeFcfa,
            DayMonthlyFeeFcfa = s.DayMonthlyFeeFcfa,
            RegistrationFeeFcfa = s.RegistrationFeeFcfa,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt
        };
    }
}
