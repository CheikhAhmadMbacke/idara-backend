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
    /// Retraits du wallet école vers un compte Mobile Money (SenePay Payout).
    /// Saisie manuelle des coordonnées à chaque retrait (pas de comptes
    /// pré-enregistrés, spec §4.2), validation OTP SchoolAdmin obligatoire.
    ///
    /// Modèle de frais (spec §4.4) : le wallet est DÉJÀ net de payout. L'école
    /// retire X (= ce qu'elle voit), le bénéficiaire reçoit X. On envoie à
    /// SenePay X / (1 − 1,77 %) pour absorber les frais opérateur.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/school/wallet")]
    public class SchoolWalletController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ISenePayClient _senepay;
        private readonly SenePaySettings _senepaySettings;
        private readonly IOtpService _otp;
        private readonly IPayoutSettlementService _settlement;
        private readonly ILogger<SchoolWalletController> _logger;

        // Montant minimum de retrait et frais payout (%) ne sont plus codés en
        // dur : lus depuis PlatformSettings (éditable SuperAdmin).

        public SchoolWalletController(
            AppDbContext context,
            ISenePayClient senepay,
            IOptions<SenePaySettings> senepaySettings,
            IOtpService otp,
            IPayoutSettlementService settlement,
            ILogger<SchoolWalletController> logger)
        {
            _context = context;
            _senepay = senepay;
            _senepaySettings = senepaySettings.Value;
            _otp = otp;
            _settlement = settlement;
            _logger = logger;
        }

        /// <summary>
        /// Étape 1 : envoie un OTP au SchoolAdmin (interim : email ; WA en Phase 2)
        /// avant d'autoriser un retrait. L'OTP est scopé OtpPurpose.Withdrawal,
        /// valable selon OtpSettings (10 min par défaut), à usage unique.
        /// </summary>
        [HttpPost("withdraw/init")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<object>>> InitWithdraw(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var admin = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
            if (admin == null) return Unauthorized();

            await _otp.GenerateAndSendOtpAsync(admin.Email, OtpPurpose.Withdrawal, admin.PreferredLanguage);

            _logger.LogInformation(
                "[withdraw] OTP retrait envoyé au SchoolAdmin {UserId} (School {SchoolId})",
                userId, User.GetSchoolId());

            return Ok(ApiResponse<object>.Ok(
                new { sentTo = MaskEmail(admin.Email) },
                "Un code de validation vous a été envoyé par email."));
        }

        /// <summary>
        /// Étape 2 : exécute le retrait. Valide l'OTP, réserve le montant
        /// (Available → Pending), appelle SenePay Payout, puis attend le webhook
        /// final (3.3) pour le débit définitif ou la restitution.
        /// </summary>
        [HttpPost("withdraw")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<WithdrawalDto>>> Withdraw(
            [FromBody] WithdrawRequestDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // --- Résolution du bénéficiaire (carnet OU saisie ponctuelle) ---
            // La validation conditionnelle (champs manuels requis si pas de
            // BeneficiaryId, égalité des numéros, format) est faite par
            // WithdrawRequestDto.Validate (IValidatableObject).
            string recipientName;
            string recipientPhone;
            PaymentOperator operatorEnum;
            int? beneficiaryId = null;

            if (dto.BeneficiaryId != null)
            {
                var beneficiary = await _context.TransferBeneficiaries.FirstOrDefaultAsync(
                    b => b.Id == dto.BeneficiaryId.Value
                         && b.SchoolId == schoolId.Value
                         && !b.IsArchived, ct);
                if (beneficiary == null)
                    return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                        "Bénéficiaire introuvable ou archivé."));

                recipientName = beneficiary.Name;
                recipientPhone = beneficiary.Phone;
                operatorEnum = beneficiary.Operator;
                beneficiaryId = beneficiary.Id;
            }
            else
            {
                // Saisie manuelle — champs garantis non-null/valides par le DTO.
                operatorEnum = ParseOperator(dto.Operator!);
                recipientName = dto.RecipientName!.Trim();
                recipientPhone = dto.RecipientPhone!;
            }

            var admin = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
            if (admin == null) return Unauthorized();

            // Réglages globaux (min retrait + frais payout %), éditables SuperAdmin.
            var platform = await _context.GetPlatformSettingsAsync(ct);

            // Montant minimum : check statique avant toute transaction.
            if (dto.Amount < platform.MinWithdrawalFcfa)
                return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                    $"Le montant minimum est de {platform.MinWithdrawalFcfa} FCFA."));

            // Montant EXACT envoyé à SenePay — plus de majoration côté Idara.
            // Depuis le modèle de frais SenePay 2026, on force fee_mode="on_top"
            // (cf. SenePayPayoutRequest.FeeMode) : le bénéficiaire reçoit
            // précisément dto.Amount, les frais opérateur (~1,77%) sont prélevés
            // EN PLUS sur la réserve marchand — financés par le coussin de la
            // majoration payin +8% (§82). L'ancienne majoration `dto.Amount /
            // (1 - PayoutFeeRate)` faisait SUR-verser le bénéficiaire (bug réel :
            // retrait de 500 → 510 reçu), car le nouveau modèle SenePay verse le
            // montant saisi tel quel. `PayoutFeePercent` (PlatformSettings) n'est
            // donc plus utilisé pour le calcul — conservé pour estimation/affichage.
            var sepayAmount = dto.Amount;

            var withdrawal = new Withdrawal
            {
                SchoolId = schoolId.Value,
                AmountFcfa = dto.Amount,
                SepayAmountFcfa = sepayAmount,
                Operator = operatorEnum,
                Category = dto.Category,
                BeneficiaryId = beneficiaryId,
                RecipientName = recipientName,
                RecipientPhone = recipientPhone,
                Status = WithdrawalStatus.Initiated,
                InitiatedById = userId.Value,
                CreatedAt = DateTime.UtcNow
            };

            // --- Verrou wallet + check solde + OTP + réservation, ATOMIQUEMENT ---
            // SELECT ... FOR UPDATE sérialise tout mouvement concurrent sur ce
            // wallet (deux retraits simultanés, retrait vs crédit payin, vs
            // webhook) : ni sur-débit ni lost update possibles. L'OTP est
            // consommé SOUS ce verrou → deux requêtes au même code sont
            // sérialisées (la 2e voit IsUsed=true). On COMMIT avant l'appel
            // SenePay (anti-race webhook, même logique que le payin §1.4).
            await using (var tx = await _context.Database.BeginTransactionAsync(ct))
            {
                var wallet = await _context.LockWalletAsync(schoolId.Value, ct);
                if (wallet == null)
                {
                    await tx.RollbackAsync(ct);
                    return StatusCode(500, ApiResponse<WithdrawalDto>.Fail("Wallet introuvable."));
                }

                if (dto.Amount > wallet.AvailableBalance)
                {
                    await tx.RollbackAsync(ct);
                    return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                        $"Solde insuffisant. Disponible : {wallet.AvailableBalance} FCFA."));
                }

                var otpOk = await _otp.VerifyOtpAsync(admin.Email, dto.OtpCode, OtpPurpose.Withdrawal);
                if (!otpOk)
                {
                    await tx.RollbackAsync(ct);
                    return BadRequest(ApiResponse<WithdrawalDto>.Fail("Code de validation invalide ou expiré."));
                }

                _context.Withdrawals.Add(withdrawal);
                await _context.SaveChangesAsync(ct); // assigne withdrawal.Id

                wallet.AvailableBalance -= dto.Amount;
                wallet.PendingBalance += dto.Amount;
                wallet.UpdatedAt = DateTime.UtcNow;

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    SchoolId = schoolId.Value,
                    Type = WalletTransactionType.Reservation,
                    AmountFcfa = -dto.Amount, // réservation = signé négatif
                    BalanceAfter = wallet.AvailableBalance,
                    RelatedEntity = WalletRelatedEntity.Withdrawal,
                    RelatedId = withdrawal.Id,
                    Note = $"Réservation retrait #{withdrawal.Id}",
                    OccurredAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }

            // --- Appel SenePay Payout (hors transaction) ---
            SenePayPayoutResponse resp;
            try
            {
                resp = await _senepay.InitiatePayoutAsync(new SenePayPayoutRequest
                {
                    ExternalId = withdrawal.Id.ToString(),
                    Amount = sepayAmount,
                    Phone = "221" + recipientPhone,
                    RecipientName = recipientName,
                    Country = "SN",
                    Operator = operatorEnum == PaymentOperator.Wave ? "wave" : "orange",
                    Type = "seller_payment",
                    // SenePay/AfribaPay mappe `description` sur son `reference_id`
                    // en aval, qui n'accepte que [A-Za-z0-9_-] : ni accents ni
                    // espaces (sinon HTTP 400 "reference_id ... invalid or
                    // contains unsupported characters"). On garde donc une chaîne
                    // ASCII sans espace — découvert au 1er test payout réel.
                    Description = $"Idara-retrait-ecole-{schoolId.Value}",
                    // Frais "en plus" : le bénéficiaire reçoit exactement sepayAmount
                    // (= dto.Amount), pas de repli "inclusive" (cf. SenePayPayoutRequest).
                    FeeMode = "on_top",
                    CallbackUrl = _senepaySettings.WebhookPayoutUrl
                }, ct);
            }
            catch (SenePayApiException ex)
            {
                // DURCISSEMENT ANTI DOUBLE DÉPENSE : un timeout/5xx ne signifie PAS
                // que le décaissement a échoué — il peut être sorti côté
                // AfribaPay/opérateur. On ne restitue QUE sur un rejet 4xx clair
                // (validation pré-exécution, aucun fonds sorti).
                var isDuplicate = (ex.ResponseBody ?? string.Empty)
                    .Contains("DUPLICATE_EXTERNAL_ID", StringComparison.OrdinalIgnoreCase);

                if (ex.StatusCode is >= 400 and < 500 && !isDuplicate)
                {
                    // Rejet pré-exécution (numéro/opérateur invalide, solde
                    // marchand insuffisant…) → restitution immédiate + feedback.
                    await _settlement.SettleFailedAsync(
                        withdrawal.Id, ex.ResponseBody ?? ex.Message, null, null, "sync-init", ct);
                    _logger.LogWarning(ex,
                        "[withdraw] SenePay {Status} (rejet pré-exécution) Withdrawal {Id} — réservation restituée",
                        ex.StatusCode, withdrawal.Id);
                    return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                        "Le retrait a été refusé (coordonnées ou solde). Votre solde a été restitué."));
                }

                // 5xx / timeout / réseau / duplicate = INDÉTERMINÉ → on garde les
                // fonds réservés (UnderVerification) et le PayoutVerificationJob
                // interrogera GET /payouts/{id} (autoritatif) jusqu'à résolution.
                await _settlement.MarkUnderVerificationAsync(
                    withdrawal.Id, null, ex.Message, "sync-init", ct);
                await _context.Entry(withdrawal).ReloadAsync(ct);
                _logger.LogWarning(ex,
                    "[withdraw] SenePay indéterminé (status={Status}) Withdrawal {Id} — passé en vérification",
                    ex.StatusCode, withdrawal.Id);
                return Ok(ApiResponse<WithdrawalDto>.Ok(MapToDto(withdrawal),
                    "Retrait en cours de vérification. Vous serez notifié dès confirmation."));
            }

            var statusLower = resp.Status?.ToLowerInvariant();

            // Rejet TERMINAL explicite (failed/cancelled) : aucun fonds sorti →
            // restitution + Failed.
            if (statusLower is "failed" or "cancelled")
            {
                await _settlement.SettleFailedAsync(
                    withdrawal.Id, resp.ErrorCode ?? resp.Message ?? statusLower,
                    resp.DisbursementId, null, "sync-init", ct);
                _logger.LogWarning(
                    "[withdraw] SenePay a rejeté le payout Withdrawal {Id} (status={Status}) — réservation restituée",
                    withdrawal.Id, resp.Status);
                return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                    "Le retrait a été refusé par l'opérateur. Votre solde a été restitué."));
            }

            // Succès SYNCHRONE (défensif — depuis le durcissement SenePay, le POST
            // ne renvoie plus `completed` synchrone en prod, mais on le gère par
            // sécurité si SenePay n'envoyait pas de webhook séparé).
            if (statusLower == "completed")
            {
                var fees = (long)Math.Round(resp.Fees?.Provider ?? 0, MidpointRounding.AwayFromZero);
                var net = (long)Math.Round(resp.NetAmount, MidpointRounding.AwayFromZero);
                await _settlement.SettleCompletedAsync(
                    withdrawal.Id, resp.DisbursementId, fees, net, null, "sync-init", ct);
                await _context.Entry(withdrawal).ReloadAsync(ct);
                _logger.LogInformation(
                    "[withdraw] Withdrawal {Id} complété SYNCHRONE (School {SchoolId}, {Amount} FCFA)",
                    withdrawal.Id, schoolId.Value, dto.Amount);
                return Ok(ApiResponse<WithdrawalDto>.Ok(MapToDto(withdrawal), "Retrait effectué."));
            }

            // Tout le reste — submitted / processing / pending / pending_approval /
            // pending_verification / inconnu / success=false-sans-statut-terminal :
            // INDÉTERMINÉ. On garde les fonds réservés et on poll. C'est désormais
            // le chemin nominal (submitted = opérateur a accepté, confirmation à venir).
            await _settlement.MarkUnderVerificationAsync(
                withdrawal.Id, resp.DisbursementId,
                $"status={resp.Status} success={resp.Success}", "sync-init", ct);
            await _context.Entry(withdrawal).ReloadAsync(ct);

            _logger.LogInformation(
                "[withdraw] Withdrawal {Id} en vérification (School {SchoolId}, {Amount} FCFA, sepay={Sepay}, disbId={DisbId}, status={Status})",
                withdrawal.Id, schoolId.Value, dto.Amount, sepayAmount, resp.DisbursementId, resp.Status);

            return Ok(ApiResponse<WithdrawalDto>.Ok(MapToDto(withdrawal),
                "Retrait en cours de vérification. Vous serez notifié dès confirmation."));
        }

        /// <summary>
        /// Historique read-only des retraits de l'école (récents d'abord).
        /// Numéro bénéficiaire masqué. Lisible SchoolAdmin + SchoolStaff.
        /// </summary>
        [HttpGet("withdrawals")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<IEnumerable<WithdrawalDto>>> GetWithdrawals(
            [FromQuery] int take, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var limit = take is > 0 and <= 200 ? take : 50;

            var items = await _context.Withdrawals
                .Where(w => w.SchoolId == schoolId.Value)
                .OrderByDescending(w => w.CreatedAt)
                .Take(limit)
                .ToListAsync(ct);

            return Ok(items.Select(MapToDto));
        }

        // ====================================================================
        // ===== Helpers =====
        // ====================================================================

        private static WithdrawalDto MapToDto(Withdrawal w) => new()
        {
            Id = w.Id,
            AmountFcfa = w.AmountFcfa,
            FeesFcfa = w.FeesFcfa,
            NetReceivedFcfa = w.NetReceivedFcfa,
            Operator = w.Operator,
            Category = w.Category,
            RecipientName = w.RecipientName,
            RecipientPhoneMasked = MaskPhone(w.RecipientPhone),
            Status = w.Status,
            FailureReason = w.FailureReason,
            CreatedAt = w.CreatedAt,
            CompletedAt = w.CompletedAt,
            FailedAt = w.FailedAt
        };

        private static PaymentOperator ParseOperator(string op) => op.ToLowerInvariant() switch
        {
            "wave" => PaymentOperator.Wave,
            "orange" => PaymentOperator.Orange,
            _ => throw new ArgumentOutOfRangeException(nameof(op), $"Opérateur non supporté : {op}")
        };

        /// <summary>"771234567" → "77*****67".</summary>
        private static string MaskPhone(string phone)
        {
            if (string.IsNullOrEmpty(phone) || phone.Length < 4) return "****";
            return phone[..2] + new string('*', phone.Length - 4) + phone[^2..];
        }

        private static string MaskEmail(string email)
        {
            var at = email.IndexOf('@');
            if (at <= 1) return "***" + (at >= 0 ? email[at..] : "");
            return email[0] + new string('*', Math.Min(at - 1, 6)) + email[at..];
        }
    }
}
