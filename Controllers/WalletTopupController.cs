using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Payment;
using Idara.API.DTOs.Senepay;
using Idara.API.DTOs.Subscription;
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
    /// Recharge du wallet école par le SchoolAdmin (« Recharger mon wallet »,
    /// spec §5.2). Réutilise la mécanique payin SenePay (Phase 1.4) mais sans
    /// Student/Guardian/Invoice : c'est l'école qui paie son propre wallet.
    /// Majoration +8 % (<see cref="FeesPayer.Parent"/>) : l'école est débitée de
    /// target×1,08 et le wallet est crédité du target EXACT (§82) → recharger
    /// 5000 coûte 5400 mais crédite bien 5000. Cas d'usage clé :
    /// une école qui termine son essai 30j sans paiement parent encaissé →
    /// wallet à 0 → impossible de prélever l'abo → ce flux débloque.
    /// </summary>
    [ApiController]
    [Route("api/school/wallet")]
    [Authorize(Roles = UserRoles.SchoolAdmin)]
    public class WalletTopupController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ISenePayClient _senepay;
        private readonly SenePaySettings _senepaySettings;
        private readonly ILogger<WalletTopupController> _logger;

        public WalletTopupController(
            AppDbContext context,
            ISenePayClient senepay,
            IOptions<SenePaySettings> senepaySettings,
            ILogger<WalletTopupController> logger)
        {
            _context = context;
            _senepay = senepay;
            _senepaySettings = senepaySettings.Value;
            _logger = logger;
        }

        /// <summary>`POST /api/school/wallet/topup` — recharge Wave/Orange.</summary>
        [HttpPost("topup")]
        public async Task<ActionResult<ApiResponse<InitiatePaymentResponseDto>>> Topup(
            [FromBody] TopupRequestDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null)
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail("École introuvable."));

            // Valide l'opérateur AVANT ParseOperator (qui lèverait sinon une
            // ArgumentOutOfRangeException → 500). Réponse 400 propre.
            var opNorm = dto.Operator?.Trim().ToLowerInvariant();
            if (opNorm != "wave" && opNorm != "orange")
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "Opérateur non supporté. Choisissez Wave ou Orange Money."));

            if (string.Equals(dto.Operator, "orange", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(dto.OtpCode))
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "Pour Orange Money, l'OTP doit être généré sur le téléphone (#144#391#) puis fourni dans cette requête."));
            }

            var platform = await _context.GetPlatformSettingsAsync(ct);
            if (dto.Amount < platform.MinPayinFcfa)
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    $"Le montant minimum est de {platform.MinPayinFcfa} FCFA."));
            }

            // Garantit le wallet (filet, comme pour un paiement parent).
            await _context.EnsurePaymentFoundationsAsync(schoolId.Value, ct);

            var operatorEnum = ParseOperator(opNorm!); // opNorm validé "wave"/"orange" ci-dessus

            // Majoration +8 % (comme un paiement parent, FeesPayer=Parent) : l'école
            // veut recevoir EXACTEMENT dto.Amount dans son wallet, donc on la débite
            // de target×1,08 et le webhook crédite le wallet du TargetAmountFcfa
            // (§82). TargetAmountFcfa = ce qui atterrit dans le wallet (5000),
            // AmountFcfa = ce qui est débité du payeur (5400).
            var targetAmount = dto.Amount;
            var amountToCharge = (long)Math.Ceiling(targetAmount * platform.ParentFeeMultiplier);
            var payment = new Payment
            {
                SchoolId = schoolId.Value,
                StudentId = null,
                GuardianId = null,
                InvoiceId = null,
                AmountFcfa = amountToCharge,
                TargetAmountFcfa = targetAmount,
                FeesFcfa = 0,
                NetCreditedFcfa = 0,
                Operator = operatorEnum,
                FeesPayer = FeesPayer.Parent,
                Status = PaymentStatus.Pending,
                InitiatedAt = DateTime.UtcNow,
                PublicResultToken = Guid.NewGuid().ToString("N")
            };
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync(ct);

            SenePayInitiatePaymentResponse resp;
            try
            {
                resp = await _senepay.InitiatePaymentAsync(BuildRequest(payment, dto), ct);
            }
            catch (SenePayApiException ex)
            {
                _logger.LogError(ex, "[wallet/topup] SenePay error pour Payment {PaymentId}", payment.Id);
                return StatusCode(502, ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "SenePay temporairement indisponible. Réessayez dans quelques secondes."));
            }

            payment.SenePayInternalId = resp.InternalId;
            payment.SenePayTransactionId = resp.Token;
            await _context.SaveChangesAsync(ct);

            if (string.Equals(resp.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(resp.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                payment.Status = string.Equals(resp.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                    ? PaymentStatus.Cancelled : PaymentStatus.Failed;
                payment.FailedAt = DateTime.UtcNow;
                payment.FailureReason = resp.FailedReason ?? resp.ErrorCode;
                await _context.SaveChangesAsync(ct);
            }

            return Ok(ApiResponse<InitiatePaymentResponseDto>.Ok(new InitiatePaymentResponseDto
            {
                PaymentId = payment.Id,
                Status = resp.Status ?? "Pending",
                NextAction = resp.NextAction ?? "NONE",
                RedirectUrl = resp.RedirectUrl,
                OtpRequired = resp.OtpRequired,
                ErrorCode = resp.ErrorCode,
                FailureReason = resp.FailedReason,
                AmountChargedFcfa = payment.AmountFcfa
            }));
        }

        /// <summary>`GET /api/school/wallet/topup/{id}` — poll de statut (scopé école).</summary>
        [HttpGet("topup/{id:int}")]
        public async Task<ActionResult<ApiResponse<TopupStatusDto>>> GetStatus(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return BadRequest(ApiResponse<TopupStatusDto>.Fail("École introuvable."));

            var p = await _context.Payments
                .FirstOrDefaultAsync(x => x.Id == id && x.SchoolId == schoolId.Value && x.GuardianId == null, ct);
            if (p == null) return NotFound(ApiResponse<TopupStatusDto>.Fail("Recharge introuvable."));

            return Ok(ApiResponse<TopupStatusDto>.Ok(new TopupStatusDto
            {
                PaymentId = p.Id,
                Status = p.Status,
                AmountFcfa = p.AmountFcfa,
                NetCreditedFcfa = p.NetCreditedFcfa,
                // Montant réellement crédité au wallet = la cible (§82), pas le net
                // SenePay. Fallback net pour d'anciens topups (FeesPayer=School).
                CreditedFcfa = p.FeesPayer == FeesPayer.Parent && p.TargetAmountFcfa > 0
                    ? p.TargetAmountFcfa
                    : p.NetCreditedFcfa,
                FailureReason = p.FailureReason
            }));
        }

        private SenePayInitiatePaymentRequest BuildRequest(Payment payment, TopupRequestDto dto)
        {
            var publicBase = _senepaySettings.PublicBaseUrl.TrimEnd('/');
            var resultBase = $"{publicBase}/pay/{payment.Id}/{payment.PublicResultToken}";

            return new SenePayInitiatePaymentRequest
            {
                Amount = payment.AmountFcfa,
                Currency = "XOF",
                CountryCode = "SN",
                Operator = dto.Operator.ToLowerInvariant(),
                CustomerPhone = "+221" + dto.Phone,
                OtpCode = dto.OtpCode,
                OrderId = payment.Id.ToString(),
                CustomerName = User.GetEmail(),
                WebhookUrl = _senepaySettings.WebhookPayinUrl,
                ReturnUrl = $"{resultBase}?status=success",
                CancelUrl = $"{resultBase}?status=cancel"
            };
        }

        private static PaymentOperator ParseOperator(string op) => op.ToLowerInvariant() switch
        {
            "wave" => PaymentOperator.Wave,
            "orange" => PaymentOperator.Orange,
            _ => throw new ArgumentOutOfRangeException(nameof(op), $"Opérateur non supporté : {op}")
        };
    }
}
