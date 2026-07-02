using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Admin;
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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Gestion financière plateforme (SuperAdmin uniquement) : réconciliation
    /// R = D + P, soldes par école, enregistrement des retraits manuels. Le
    /// retrait des gains plateforme via API arrive en Phase B.
    /// </summary>
    [ApiController]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    [Route("api/admin")]
    public class PlatformFinanceController : ControllerBase
    {
        private readonly IPlatformFinanceService _finance;
        private readonly AppDbContext _context;
        private readonly ISenePayClient _senepay;
        private readonly IPayoutSettlementService _settlement;
        private readonly SenePaySettings _senepaySettings;
        private readonly IMemoryCache _cache;
        private readonly ILogger<PlatformFinanceController> _logger;

        private const string PayoutUnavailableMsg =
            "Le retrait n'a pas pu être effectué : le service de paiement est momentanément indisponible. Réessaie plus tard.";

        public PlatformFinanceController(
            IPlatformFinanceService finance, AppDbContext context,
            ISenePayClient senepay, IPayoutSettlementService settlement,
            IOptions<SenePaySettings> senepaySettings, IMemoryCache cache,
            ILogger<PlatformFinanceController> logger)
        {
            _finance = finance;
            _context = context;
            _senepay = senepay;
            _settlement = settlement;
            _senepaySettings = senepaySettings.Value;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>Réconciliation complète : réserve SenePay vs dette écoles vs gains plateforme.</summary>
        [HttpGet("reconciliation")]
        public async Task<ActionResult<ApiResponse<ReconciliationDto>>> GetReconciliation(CancellationToken ct)
        {
            var dto = await _finance.ComputeReconciliationAsync(ct);
            return Ok(ApiResponse<ReconciliationDto>.Ok(dto));
        }

        /// <summary>Solde de chaque école (Available + Pending), plus gros d'abord.</summary>
        [HttpGet("schools/balances")]
        public async Task<ActionResult<ApiResponse<List<SchoolBalanceDto>>>> GetSchoolBalances(CancellationToken ct)
        {
            var list = await _finance.GetSchoolBalancesAsync(ct);
            return Ok(ApiResponse<List<SchoolBalanceDto>>.Ok(list));
        }

        /// <summary>
        /// Rapproche les payouts SenePay avec les retraits Idara : liste les retraits
        /// effectués HORS Idara (dashboard) + les anomalies. Lecture seule.
        /// </summary>
        [HttpGet("senepay/untracked-payouts")]
        public async Task<ActionResult<ApiResponse<UntrackedPayoutsResultDto>>> GetUntrackedPayouts(CancellationToken ct)
        {
            var result = await _finance.ScanUntrackedPayoutsAsync(ct);
            return Ok(ApiResponse<UntrackedPayoutsResultDto>.Ok(result));
        }

        /// <summary>
        /// Enregistre en 1 clic tous les retraits hors Idara détectés (non encore
        /// consignés) comme sorties plateforme → réconciliation exacte. Idempotent.
        /// </summary>
        [HttpPost("senepay/record-untracked")]
        public async Task<ActionResult<ApiResponse<object>>> RecordUntracked(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            try
            {
                var (recorded, total) = await _finance.RecordUntrackedPayoutsAsync(userId.Value, ct);
                return Ok(ApiResponse<object>.Ok(
                    new { recorded, totalFcfa = total },
                    recorded == 0
                        ? "Aucun nouveau retrait hors Idara à enregistrer."
                        : $"{recorded} retrait(s) hors Idara enregistré(s) ({total} FCFA)."));
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(503, ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>
        /// Enregistre un retrait manuel effectué depuis le dashboard marchand
        /// SenePay (hors Idara) → réduit les gains plateforme P, remet la
        /// réconciliation à l'équilibre. Écriture comptable pure (aucun mouvement
        /// SenePay déclenché).
        /// </summary>
        [HttpPost("platform/manual-outflow")]
        public async Task<ActionResult<ApiResponse<object>>> RecordManualOutflow(
            [FromBody] RecordManualOutflowDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var outflow = new PlatformOutflow
            {
                Type = PlatformOutflowType.ManualAdjustment,
                AmountFcfa = dto.AmountFcfa,
                Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
                OccurredAt = (dto.OccurredAt ?? DateTime.UtcNow).ToUtcSafe(),
                CreatedById = userId.Value,
                CreatedAt = DateTime.UtcNow
            };

            _context.PlatformOutflows.Add(outflow);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[finance] Retrait manuel enregistré : {Amount} FCFA (outflow #{Id}, par user {User})",
                dto.AmountFcfa, outflow.Id, userId.Value);

            return Ok(ApiResponse<object>.Ok(new { outflow.Id }, "Retrait manuel enregistré."));
        }

        /// <summary>
        /// Retrait des GAINS PLATEFORME vers un Mobile Money via SenePay Payout.
        /// Step-up mot de passe SuperAdmin. Garde-fou SOUS verrou plateforme : ne
        /// peut pas faire passer la réserve sous D × (1 + marge) — l'argent des
        /// écoles reste intangible. Réutilise le durcissement §78 (le règlement
        /// branche sur Withdrawal.IsPlatform → transitions de statut, P dérivé).
        /// </summary>
        [HttpPost("platform/withdraw")]
        public async Task<ActionResult<ApiResponse<WithdrawalDto>>> WithdrawPlatform(
            [FromBody] PlatformWithdrawRequestDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var admin = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
            if (admin == null) return Unauthorized();

            // Step-up mot de passe (rate-limité, clé partagée avec /auth/verify-password).
            var rlKey = $"pwdreauth:{userId.Value}";
            if (_cache.TryGetValue(rlKey, out int attempts) && attempts >= 5)
                return StatusCode(429, ApiResponse<WithdrawalDto>.Fail(
                    "Trop de tentatives. Réessaie dans quelques minutes."));
            if (!BCrypt.Net.BCrypt.Verify(dto.Password, admin.PasswordHash))
            {
                _cache.Set(rlKey, attempts + 1, TimeSpan.FromMinutes(15));
                return BadRequest(ApiResponse<WithdrawalDto>.Fail("Mot de passe incorrect."));
            }
            _cache.Remove(rlKey);

            var platform = await _context.GetPlatformSettingsAsync(ct);
            if (dto.Amount < platform.MinWithdrawalFcfa)
                return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                    $"Le montant minimum est de {platform.MinWithdrawalFcfa} FCFA."));

            var operatorEnum = dto.Operator.ToLowerInvariant() == "wave"
                ? PaymentOperator.Wave : PaymentOperator.Orange;
            var recipientName = dto.RecipientName.Trim();
            var recipientPhone = dto.RecipientPhone;
            var feeEstimate = (long)Math.Ceiling(dto.Amount * platform.PayoutFeeRate);

            // Réserve SenePay (live) AVANT le verrou (valeur externe, indépendante de nos writes).
            long reserve;
            try
            {
                var bal = await _senepay.GetMerchantBalanceAsync(ct);
                reserve = bal.ReserveBalanceFcfa;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[platform-withdraw] solde SenePay injoignable");
                return StatusCode(503, ApiResponse<WithdrawalDto>.Fail(
                    "Solde SenePay injoignable, réessaie plus tard."));
            }

            var withdrawal = new Withdrawal
            {
                IsPlatform = true,
                SchoolId = null,
                AmountFcfa = dto.Amount,
                SepayAmountFcfa = dto.Amount,
                FeesFcfa = feeEstimate, // estimation ; réécrite à la complétion
                Operator = operatorEnum,
                Category = TransferCategory.Withdrawal,
                RecipientName = recipientName,
                RecipientPhone = recipientPhone,
                Status = WithdrawalStatus.Initiated,
                InitiatedById = userId.Value,
                CreatedAt = DateTime.UtcNow
            };

            // --- Garde-fou SOUS verrou plateforme : D + P recalculés, available borné ---
            await using (var tx = await _context.Database.BeginTransactionAsync(ct))
            {
                await _context.LockPlatformAsync(ct);
                var (owed, p) = await _finance.ComputeDebtAndPlatformAsync(ct);
                var safeThreshold = (long)Math.Ceiling(owed * (1 + _finance.SafetyMarginPercent / 100.0));
                // min(P, réserve − seuil) : conservateur, ne descend jamais sous D×(1+marge).
                var withdrawable = Math.Max(0, Math.Min(p.TotalFcfa, reserve - safeThreshold));
                var cost = dto.Amount + feeEstimate;
                if (cost > withdrawable)
                {
                    await tx.RollbackAsync(ct);
                    return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                        $"Montant trop élevé. Retirable en sécurité (frais inclus) : {withdrawable} FCFA."));
                }

                _context.Withdrawals.Add(withdrawal);
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }

            // --- Appel SenePay + issues (mêmes règles que le retrait école, §78/§111) ---
            SenePayPayoutResponse resp;
            try
            {
                resp = await _senepay.InitiatePayoutAsync(new SenePayPayoutRequest
                {
                    ExternalId = withdrawal.Id.ToString(),
                    Amount = dto.Amount,
                    Phone = "221" + recipientPhone,
                    RecipientName = recipientName,
                    Country = "SN",
                    Operator = operatorEnum == PaymentOperator.Wave ? "wave" : "orange",
                    Type = "seller_payment",
                    Description = "Idara-gains-plateforme",
                    FeeMode = "on_top",
                    CallbackUrl = _senepaySettings.WebhookPayoutUrl
                }, ct);
            }
            catch (SenePayApiException ex)
            {
                var isDuplicate = (ex.ResponseBody ?? string.Empty)
                    .Contains("DUPLICATE_EXTERNAL_ID", StringComparison.OrdinalIgnoreCase);
                if (ex.StatusCode is >= 400 and < 500 && !isDuplicate)
                {
                    await _settlement.SettleFailedAsync(
                        withdrawal.Id, ex.ResponseBody ?? ex.Message, null, null, "sync-init", ct);
                    _logger.LogWarning(ex,
                        "[platform-withdraw] SenePay {Status} (rejet) Withdrawal {Id}", ex.StatusCode, withdrawal.Id);
                    return BadRequest(ApiResponse<WithdrawalDto>.Fail(PayoutUnavailableMsg));
                }
                await _settlement.MarkUnderVerificationAsync(withdrawal.Id, null, ex.Message, "sync-init", ct);
                await _context.Entry(withdrawal).ReloadAsync(ct);
                if (withdrawal.Status == WithdrawalStatus.Failed)
                    return BadRequest(ApiResponse<WithdrawalDto>.Fail(PayoutUnavailableMsg));
                return Ok(ApiResponse<WithdrawalDto>.Ok(MapToDto(withdrawal),
                    "Retrait en cours de vérification. Vous serez notifié dès confirmation."));
            }

            var statusLower = resp.Status?.ToLowerInvariant();
            if (statusLower is "failed" or "cancelled")
            {
                await _settlement.SettleFailedAsync(
                    withdrawal.Id, resp.ErrorCode ?? resp.Message ?? statusLower,
                    resp.DisbursementId, null, "sync-init", ct);
                return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                    "Le retrait a été refusé par l'opérateur."));
            }
            if (statusLower == "completed")
            {
                var fees = (long)Math.Round(resp.Fees?.Provider ?? 0, MidpointRounding.AwayFromZero);
                var net = (long)Math.Round(resp.NetAmount, MidpointRounding.AwayFromZero);
                await _settlement.SettleCompletedAsync(
                    withdrawal.Id, resp.DisbursementId, fees, net, null, "sync-init", ct);
                await _context.Entry(withdrawal).ReloadAsync(ct);
                _logger.LogInformation(
                    "[platform-withdraw] Withdrawal {Id} complété SYNCHRONE ({Amount} FCFA)",
                    withdrawal.Id, dto.Amount);
                return Ok(ApiResponse<WithdrawalDto>.Ok(MapToDto(withdrawal), "Retrait effectué."));
            }

            await _settlement.MarkUnderVerificationAsync(
                withdrawal.Id, resp.DisbursementId, $"status={resp.Status} success={resp.Success}", "sync-init", ct);
            await _context.Entry(withdrawal).ReloadAsync(ct);
            _logger.LogInformation(
                "[platform-withdraw] Withdrawal {Id} en vérification ({Amount} FCFA, disbId={DisbId}, status={Status})",
                withdrawal.Id, dto.Amount, resp.DisbursementId, resp.Status);
            return Ok(ApiResponse<WithdrawalDto>.Ok(MapToDto(withdrawal),
                "Retrait en cours de vérification. Vous serez notifié dès confirmation."));
        }

        // --- Helpers ---

        private static WithdrawalDto MapToDto(Withdrawal w) => new()
        {
            Id = w.Id,
            AmountFcfa = w.AmountFcfa,
            FeesFcfa = w.FeesFcfa,
            NetReceivedFcfa = w.NetReceivedFcfa,
            Operator = w.Operator,
            Category = w.Category,
            CategoryLabel = w.CategoryLabel,
            RecipientName = w.RecipientName,
            RecipientPhoneMasked = MaskPhone(w.RecipientPhone),
            Status = w.Status,
            FailureReason = w.FailureReason,
            CreatedAt = w.CreatedAt,
            CompletedAt = w.CompletedAt,
            FailedAt = w.FailedAt
        };

        private static string MaskPhone(string phone)
        {
            if (string.IsNullOrEmpty(phone) || phone.Length < 4) return "****";
            return phone[..2] + new string('*', phone.Length - 4) + phone[^2..];
        }
    }
}
