using Idara.API.Common.Extensions;
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
        private readonly ILogger<PaymentsController> _logger;

        // Majoration parent fixée à +8 % (cf. spec §3.6 — couvre 5,37 % payin
        // + 1,77 % payout + coussin ~20 FCFA/paiement pour fluctuations SenePay).
        private const double ParentFeeMultiplier = 1.08;

        // Minimum SenePay (cf. doc §1 ligne 581).
        private const long MinAmountFcfa = 200;

        public PaymentsController(
            AppDbContext context,
            ISenePayClient senepay,
            IOptions<SenePaySettings> senepaySettings,
            ILogger<PaymentsController> logger)
        {
            _context = context;
            _senepay = senepay;
            _senepaySettings = senepaySettings.Value;
            _logger = logger;
        }

        /// <summary>
        /// `POST /api/payments/initiate` — initie un paiement Wave ou Orange.
        /// Deux flows :
        /// - 1er appel : sans PaymentId, avec StudentId + (InvoiceId ou Amount).
        ///   Crée un Payment.Status=Pending, appelle SenePay, met à jour le
        ///   Payment avec token/internalId, retourne le nextAction.
        /// - 2e appel Orange OTP : avec PaymentId + OtpCode, sans StudentId/...
        ///   Récupère le Payment existant, rappelle SenePay avec l'OTP.
        /// </summary>
        [HttpPost("initiate")]
        public async Task<ActionResult<ApiResponse<InitiatePaymentResponseDto>>> Initiate(
            [FromBody] InitiatePaymentRequestDto dto,
            CancellationToken ct)
        {
            var guardianId = User.GetUserId()
                ?? throw new UnauthorizedAccessException("UserId missing from JWT");

            // Branche 2e appel OTP : PaymentId + OtpCode fournis.
            if (dto.PaymentId.HasValue)
            {
                if (string.IsNullOrWhiteSpace(dto.OtpCode))
                {
                    return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                        "OtpCode est requis quand PaymentId est fourni (2e appel Orange)."));
                }
                return await ResubmitWithOtpAsync(guardianId, dto, ct);
            }

            // Branche 1er appel : création de Payment.
            return await InitiateNewPaymentAsync(guardianId, dto, ct);
        }

        // ====================================================================
        // ===== 1er appel : création Payment + SenePay initiate =====
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

            if (targetAmount < MinAmountFcfa)
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    $"Le montant minimum est de {MinAmountFcfa} FCFA."));
            }

            // Majoration parent : si FeesPayer=Parent, on charge targetAmount × 1.08
            // (le parent porte les frais SenePay+opérateurs). Si FeesPayer=School,
            // on charge targetAmount tel quel (l'école absorbe les frais au net).
            long amountToCharge = settings.FeesPayer == FeesPayer.Parent
                ? (long)Math.Ceiling(targetAmount * ParentFeeMultiplier)
                : targetAmount;

            var operatorEnum = ParseOperator(dto.Operator);

            // Création Payment AVANT l'appel SenePay : le webhook peut arriver
            // entre l'appel et notre SaveChanges si l'opérateur est très rapide.
            var payment = new Payment
            {
                SchoolId = schoolId,
                StudentId = studentId,
                GuardianId = guardianId,
                InvoiceId = invoice?.Id,
                AmountFcfa = amountToCharge,
                FeesFcfa = 0, // rempli au webhook
                NetCreditedFcfa = 0, // rempli au webhook
                Operator = operatorEnum,
                FeesPayer = settings.FeesPayer,
                Status = PaymentStatus.Pending,
                InitiatedAt = DateTime.UtcNow
            };
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync(ct);

            // Appel SenePay.
            SenePayInitiatePaymentResponse senepayResponse;
            try
            {
                senepayResponse = await _senepay.InitiatePaymentAsync(BuildSenePayRequest(
                    payment, dto.Operator, dto.CustomerPhone, otpCode: null, customerName: GuardianName()), ct);
            }
            catch (SenePayApiException ex)
            {
                _logger.LogError(ex,
                    "[payment/initiate] SenePay error pour Payment {PaymentId}",
                    payment.Id);
                // Garder le Payment en Pending — un retry ultérieur via le même
                // endpoint créera un nouveau Payment. Ce vieux Payment Pending
                // expirera fonctionnellement (timeout UI = 90s côté Flutter).
                return StatusCode(502, ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "SenePay temporairement indisponible. Réessayez dans quelques secondes."));
            }

            // Stocke les IDs SenePay sur le Payment.
            payment.SenePayInternalId = senepayResponse.InternalId;
            // ATTENTION : token = SenePay payin token (afp_tx_...) — c'est ce
            // qu'on stocke dans SenePayTransactionId (clé d'idempotence webhook).
            // Le webhook nous renverra ce même token dans payload.transactionId
            // (préfixé SENEPAY_PAYIN_xxx pour Direct API — différent du token afp_tx_).
            // → On stocke les DEUX : token court (pour GET /status), internalId
            // (pour rapprochement). Le webhook utilisera transactionId qu'on
            // matchera via SenePayTransactionId ou via OrderReference=Payment.Id.
            payment.SenePayTransactionId = senepayResponse.Token;
            await _context.SaveChangesAsync(ct);

            // Si SenePay a déjà tranché Failed sur la 1ère réponse (ex: numéro
            // invalide), on met à jour le Payment immédiatement — pas besoin
            // d'attendre un webhook.
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
                AmountChargedFcfa = amountToCharge
            }));
        }

        // ====================================================================
        // ===== 2e appel : Orange OTP =====
        // ====================================================================

        private async Task<ActionResult<ApiResponse<InitiatePaymentResponseDto>>> ResubmitWithOtpAsync(
            int guardianId,
            InitiatePaymentRequestDto dto,
            CancellationToken ct)
        {
            var paymentId = dto.PaymentId!.Value;

            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.Id == paymentId, ct);
            if (payment == null)
            {
                return NotFound(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "Paiement introuvable."));
            }
            if (payment.GuardianId != guardianId)
            {
                _logger.LogWarning(
                    "[payment/initiate] Guardian {GuardianId} a tenté de soumettre OTP pour Payment {PaymentId} appartenant à un autre guardian",
                    guardianId, paymentId);
                return Forbid();
            }
            if (payment.Status != PaymentStatus.Pending)
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    $"Ce paiement est déjà en statut {payment.Status}, impossible de soumettre un OTP."));
            }
            if (payment.Operator != PaymentOperator.Orange)
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "Le code OTP n'est utilisé que pour Orange Money."));
            }

            SenePayInitiatePaymentResponse senepayResponse;
            try
            {
                senepayResponse = await _senepay.InitiatePaymentAsync(BuildSenePayRequest(
                    payment, "orange", dto.CustomerPhone, otpCode: dto.OtpCode, customerName: GuardianName()), ct);
            }
            catch (SenePayApiException ex)
            {
                _logger.LogError(ex,
                    "[payment/initiate] SenePay error pour Payment {PaymentId} (2e appel OTP)",
                    payment.Id);
                return StatusCode(502, ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "SenePay temporairement indisponible. Réessayez dans quelques secondes."));
            }

            // Mise à jour : nouveaux token/internalId potentiels (SenePay peut
            // créer une nouvelle transaction interne — on tracke le dernier).
            if (!string.IsNullOrEmpty(senepayResponse.InternalId))
                payment.SenePayInternalId = senepayResponse.InternalId;
            if (!string.IsNullOrEmpty(senepayResponse.Token))
                payment.SenePayTransactionId = senepayResponse.Token;

            if (string.Equals(senepayResponse.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                payment.Status = PaymentStatus.Failed;
                payment.FailedAt = DateTime.UtcNow;
                payment.FailureReason = senepayResponse.FailedReason ?? senepayResponse.ErrorCode;
            }

            await _context.SaveChangesAsync(ct);

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
            string operatorStr,
            string customerPhoneNational,
            string? otpCode,
            string? customerName)
        {
            return new SenePayInitiatePaymentRequest
            {
                Amount = payment.AmountFcfa,
                Currency = "XOF",
                CountryCode = "SN",
                Operator = operatorStr.ToLowerInvariant(),
                CustomerPhone = "+221" + customerPhoneNational,
                OtpCode = otpCode,
                OrderId = payment.Id.ToString(),
                CustomerName = customerName,
                WebhookUrl = _senepaySettings.WebhookPayinUrl
            };
        }

        private static PaymentOperator ParseOperator(string op)
        {
            return op.ToLowerInvariant() switch
            {
                "wave" => PaymentOperator.Wave,
                "orange" => PaymentOperator.Orange,
                _ => throw new ArgumentOutOfRangeException(nameof(op), $"Opérateur non supporté : {op}")
            };
        }

        private string? GuardianName()
        {
            // Approximation : on utilise l'email. La création d'un User dans
            // Idara ne capture pas systématiquement un FirstName/LastName ; le
            // CustomerName SenePay est informatif (affiché sur les reçus PSP).
            return User.GetEmail();
        }
    }
}
