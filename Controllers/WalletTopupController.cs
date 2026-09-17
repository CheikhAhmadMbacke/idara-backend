using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
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
    /// Majoration (<see cref="FeesPayer.Parent"/>) : l'école est débitée de
    /// target × ParentFeeMultiplier et le wallet est crédité du target EXACT (§82) → recharger
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
        private readonly IWavePayinService _wavePayin;
        private readonly ILogger<WalletTopupController> _logger;

        public WalletTopupController(
            AppDbContext context,
            IWavePayinService wavePayin,
            ILogger<WalletTopupController> logger)
        {
            _context = context;
            _wavePayin = wavePayin;
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

            // Numéro récupéré en base : celui de l'admin qui recharge, à défaut
            // celui de l'école. Plus de saisie ni de choix d'opérateur : Wave
            // uniquement (refonte UX 2026-07-07).
            var adminId = User.GetUserId();
            var payerPhone = adminId != null
                ? await _context.Users.Where(u => u.Id == adminId.Value)
                    .Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct)
                : null;
            if (string.IsNullOrWhiteSpace(payerPhone))
                payerPhone = await _context.Schools.Where(s => s.Id == schoolId.Value)
                    .Select(s => s.PhoneNumber).FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(payerPhone))
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "Aucun numéro de téléphone n'est associé à votre compte ni à votre école. Ajoutez-en un dans les paramètres de l'école."));

            var platform = await _context.GetPlatformSettingsAsync(ct);
            if (dto.Amount < platform.MinPayinFcfa)
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    $"Le montant minimum est de {platform.MinPayinFcfa} FCFA."));
            }

            // Garantit le wallet (filet, comme pour un paiement parent).
            await _context.EnsurePaymentFoundationsAsync(schoolId.Value, ct);

            var operatorEnum = PaymentOperator.Wave; // Wave uniquement (2026-07-07)

            // Majoration (comme un paiement parent, FeesPayer=Parent) : l'école
            // veut recevoir EXACTEMENT dto.Amount dans son wallet ET pouvoir le
            // ressortir, donc on RÉSOUT le montant à débiter (ProviderFees) et le
            // webhook crédite le wallet du TargetAmountFcfa (§82).
            // TargetAmountFcfa = ce qui atterrit dans le wallet (5 000),
            // AmountFcfa = ce qui est débité du payeur (5 379 aux taux actuels).
            // ⚠️ Ne jamais réécrire un taux en dur ici : les frais ne sont pas un
            // pourcentage, ils dépendent du montant (arrondis du prestataire).
            if (!platform.Fees.IsConfigured)
            {
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "Recharge indisponible : les commissions du prestataire ne sont pas "
                    + "renseignées. SuperAdmin → Réglages plateforme → Frais."));
            }
            // 🔴 AUCUNE majoration : l'article 8.2 du contrat Wave interdit de
            // facturer des frais à un Détenteur pour payer via Wave, sous peine
            // de résiliation SANS PRÉAVIS. La recharge est l'un des rares
            // parcours où c'est bien un Détenteur qui paie. On débite donc le
            // montant exact, et le wallet est crédité du NET (mode School) —
            // c'est l'école qui supporte la commission, ce qui est conforme :
            // elle reçoit, elle ne paie pas au sens de la clause.
            var targetAmount = dto.Amount;
            var amountToCharge = targetAmount;
            var payment = new Payment
            {
                SchoolId = schoolId.Value,
                StudentId = null,
                GuardianId = null,
                InvoiceId = null,
                Purpose = PaymentPurpose.WalletTopup,
                AmountFcfa = amountToCharge,
                TargetAmountFcfa = targetAmount,
                FeesFcfa = 0,
                NetCreditedFcfa = 0,
                Operator = operatorEnum,
                FeesPayer = FeesPayer.School,
                Status = PaymentStatus.Pending,
                InitiatedAt = DateTime.UtcNow,
                PublicResultToken = Guid.NewGuid().ToString("N")
            };
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync(ct);

            var outcome = await _wavePayin.StartAsync(payment, User.GetEmail(), ct);
            if (!outcome.Ok)
            {
                _logger.LogError("[wallet/topup] Ouverture de session refusée pour Payment {PaymentId} : {Err}",
                    payment.Id, outcome.ErrorMessage);
                return StatusCode(outcome.HttpStatus, ApiResponse<InitiatePaymentResponseDto>.Fail(
                    outcome.ErrorMessage ?? "Le paiement est temporairement indisponible."));
            }

            return Ok(ApiResponse<InitiatePaymentResponseDto>.Ok(new InitiatePaymentResponseDto
            {
                PaymentId = payment.Id,
                Status = "Pending",
                // Wave ne connaît qu'une seule suite : ouvrir sa page. Les autres
                // valeurs de ce champ (saisie d'un code, poussée USSD) venaient
                // du prestataire précédent et n'ont plus de sens — mais le champ
                // reste, les applications installées le lisent (§220).
                NextAction = "REDIRECT_TO_PROVIDER_LINK",
                RedirectUrl = outcome.RedirectUrl,
                OtpRequired = false,
                ErrorCode = null,
                FailureReason = null,
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
                .FirstOrDefaultAsync(x => x.Id == id && x.SchoolId == schoolId.Value
                                          && x.Purpose == PaymentPurpose.WalletTopup, ct);
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

    }
}
