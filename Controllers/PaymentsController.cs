using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Payment;
using Idara.API.DTOs.Senepay;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Endpoints de paiement parent → école. Authentifié Guardian uniquement
    /// pour Phase 1.4 (le topup wallet école SchoolAdmin viendra en Phase 4).
    /// </summary>
    [ApiController]
    [Route("api/payments")]
    [Authorize(Roles = UserRoles.Guardian)]
    public class PaymentsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ISenePayClient _senepay;
        private readonly SenePaySettings _senepaySettings;
        private readonly IReceiptPdfService _receiptPdf;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<PaymentsController> _logger;

        // Majoration parent (+8 %) et minimum de paiement (200 FCFA) ne sont
        // plus codés en dur : lus depuis PlatformSettings (éditable SuperAdmin).

        public PaymentsController(
            AppDbContext context,
            ISenePayClient senepay,
            IOptions<SenePaySettings> senepaySettings,
            IReceiptPdfService receiptPdf,
            IWebHostEnvironment env,
            ILogger<PaymentsController> logger)
        {
            _context = context;
            _senepay = senepay;
            _senepaySettings = senepaySettings.Value;
            _receiptPdf = receiptPdf;
            _env = env;
            _logger = logger;
        }

        /// <summary>
        /// `GET /api/payments/{id}/receipt` — télécharge le reçu PDF (parent
        /// uniquement, sur ses propres paiements complétés). Génération à la
        /// volée si le fichier n'existe pas encore (recovery si le webhook a
        /// échoué la génération automatique).
        /// </summary>
        [HttpGet("{id:int}/receipt")]
        public async Task<IActionResult> DownloadReceipt(int id, CancellationToken ct)
        {
            var guardianId = User.GetUserId()
                ?? throw new UnauthorizedAccessException("UserId missing from JWT");

            var p = await _context.Payments
                .Include(x => x.Student)
                .Include(x => x.InvoiceAllocations).ThenInclude(a => a.Invoice).ThenInclude(i => i.Student)
                .FirstOrDefaultAsync(x => x.Id == id && x.GuardianId == guardianId, ct);
            if (p == null) return NotFound(ApiResponse<bool>.Fail("Paiement introuvable."));
            if (p.Status != PaymentStatus.Completed)
            {
                return BadRequest(ApiResponse<bool>.Fail(
                    "Reçu disponible uniquement pour les paiements complétés."));
            }

            // Chemin attendu — déterministe par PaymentId (nom + suffixe HMAC).
            var relativePath = p.ReceiptPdfPath ?? _receiptPdf.RelativePathFor(p.Id);
            var fullPath = Path.GetFullPath(Path.Combine(
                _env.WebRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

            // Path traversal défense en profondeur (cf. gotcha §43) — au cas
            // où ReceiptPdfPath aurait été altéré en base.
            var webRootFull = Path.GetFullPath(_env.WebRootPath);
            if (!fullPath.StartsWith(webRootFull, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "[payment/receipt] Path traversal bloqué pour Payment.Id={Id} (path={Path})",
                    p.Id, relativePath);
                return BadRequest(ApiResponse<bool>.Fail("Chemin de reçu invalide."));
            }

            if (!System.IO.File.Exists(fullPath))
            {
                // Recovery : régénérer à la volée.
                var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == p.SchoolId, ct);
                var invoice = p.InvoiceId.HasValue
                    ? await _context.Invoices.FirstOrDefaultAsync(x => x.Id == p.InvoiceId.Value, ct)
                    : null;
                if (school == null) return NotFound();

                List<ReceiptConsolidatedLine>? consolidatedLines = p.InvoiceAllocations.Count > 0
                    ? p.InvoiceAllocations
                        .OrderBy(a => a.Invoice.Student.FirstName)
                        .Select(a => new ReceiptConsolidatedLine(
                            $"{a.Invoice.Student.FirstName} {a.Invoice.Student.LastName}".Trim(),
                            FrenchMonthYear(a.Invoice.PeriodStart),
                            a.AmountFcfa))
                        .ToList()
                    : null;
                var regenerated = await _receiptPdf.GenerateAsync(p, school, p.Student, invoice, null, consolidatedLines);
                if (string.IsNullOrEmpty(p.ReceiptPdfPath))
                {
                    await _context.Payments
                        .Where(x => x.Id == p.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReceiptPdfPath, regenerated), ct);
                }
                fullPath = Path.GetFullPath(Path.Combine(
                    _env.WebRootPath, regenerated.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath, ct);
            return File(bytes, "application/pdf", $"recu-idara-{p.Id:D6}.pdf");
        }

        /// <summary>
        /// `GET /api/payments/{id}` — détails / statut d'un paiement (poll
        /// côté Flutter en attendant le webhook SenePay). Le Guardian ne peut
        /// voir que ses propres paiements.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ApiResponse<PaymentDto>>> Get(int id, CancellationToken ct)
        {
            var guardianId = User.GetUserId()
                ?? throw new UnauthorizedAccessException("UserId missing from JWT");

            var p = await _context.Payments
                .Include(x => x.Student)
                .FirstOrDefaultAsync(x => x.Id == id && x.GuardianId == guardianId, ct);
            if (p == null)
            {
                return NotFound(ApiResponse<PaymentDto>.Fail("Paiement introuvable."));
            }

            return Ok(ApiResponse<PaymentDto>.Ok(new PaymentDto
            {
                Id = p.Id,
                Reference = IdaraReference.Payment(p.Id),
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
        /// `POST /api/payments/initiate` — initie un paiement **Wave** (unique
        /// moyen depuis 2026-07-07). Le numéro du parent est récupéré en base
        /// (identité par téléphone) : plus rien à saisir. Corps utile :
        /// { studentId, invoiceId? ou amount }.
        /// </summary>
        [HttpPost("initiate")]
        public async Task<ActionResult<ApiResponse<InitiatePaymentResponseDto>>> Initiate(
            [FromBody] InitiatePaymentRequestDto dto,
            CancellationToken ct)
        {
            var guardianId = User.GetUserId()
                ?? throw new UnauthorizedAccessException("UserId missing from JWT");

            return await InitiateNewPaymentAsync(guardianId, dto, ct);
        }

        // ====================================================================
        // ===== Création Payment + SenePay initiate =====
        // ====================================================================

        private async Task<ActionResult<ApiResponse<InitiatePaymentResponseDto>>> InitiateNewPaymentAsync(
            int guardianId,
            InitiatePaymentRequestDto dto,
            CancellationToken ct)
        {
            if (dto.StudentId is not int studentId)
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "StudentId est requis pour le 1er appel."));
            }

            // Validation : l'élève appartient bien à ce guardian (multi-tenant strict).
            var link = await _context.StudentGuardians
                .Include(sg => sg.Student)
                .FirstOrDefaultAsync(sg => sg.StudentId == studentId && sg.GuardianId == guardianId, ct);
            if (link == null || link.Student.IsDeleted)
            {
                _logger.LogWarning(
                    "[payment/initiate] Guardian {GuardianId} a tenté de payer pour Student {StudentId} non lié ou supprimé",
                    guardianId, studentId);
                return NotFound(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "Élève introuvable ou non lié à votre compte."));
            }

            var student = link.Student;
            var schoolId = student.SchoolId;

            // Garantit que l'école a ses fondations (settings + wallet) avant
            // tout paiement — sinon le crédit webhook throwerait faute de wallet
            // sur une école créée entre deux redémarrages (cf. seed DbInitializer).
            await _context.EnsurePaymentFoundationsAsync(schoolId, ct);

            // Réglages globaux (min paiement + majoration parent), éditables SuperAdmin.
            var platform = await _context.GetPlatformSettingsAsync(ct);

            // Récupère la config paiement de l'école.
            var settings = await _context.SchoolPaymentSettings
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);
            if (settings == null)
            {
                _logger.LogError(
                    "[payment/initiate] SchoolPaymentSettings manquant pour SchoolId={SchoolId} (devrait être seedé)",
                    schoolId);
                return StatusCode(500, ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "Configuration de paiement de l'école introuvable. Contactez le support."));
            }

            // Détermine le montant cible (ce que reçoit l'école avant majoration parent).
            long targetAmount;
            Invoice? invoice = null;

            if (dto.InvoiceId.HasValue)
            {
                invoice = await _context.Invoices
                    .FirstOrDefaultAsync(i => i.Id == dto.InvoiceId.Value, ct);
                if (invoice == null || invoice.StudentId != studentId)
                {
                    return NotFound(ApiResponse<InitiatePaymentResponseDto>.Fail(
                        "Facture introuvable ou n'appartient pas à cet élève."));
                }
                if (invoice.Status == InvoiceStatus.Paid)
                {
                    return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                        "Cette facture est déjà payée."));
                }
                if (invoice.Status == InvoiceStatus.Cancelled)
                {
                    return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                        "Cette facture a été annulée."));
                }
                targetAmount = invoice.AmountDueFcfa - invoice.AmountPaidFcfa;
            }
            else if (dto.Amount.HasValue && dto.Amount.Value > 0)
            {
                if (settings.BillingMode == BillingMode.FixedAmount)
                {
                    return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                        "L'école fonctionne en montant fixe — vous devez payer une facture (InvoiceId requis), pas un montant libre."));
                }
                targetAmount = dto.Amount.Value;
            }
            else
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "InvoiceId ou Amount doit être fourni."));
            }

            if (targetAmount < platform.MinPayinFcfa)
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    $"Le montant minimum est de {platform.MinPayinFcfa} FCFA."));
            }

            // Majoration parent : si FeesPayer=Parent, on charge targetAmount × 1.08
            // (le parent porte les frais SenePay+opérateurs). Si FeesPayer=School,
            // on charge targetAmount tel quel (l'école absorbe les frais au net).
            long amountToCharge = settings.FeesPayer == FeesPayer.Parent
                ? (long)Math.Ceiling(targetAmount * platform.ParentFeeMultiplier)
                : targetAmount;

            var operatorEnum = PaymentOperator.Wave; // Wave uniquement (2026-07-07)

            // Création Payment AVANT l'appel SenePay : le webhook peut arriver
            // entre l'appel et notre SaveChanges si l'opérateur est très rapide.
            //
            // PublicResultToken : GUID opaque utilisé dans la page HTML de
            // résultat (`/pay/{id}/{token}`) — empêche un attaquant qui
            // énumère les PaymentId de voir les reçus des autres parents.
            // TargetAmountFcfa : montant que l'invoice doit considérer comme
            // payé quand le webhook confirme (l'amountToCharge inclut la +8%
            // de majoration, qui est censée couvrir les frais SenePay).
            var payment = new Payment
            {
                SchoolId = schoolId,
                StudentId = studentId,
                GuardianId = guardianId,
                InvoiceId = invoice?.Id,
                AmountFcfa = amountToCharge,
                TargetAmountFcfa = targetAmount,
                FeesFcfa = 0, // rempli au webhook
                NetCreditedFcfa = 0, // rempli au webhook
                Operator = operatorEnum,
                FeesPayer = settings.FeesPayer,
                Status = PaymentStatus.Pending,
                InitiatedAt = DateTime.UtcNow,
                PublicResultToken = GeneratePublicToken()
            };
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync(ct);

            return await FinalizeSenePayInitiateAsync(payment, ct);
        }

        // ====================================================================
        // ===== Paiement CONSOLIDÉ (« paiement global » d'un parent) =====
        // ====================================================================

        /// <summary>
        /// `POST /api/payments/initiate-consolidated` — règle en UNE fois le
        /// total des mensualités dues de TOUS les enfants du parent dans une
        /// école (mode FixedAmount). Le backend recalcule lui-même l'ensemble
        /// des factures impayées (source de vérité, anti-falsification).
        /// </summary>
        [HttpPost("initiate-consolidated")]
        public async Task<ActionResult<ApiResponse<InitiatePaymentResponseDto>>> InitiateConsolidated(
            [FromBody] InitiateConsolidatedPaymentRequestDto dto,
            CancellationToken ct)
        {
            var guardianId = User.GetUserId()
                ?? throw new UnauthorizedAccessException("UserId missing from JWT");

            if (dto.SchoolId is not int schoolId)
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail("SchoolId est requis."));
            }

            await _context.EnsurePaymentFoundationsAsync(schoolId, ct);
            var platform = await _context.GetPlatformSettingsAsync(ct);

            var settings = await _context.SchoolPaymentSettings
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);
            if (settings == null)
            {
                _logger.LogError(
                    "[payment/consolidated] SchoolPaymentSettings manquant pour SchoolId={SchoolId}", schoolId);
                return StatusCode(500, ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "Configuration de paiement de l'école introuvable. Contactez le support."));
            }
            if (settings.BillingMode != BillingMode.FixedAmount)
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "Le paiement global n'est disponible que pour les écoles en montant fixe."));
            }

            // Toutes les factures impayables du parent DANS cette école : enfants
            // liés (non supprimés), factures Pending/Overdue, reste dû > 0.
            // Le lien StudentGuardian garantit le cloisonnement multi-tenant.
            var outstanding = await _context.Invoices
                .Where(i => i.SchoolId == schoolId
                            && (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Overdue)
                            && i.AmountDueFcfa - i.AmountPaidFcfa > 0
                            && _context.StudentGuardians.Any(sg =>
                                sg.StudentId == i.StudentId
                                && sg.GuardianId == guardianId
                                && !sg.Student.IsDeleted))
                .Select(i => new { i.Id, Remaining = i.AmountDueFcfa - i.AmountPaidFcfa })
                .ToListAsync(ct);

            if (outstanding.Count == 0)
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "Aucune mensualité à payer pour vos enfants dans cette école."));
            }

            var targetAmount = outstanding.Sum(o => o.Remaining);
            if (targetAmount < platform.MinPayinFcfa)
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    $"Le montant minimum est de {platform.MinPayinFcfa} FCFA."));
            }

            long amountToCharge = settings.FeesPayer == FeesPayer.Parent
                ? (long)Math.Ceiling(targetAmount * platform.ParentFeeMultiplier)
                : targetAmount;

            var operatorEnum = PaymentOperator.Wave; // Wave uniquement (2026-07-07)

            var payment = new Payment
            {
                SchoolId = schoolId,
                StudentId = null,            // consolidé → pas d'élève unique
                GuardianId = guardianId,
                InvoiceId = null,            // consolidé → factures portées par les allocations
                Purpose = PaymentPurpose.SchoolFee,
                AmountFcfa = amountToCharge,
                TargetAmountFcfa = targetAmount,
                FeesFcfa = 0,
                NetCreditedFcfa = 0,
                Operator = operatorEnum,
                FeesPayer = settings.FeesPayer,
                Status = PaymentStatus.Pending,
                InitiatedAt = DateTime.UtcNow,
                PublicResultToken = GeneratePublicToken(),
                InvoiceAllocations = outstanding
                    .Select(o => new PaymentInvoiceAllocation { InvoiceId = o.Id, AmountFcfa = o.Remaining })
                    .ToList()
            };
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync(ct);

            return await FinalizeSenePayInitiateAsync(payment, ct);
        }

        /// <summary>
        /// Tronc commun aux deux flows d'initiation (unitaire + consolidé) :
        /// appelle SenePay, stocke les identifiants sur le Payment déjà persisté,
        /// gère l'échec synchrone, et renvoie la réponse client. Le Payment est
        /// créé AVANT (anti-race webhook) par l'appelant.
        /// </summary>
        private async Task<ActionResult<ApiResponse<InitiatePaymentResponseDto>>> FinalizeSenePayInitiateAsync(
            Payment payment, CancellationToken ct)
        {
            // Numéro du parent récupéré en base (identité par téléphone) — plus
            // aucune saisie côté client (refonte UX 2026-07-07).
            var payerPhone = payment.GuardianId is int gid
                ? await _context.Users.Where(u => u.Id == gid).Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct)
                : null;
            if (string.IsNullOrWhiteSpace(payerPhone))
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "Aucun numéro de téléphone n'est associé à votre compte. Contactez votre école."));
            }

            SenePayInitiatePaymentResponse senepayResponse;
            try
            {
                senepayResponse = await _senepay.InitiatePaymentAsync(
                    BuildSenePayRequest(payment, payerPhone, customerName: GuardianName()), ct);
            }
            catch (SenePayApiException ex)
            {
                _logger.LogError(ex,
                    "[payment/initiate] SenePay error pour Payment {PaymentId}", payment.Id);
                return StatusCode(502, ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "SenePay temporairement indisponible. Réessayez dans quelques secondes."));
            }

            // Stocke les IDs SenePay sur le Payment (cf. commentaire d'origine :
            // token court pour GET /status, internalId pour rapprochement).
            payment.SenePayInternalId = senepayResponse.InternalId;
            payment.SenePayTransactionId = senepayResponse.Token;
            await _context.SaveChangesAsync(ct);

            if (string.Equals(senepayResponse.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(senepayResponse.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                payment.Status = string.Equals(senepayResponse.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                    ? PaymentStatus.Cancelled
                    : PaymentStatus.Failed;
                payment.FailedAt = DateTime.UtcNow;
                payment.FailureReason = senepayResponse.FailedReason ?? senepayResponse.ErrorCode;
                await _context.SaveChangesAsync(ct);
            }

            return Ok(ApiResponse<InitiatePaymentResponseDto>.Ok(new InitiatePaymentResponseDto
            {
                PaymentId = payment.Id,
                Status = senepayResponse.Status ?? "Pending",
                NextAction = senepayResponse.NextAction ?? "NONE",
                RedirectUrl = senepayResponse.RedirectUrl,
                OtpRequired = senepayResponse.OtpRequired,
                ErrorCode = senepayResponse.ErrorCode,
                FailureReason = senepayResponse.FailedReason,
                AmountChargedFcfa = payment.AmountFcfa
            }));
        }

        // ====================================================================
        // ===== Helpers =====
        // ====================================================================

        private SenePayInitiatePaymentRequest BuildSenePayRequest(
            Payment payment,
            string payerPhone,
            string? customerName)
        {
            // URLs page HTML résultat (servies par PaymentPublicController).
            // Ne PAS oublier le token : sinon la page accepte n'importe quel
            // PaymentId énuméré.
            var publicBase = _senepaySettings.PublicBaseUrl.TrimEnd('/');
            var resultBase = $"{publicBase}/pay/{payment.Id}/{payment.PublicResultToken}";

            return new SenePayInitiatePaymentRequest
            {
                Amount = payment.AmountFcfa,
                Currency = "XOF",
                CountryCode = "SN",
                Operator = "wave", // Wave uniquement (2026-07-07)
                CustomerPhone = PaymentPhone.ForSenePay(payerPhone),
                OtpCode = null,
                OrderId = payment.Id.ToString(),
                CustomerName = customerName,
                WebhookUrl = _senepaySettings.WebhookPayinUrl,
                ReturnUrl = $"{resultBase}?status=success",
                CancelUrl = $"{resultBase}?status=cancel"
            };
        }

        /// <summary>
        /// Génère un token public opaque pour la page HTML de résultat.
        /// Format compact (GUID sans tirets, 32 chars hexa) : court mais
        /// imprévisible (128 bits d'entropie, GUID v4).
        /// </summary>
        private static string GeneratePublicToken() =>
            Guid.NewGuid().ToString("N");

        private static readonly string[] FrMonths =
        {
            "janvier", "fevrier", "mars", "avril", "mai", "juin",
            "juillet", "aout", "septembre", "octobre", "novembre", "decembre"
        };

        private static string FrenchMonthYear(DateTime d) => $"{FrMonths[d.Month - 1]} {d.Year}";

        private string? GuardianName()
        {
            // Approximation : on utilise l'email. La création d'un User dans
            // Idara ne capture pas systématiquement un FirstName/LastName ; le
            // CustomerName SenePay est informatif (affiché sur les reçus PSP).
            return User.GetEmail();
        }
    }
}
