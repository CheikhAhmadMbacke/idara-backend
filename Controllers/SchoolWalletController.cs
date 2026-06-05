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
        private readonly ILogger<SchoolWalletController> _logger;

        // Montant minimum de retrait et frais payout (%) ne sont plus codés en
        // dur : lus depuis PlatformSettings (éditable SuperAdmin).

        public SchoolWalletController(
            AppDbContext context,
            ISenePayClient senepay,
            IOptions<SenePaySettings> senepaySettings,
            IOtpService otp,
            ILogger<SchoolWalletController> logger)
        {
            _context = context;
            _senepay = senepay;
            _senepaySettings = senepaySettings.Value;
            _otp = otp;
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

            // Montant majoré envoyé à SenePay pour que le bénéficiaire reçoive
            // exactement dto.Amount après les frais opérateur (PayoutFeePercent).
            var sepayAmount = (long)Math.Ceiling(dto.Amount / (1 - platform.PayoutFeeRate));

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
                    Description = $"Retrait Idara école {schoolId.Value}",
                    CallbackUrl = _senepaySettings.WebhookPayoutUrl
                }, ct);
            }
            catch (SenePayApiException ex)
            {
                // Le payout n'a pas démarré côté SenePay → on restitue la
                // réservation et on marque le retrait Failed. Aucun webhook ne
                // viendra (SenePay a rejeté avant traitement).
                await ReleaseReservationAsync(withdrawal.Id, schoolId.Value, dto.Amount,
                    ex.ResponseBody ?? ex.Message, ct);
                _logger.LogError(ex,
                    "[withdraw] Échec appel SenePay payout pour Withdrawal {Id} — réservation restituée", withdrawal.Id);
                return StatusCode(502, ApiResponse<WithdrawalDto>.Fail(
                    "Le retrait n'a pas pu être lancé auprès de l'opérateur. Votre solde a été restitué. Réessayez plus tard."));
            }

            var statusLower = resp.Status?.ToLowerInvariant();

            // Rejet synchrone (200 mais success=false / status failed|cancelled) :
            // même traitement — restitution + Failed.
            if (!resp.Success || statusLower == "failed" || statusLower == "cancelled")
            {
                await ReleaseReservationAsync(withdrawal.Id, schoolId.Value, dto.Amount,
                    resp.ErrorCode ?? resp.Message ?? resp.Status ?? "rejected", ct);
                _logger.LogWarning(
                    "[withdraw] SenePay a rejeté le payout Withdrawal {Id} (status={Status}) — réservation restituée",
                    withdrawal.Id, resp.Status);
                return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                    "Le retrait a été refusé par l'opérateur. Votre solde a été restitué."));
            }

            // Succès SYNCHRONE immédiat (rare pour mobile money, mais possible — et
            // SenePay n'enverrait alors peut-être pas de webhook séparé) : on clôt
            // tout de suite. Idempotent vis-à-vis d'un webhook éventuel (qui verra
            // Status != Initiated). Sans ça : Withdrawal bloqué en Initiated +
            // Pending fantôme permanent (cf. revue Phase 3, M2).
            if (statusLower == "completed")
            {
                var fees = (long)Math.Round(resp.Fees?.Provider ?? 0, MidpointRounding.AwayFromZero);
                var net = (long)Math.Round(resp.NetAmount, MidpointRounding.AwayFromZero);
                await FinalizeCompletedAsync(withdrawal.Id, schoolId.Value, dto.Amount,
                    resp.DisbursementId, fees, net, ct);
                _logger.LogInformation(
                    "[withdraw] Withdrawal {Id} complété SYNCHRONE (School {SchoolId}, {Amount} FCFA)",
                    withdrawal.Id, schoolId.Value, dto.Amount);
                return Ok(ApiResponse<WithdrawalDto>.Ok(MapToDto(withdrawal), "Retrait effectué."));
            }

            // Sinon (pending/processing/submitted) : on pose juste le disbursement_id
            // via un UPDATE ciblé (ExecuteUpdate, change tracker ignoré) gardé par
            // Status=Initiated pour ne pas écraser une clôture concurrente d'un
            // webhook ultra-rapide. Puis on attend le webhook final.
            await _context.Withdrawals
                .Where(w => w.Id == withdrawal.Id && w.Status == WithdrawalStatus.Initiated)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.SenePayDisbursementId, resp.DisbursementId), ct);

            _logger.LogInformation(
                "[withdraw] Withdrawal {Id} initié (School {SchoolId}, {Amount} FCFA, sepay={Sepay}, disbId={DisbId}, status={Status})",
                withdrawal.Id, schoolId.Value, dto.Amount, sepayAmount, resp.DisbursementId, resp.Status);

            return Ok(ApiResponse<WithdrawalDto>.Ok(MapToDto(withdrawal),
                "Retrait initié. Vous serez notifié une fois les fonds envoyés."));
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

        /// <summary>
        /// Restitue une réservation : Pending → Available + transaction Release +
        /// Withdrawal.Status = Failed. Utilisé quand le payout n'a pas pu
        /// démarrer (échec/refus SenePay à l'init). Transaction PG atomique.
        /// </summary>
        private async Task ReleaseReservationAsync(
            int withdrawalId, int schoolId, long amount, string reason, CancellationToken ct)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            // Verrou pessimiste sur le wallet (sérialise vs un éventuel webhook).
            var wallet = await _context.LockWalletAsync(schoolId, ct);
            var withdrawal = await _context.Withdrawals.FirstOrDefaultAsync(w => w.Id == withdrawalId, ct);
            if (wallet == null || withdrawal == null) return;

            // Idempotence : ne restituer que si encore réservé (Initiated).
            if (withdrawal.Status != WithdrawalStatus.Initiated) return;

            wallet.PendingBalance -= amount;
            wallet.AvailableBalance += amount;
            wallet.UpdatedAt = DateTime.UtcNow;

            withdrawal.Status = WithdrawalStatus.Failed;
            withdrawal.FailedAt = DateTime.UtcNow;
            withdrawal.FailureReason = Truncate(reason, 480);

            _context.WalletTransactions.Add(new WalletTransaction
            {
                SchoolId = schoolId,
                Type = WalletTransactionType.Release,
                AmountFcfa = amount, // release = signé positif
                BalanceAfter = wallet.AvailableBalance,
                RelatedEntity = WalletRelatedEntity.Withdrawal,
                RelatedId = withdrawalId,
                Note = $"Restitution retrait #{withdrawalId} (échec init)",
                OccurredAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        /// <summary>
        /// Clôt un retrait completed (cas synchrone à l'init) : débit définitif
        /// du Pending + TotalWithdrawn. Pas de WalletTransaction (Available ne
        /// bouge pas — la réservation portait déjà le -X, cf. gotcha §55).
        /// Verrou pessimiste + garde Status=Initiated pour rester idempotent vis
        /// d'un éventuel webhook completed qui arriverait ensuite.
        /// </summary>
        private async Task FinalizeCompletedAsync(
            int withdrawalId, int schoolId, long amount,
            string? disbursementId, long fees, long netReceived, CancellationToken ct)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var wallet = await _context.LockWalletAsync(schoolId, ct);
            var withdrawal = await _context.Withdrawals.FirstOrDefaultAsync(w => w.Id == withdrawalId, ct);
            if (wallet == null || withdrawal == null) return;
            if (withdrawal.Status != WithdrawalStatus.Initiated) return;

            withdrawal.Status = WithdrawalStatus.Completed;
            withdrawal.CompletedAt = DateTime.UtcNow;
            withdrawal.FeesFcfa = fees;
            withdrawal.NetReceivedFcfa = netReceived;
            withdrawal.SenePayDisbursementId ??= disbursementId;

            wallet.PendingBalance -= amount;
            wallet.TotalWithdrawnLifetime += amount;
            wallet.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

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

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s[..max];
        }
    }
}
