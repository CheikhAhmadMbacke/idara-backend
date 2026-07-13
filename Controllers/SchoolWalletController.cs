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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Retraits du wallet école vers un compte Mobile Money (SenePay Payout).
    /// Saisie manuelle des coordonnées à chaque retrait (pas de comptes
    /// pré-enregistrés, spec §4.2). Step-up de sécurité = mot de passe SchoolAdmin
    /// vérifié côté serveur (remplace l'OTP retrait, jugé trop compliqué — cf.
    /// chantier auth retrait). Le mot de passe est saisi au verrou de l'écran
    /// paiement puis réutilisé ici.
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
        private readonly IPayoutSettlementService _settlement;
        private readonly IMemoryCache _cache;
        private readonly IExportPdfService _exportPdf;
        private readonly ILogger<SchoolWalletController> _logger;

        // Montant minimum de retrait et frais payout (%) ne sont plus codés en
        // dur : lus depuis PlatformSettings (éditable SuperAdmin).

        public SchoolWalletController(
            AppDbContext context,
            ISenePayClient senepay,
            IOptions<SenePaySettings> senepaySettings,
            IPayoutSettlementService settlement,
            IMemoryCache cache,
            IExportPdfService exportPdf,
            ILogger<SchoolWalletController> logger)
        {
            _context = context;
            _senepay = senepay;
            _senepaySettings = senepaySettings.Value;
            _settlement = settlement;
            _cache = cache;
            _exportPdf = exportPdf;
            _logger = logger;
        }

        /// <summary>
        /// Exécute le retrait. Vérifie le mot de passe (step-up), réserve le montant
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

            // --- Step-up de sécurité : mot de passe SchoolAdmin (remplace l'OTP) ---
            // Rate-limité par utilisateur (15 min) pour empêcher le brute-force du
            // mot de passe via une session valide. Le mot de passe est réutilisable
            // (pas single-use comme l'OTP) → vérifié AVANT la transaction wallet,
            // pour échouer vite sans réserver de fonds. Clé PARTAGÉE avec
            // /auth/verify-password (`pwdreauth:{userId}`) : un attaquant n'a pas
            // deux budgets séparés contre le même secret (gate + retrait).
            var rlKey = $"pwdreauth:{userId.Value}";
            if (_cache.TryGetValue(rlKey, out int pwdAttempts) && pwdAttempts >= 5)
                return StatusCode(429, ApiResponse<WithdrawalDto>.Fail(
                    "Trop de tentatives. Réessayez dans quelques minutes."));
            if (!BCrypt.Net.BCrypt.Verify(dto.Password, admin.PasswordHash))
            {
                _cache.Set(rlKey, pwdAttempts + 1, TimeSpan.FromMinutes(15));
                return BadRequest(ApiResponse<WithdrawalDto>.Fail("Mot de passe incorrect."));
            }
            _cache.Remove(rlKey);

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
                CategoryLabel = dto.Category == TransferCategory.Other
                    ? dto.CategoryLabel?.Trim()
                    : null,
                BeneficiaryId = beneficiaryId,
                RecipientName = recipientName,
                RecipientPhone = recipientPhone,
                Source = dto.Source,
                Status = WithdrawalStatus.Initiated,
                InitiatedById = userId.Value,
                CreatedAt = DateTime.UtcNow
            };

            // --- Verrou wallet + check solde + réservation, ATOMIQUEMENT ---
            // SELECT ... FOR UPDATE sérialise tout mouvement concurrent sur ce
            // wallet (deux retraits simultanés, retrait vs crédit payin, vs
            // webhook) : ni sur-débit ni lost update possibles. Le mot de passe
            // (step-up) est déjà vérifié en amont. On COMMIT avant l'appel SenePay
            // (anti-race webhook, même logique que le payin §1.4).
            await using (var tx = await _context.Database.BeginTransactionAsync(ct))
            {
                var wallet = await _context.LockWalletAsync(schoolId.Value, ct);
                if (wallet == null)
                {
                    await tx.RollbackAsync(ct);
                    return StatusCode(500, ApiResponse<WithdrawalDto>.Fail("Wallet introuvable."));
                }

                // Check de solde PAR POCHE (le daara a choisi Total / Paiements /
                // Dons). Sous le verrou → l'état est figé.
                if (!wallet.HasEnoughForSource(dto.Amount, dto.Source))
                {
                    await tx.RollbackAsync(ct);
                    var (label, avail) = dto.Source switch
                    {
                        WithdrawalSource.Fee => ("solde paiement", wallet.FeeBalance()),
                        WithdrawalSource.Donation => ("solde don", wallet.DonationBalanceFcfa),
                        _ => ("solde disponible", wallet.AvailableBalance)
                    };
                    return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                        $"Solde insuffisant. {char.ToUpper(label[0]) + label[1..]} : {avail} FCFA."));
                }

                // --- Idempotence anti double-dépense ---
                // L'OTP à usage unique (consommé sous CE verrou) protégeait contre
                // le rejeu : double-tap, deux onglets, OU retry réseau après un
                // succès dont la réponse s'est perdue. Le mot de passe qui le
                // remplace est RÉUTILISABLE → on bloque tout retrait identique
                // (même école, même montant, même bénéficiaire) initié dans les
                // 60 dernières s et non échoué. Sous le verrou FOR UPDATE, deux
                // requêtes concurrentes sont sérialisées : la 2e voit la 1re
                // committée (READ COMMITTED) → rejetée. Les retraits Failed
                // (restitués) ne bloquent pas un nouvel essai légitime.
                var dupSince = DateTime.UtcNow.AddSeconds(-60);
                var isDuplicate = await _context.Withdrawals.AnyAsync(w =>
                    w.SchoolId == schoolId.Value
                    && w.AmountFcfa == dto.Amount
                    && w.RecipientPhone == recipientPhone
                    && w.Status != WithdrawalStatus.Failed
                    && w.CreatedAt >= dupSince, ct);
                if (isDuplicate)
                {
                    await tx.RollbackAsync(ct);
                    return Conflict(ApiResponse<WithdrawalDto>.Fail(
                        "Un retrait identique vient d'être initié. Vérifiez l'historique avant de réessayer."));
                }

                // Part prélevée sur la poche « Don » (figée sur le retrait pour une
                // restitution exacte en cas d'échec, cf. PayoutSettlementService).
                var donationDraw = wallet.DonationDrawFor(dto.Amount, dto.Source);
                withdrawal.DonationAmountFcfa = donationDraw;

                _context.Withdrawals.Add(withdrawal);
                await _context.SaveChangesAsync(ct); // assigne withdrawal.Id

                wallet.AvailableBalance -= dto.Amount;
                wallet.PendingBalance += dto.Amount;
                wallet.DonationBalanceFcfa -= donationDraw; // la poche don suit la réservation
                wallet.UpdatedAt = DateTime.UtcNow;

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    SchoolId = schoolId.Value,
                    Type = WalletTransactionType.Reservation,
                    Source = WalletSource.Withdrawal,
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
                    var reason = ex.ResponseBody ?? ex.Message;
                    await _settlement.SettleFailedAsync(
                        withdrawal.Id, reason, null, null, "sync-init", ct);
                    _logger.LogWarning(ex,
                        "[withdraw] SenePay {Status} (rejet pré-exécution) Withdrawal {Id} — réservation restituée",
                        ex.StatusCode, withdrawal.Id);
                    // Ne PAS accuser les coordonnées si c'est une indisponibilité
                    // temporaire du prestataire (float opérateur insuffisant) — le
                    // solde école est déjà validé en amont (ligne 182).
                    return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                        IsTemporaryPayoutOutage(reason)
                            ? PayoutUnavailableMsg
                            : "Le retrait a été refusé (coordonnées ou opérateur invalide). Votre solde a été restitué."));
                }

                // 5xx / timeout / réseau / duplicate = INDÉTERMINÉ → on garde les
                // fonds réservés (UnderVerification) et le PayoutVerificationJob
                // interrogera GET /payouts/{id} (autoritatif) jusqu'à résolution.
                await _settlement.MarkUnderVerificationAsync(
                    withdrawal.Id, null, ex.Message, "sync-init", ct);
                await _context.Entry(withdrawal).ReloadAsync(ct);
                _logger.LogWarning(ex,
                    "[withdraw] SenePay indéterminé (status={Status}) Withdrawal {Id} — statut={WStatus}",
                    ex.StatusCode, withdrawal.Id, withdrawal.Status);

                // Race observée en prod : SenePay renvoie un 502 gateway ET envoie
                // le webhook d'échec (« Insufficient balance ») quasi simultanément.
                // Si le webhook a déjà tranché Failed + restitué AVANT le retour du
                // POST, on renvoie un message clair plutôt qu'un « en vérification »
                // trompeur (suivi d'un push d'échec contradictoire). Sinon (vraiment
                // indéterminé), on garde le « en vérification » : le PayoutVerificationJob
                // interrogera GET /payouts/{id} jusqu'à résolution.
                if (withdrawal.Status == WithdrawalStatus.Failed)
                    return BadRequest(ApiResponse<WithdrawalDto>.Fail(PayoutUnavailableMsg));

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
                    IsTemporaryPayoutOutage(resp.ErrorCode ?? resp.Message)
                        ? PayoutUnavailableMsg
                        : "Le retrait a été refusé par l'opérateur. Votre solde a été restitué."));
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
                .Where(w => !w.IsHidden) // masqués par le daara → jamais affichés
                .OrderByDescending(w => w.CreatedAt)
                .Take(limit)
                .ToListAsync(ct);

            return Ok(items.Select(MapToDto));
        }

        /// <summary>
        /// `POST /api/school/wallet/withdrawals/{id}/hide` — masque un retrait de
        /// l'affichage école (cosmétique, comme les paiements/transactions ; ne
        /// touche NI au solde NI à la compta). Non réversible côté UI.
        /// </summary>
        [HttpPost("withdrawals/{id:int}/hide")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<bool>>> HideWithdrawal(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var n = await _context.Withdrawals
                .Where(w => w.Id == id && w.SchoolId == schoolId.Value && !w.IsPlatform)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.IsHidden, true), ct);
            if (n == 0) return NotFound(ApiResponse<bool>.Fail("Retrait introuvable."));

            return Ok(ApiResponse<bool>.Ok(true, "Retrait masqué."));
        }

        /// <summary>
        /// `GET /api/school/wallet/withdrawals/{id}/receipt` — reçu PDF d'un retrait
        /// / virement COMPLÉTÉ, côté ÉCOLE (SchoolAdmin/Staff), scopé à son école.
        /// Sert au daara à partager la preuve d'un salaire/charge (WhatsApp).
        /// Réutilise le rendu du reçu de virement (même document que côté bénéficiaire).
        /// </summary>
        [HttpGet("withdrawals/{id:int}/receipt")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> DownloadWithdrawalReceipt(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var w = await _context.Withdrawals
                .Include(x => x.School)
                .FirstOrDefaultAsync(x => x.Id == id
                                          && x.SchoolId == schoolId.Value
                                          && !x.IsPlatform
                                          && x.Status == WithdrawalStatus.Completed, ct);
            if (w == null)
                return NotFound(ApiResponse<bool>.Fail("Reçu indisponible (retrait non complété)."));

            var categoryLabel = w.Category == TransferCategory.Other
                    && !string.IsNullOrWhiteSpace(w.CategoryLabel)
                ? w.CategoryLabel!.Trim()
                : w.Category switch
                {
                    TransferCategory.TeacherSalary => "Salaire enseignant",
                    TransferCategory.StaffSalary => "Salaire personnel",
                    TransferCategory.Rent => "Loyer",
                    TransferCategory.Supplier => "Fournisseur",
                    TransferCategory.Utilities => "Charges",
                    TransferCategory.Other => "Autre",
                    _ => "Retrait"
                };

            var bytes = _exportPdf.BuildTransferReceiptPdf(
                w.School?.Name ?? "Daara",
                w.Id,
                string.IsNullOrWhiteSpace(w.RecipientName) ? "Beneficiaire" : w.RecipientName,
                w.AmountFcfa,
                w.Operator == PaymentOperator.Orange ? "Orange Money" : "Wave",
                categoryLabel,
                "Effectue",
                w.CompletedAt ?? w.CreatedAt);

            return File(bytes, "application/pdf", $"recu-retrait-idara-{w.Id:D6}.pdf");
        }

        // ====================================================================
        // ===== Helpers =====
        // ====================================================================

        // Message utilisateur pour un échec TEMPORAIRE côté prestataire (float
        // opérateur/AfribaPay insuffisant, 502 gateway SenePay) — distinct d'une
        // vraie erreur de coordonnées. Le solde est TOUJOURS restitué dans ces cas.
        private const string PayoutUnavailableMsg =
            "Le retrait n'a pas pu être effectué : le service de paiement est momentanément indisponible. Votre solde a été restitué, réessayez plus tard.";

        /// <summary>
        /// Détecte une indisponibilité temporaire du décaissement (float provider
        /// insuffisant, erreur gateway 5xx) à partir du message d'erreur SenePay,
        /// pour afficher un message honnête plutôt que d'accuser les coordonnées.
        /// </summary>
        private static bool IsTemporaryPayoutOutage(string? reason)
        {
            if (string.IsNullOrEmpty(reason)) return false;
            return reason.Contains("insufficient", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("balance", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("indisponible", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("502", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("503", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("504", StringComparison.OrdinalIgnoreCase);
        }

        private static WithdrawalDto MapToDto(Withdrawal w) => new()
        {
            Id = w.Id,
            AmountFcfa = w.AmountFcfa,
            FeesFcfa = w.FeesFcfa,
            NetReceivedFcfa = w.NetReceivedFcfa,
            Operator = w.Operator,
            Category = w.Category,
            Source = w.Source,
            CategoryLabel = w.CategoryLabel,
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
    }
}
