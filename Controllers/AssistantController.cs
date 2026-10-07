using System.ComponentModel.DataAnnotations;
using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.Enums;
using Idara.API.Services;
using Idara.API.Services.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// 🤖 Assistant IA de l'école (2026-10-07). Direction et personnel, chacun
    /// avec ses propres droits : l'assistant ne fait rien que l'écran ne
    /// permette déjà à ce rôle.
    /// </summary>
    [ApiController]
    [Route("api/assistant")]
    [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
    public class AssistantController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IAssistantService _assistant;
        private readonly IAssistantCreditService _credits;
        private readonly AssistantToolbox _tools;
        private readonly IWavePayinService _wavePayin;
        private readonly ILogger<AssistantController> _logger;

        public AssistantController(
            AppDbContext context,
            IAssistantService assistant,
            IAssistantCreditService credits,
            AssistantToolbox tools,
            IWavePayinService wavePayin,
            ILogger<AssistantController> logger)
        {
            _context = context;
            _assistant = assistant;
            _credits = credits;
            _tools = tools;
            _wavePayin = wavePayin;
            _logger = logger;
        }

        public class HistoryItemDto
        {
            [Required] public string Role { get; set; } = "user";
            [StringLength(4000)] public string Text { get; set; } = string.Empty;
        }

        public class ChatDto
        {
            [Required(ErrorMessage = "Le message est vide.")]
            [StringLength(2000, MinimumLength = 1, ErrorMessage = "Le message est trop long.")]
            public string Message { get; set; } = string.Empty;

            /// <summary>Langue de l'application (« fr » ou « ar ») — celle des cartes et des refus.</summary>
            public string? Lang { get; set; }

            /// <summary>Le message a été dicté : l'assistant tolère une transcription imparfaite.</summary>
            public bool FromVoice { get; set; }

            public List<HistoryItemDto> History { get; set; } = new();
        }

        public class BuyCommandsDto
        {
            public int Commands { get; set; }
        }

        private AssistantCaller? Caller(string? lang = null)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return null;
            var l = lang == "ar" ? "ar" : "fr";
            return new AssistantCaller(schoolId.Value, userId.Value, User.GetRole() ?? "", l);
        }

        /// <summary>`GET /api/assistant/status` — solde, prix, disponibilité.</summary>
        [HttpGet("status")]
        public async Task<IActionResult> Status(CancellationToken ct)
        {
            var c = Caller();
            if (c == null) return Forbid();
            var s = await _credits.DescribeAsync(c.SchoolId, c.UserId, _assistant.IsConfigured, ct);
            return Ok(ApiResponse<AssistantStatus>.Ok(s, "OK"));
        }

        /// <summary>`POST /api/assistant/chat` — une commande.</summary>
        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ChatDto dto, CancellationToken ct)
        {
            var c = Caller(dto.Lang);
            if (c == null) return Forbid();

            var history = dto.History
                .Select(h => new AssistantHistoryItem(h.Role, h.Text))
                .ToList();
            var r = await _assistant.ChatAsync(c, dto.Message.Trim(), history, dto.FromVoice, ct);
            // 200 même pour un refus métier (solde épuisé, plafond) : l'écran
            // l'affiche dans la conversation, et un code d'erreur HTTP le
            // ferait passer pour une panne.
            return Ok(ApiResponse<AssistantChatResult>.Ok(r, r.Ok ? "OK" : (r.BlockedReason ?? "error")));
        }

        /// <summary>`POST /api/assistant/actions/{id}/confirm` — l'école valide une carte.</summary>
        [HttpPost("actions/{id:int}/confirm")]
        public async Task<IActionResult> Confirm(int id, [FromQuery] string? lang, CancellationToken ct)
        {
            var c = Caller(lang);
            if (c == null) return Forbid();
            var r = await _tools.ConfirmAsync(c, id, ct);
            if (r == null) return NotFound(ApiResponse<bool>.Fail("Proposition introuvable."));
            return Ok(ApiResponse<AssistantConfirmResult>.Ok(r, r.Message));
        }

        /// <summary>`POST /api/assistant/actions/{id}/cancel`.</summary>
        [HttpPost("actions/{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, CancellationToken ct)
        {
            var c = Caller();
            if (c == null) return Forbid();
            var card = await _tools.CancelAsync(c, id, ct);
            if (card == null) return NotFound(ApiResponse<bool>.Fail("Proposition introuvable."));
            return Ok(ApiResponse<AssistantCardDto>.Ok(card, "OK"));
        }

        /// <summary>
        /// `POST /api/assistant/purchase` — achète des commandes. Wave uniquement,
        /// montant figé, aucune saisie : même parcours que l'achat de pages.
        /// 🔴 Rien n'est octroyé ici : les commandes arrivent au webhook.
        /// </summary>
        [HttpPost("purchase")]
        public async Task<IActionResult> Buy([FromBody] BuyCommandsDto dto, CancellationToken ct)
        {
            var c = Caller();
            if (c == null) return Forbid();

            var s = await _credits.DescribeAsync(c.SchoolId, null, _assistant.IsConfigured, ct);
            if (!s.PurchaseEnabled || s.BlockedReason is "disabled" or "kyc_not_validated")
                return BadRequest(ApiResponse<bool>.Fail("L'achat de commandes n'est pas disponible pour l'instant."));
            if (dto.Commands <= 0)
                return BadRequest(ApiResponse<bool>.Fail("Indiquez combien de commandes vous voulez acheter."));
            if (dto.Commands > s.MaxCommandsPerPurchase)
                return BadRequest(ApiResponse<bool>.Fail(
                    $"Vous pouvez acheter au maximum {s.MaxCommandsPerPurchase} commandes à la fois."));

            var payerPhone = await _context.Users.Where(u => u.Id == c.UserId)
                .Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(payerPhone))
                payerPhone = await _context.Schools.Where(x => x.Id == c.SchoolId)
                    .Select(x => x.PhoneNumber).FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(payerPhone))
                return BadRequest(ApiResponse<bool>.Fail(
                    "Aucun numéro de téléphone n'est associé à votre compte ni à votre école. "
                    + "Ajoutez-en un dans les paramètres de l'école."));

            var amount = s.PricePerCommandFcfa * dto.Commands;

            // Même couple que l'achat de pages, et pour la même raison :
            // FeesPayer = School + TargetAmountFcfa = 0 exclut ce paiement de la
            // marge de majoration dans P (§112), qui le compte en recette de
            // service (PaymentPurposes.IsPlatformService).
            var payment = new Models.Payment
            {
                SchoolId = c.SchoolId,
                Purpose = PaymentPurpose.AssistantCredits,
                AssistantCommandsPurchased = dto.Commands,
                AssistantPricePerCommandFcfa = s.PricePerCommandFcfa,
                AmountFcfa = amount,
                TargetAmountFcfa = 0,
                FeesFcfa = 0,
                NetCreditedFcfa = 0,
                Operator = PaymentOperator.Wave,
                FeesPayer = FeesPayer.School,
                Status = PaymentStatus.Pending,
                InitiatedAt = DateTime.UtcNow,
                PublicResultToken = Guid.NewGuid().ToString("N"),
            };
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync(ct);

            var outcome = await _wavePayin.StartAsync(payment, User.GetEmail(), ct);
            if (!outcome.Ok)
            {
                _logger.LogError("[assistant/purchase] Session refusée pour Payment {PaymentId} : {Err}",
                    payment.Id, outcome.ErrorMessage);
                return StatusCode(outcome.HttpStatus, ApiResponse<bool>.Fail(
                    outcome.ErrorMessage ?? "Le paiement est temporairement indisponible."));
            }

            return Ok(ApiResponse<object>.Ok(new
            {
                paymentId = payment.Id,
                status = "Pending",
                nextAction = "REDIRECT_TO_PROVIDER_LINK",
                redirectUrl = outcome.RedirectUrl,
                commands = dto.Commands,
                pricePerCommandFcfa = s.PricePerCommandFcfa,
                amountFcfa = amount,
            }, "Paiement initié."));
        }

        /// <summary>`GET /api/assistant/purchase/{id}` — le webhook fait foi, ceci ne fait que le lire.</summary>
        [HttpGet("purchase/{id:int}")]
        public async Task<IActionResult> PurchaseStatus(int id, CancellationToken ct)
        {
            var c = Caller();
            if (c == null) return Forbid();
            var p = await _context.Payments.FirstOrDefaultAsync(
                x => x.Id == id && x.SchoolId == c.SchoolId && x.Purpose == PaymentPurpose.AssistantCredits, ct);
            if (p == null) return NotFound(ApiResponse<bool>.Fail("Achat introuvable."));

            var s = await _credits.DescribeAsync(c.SchoolId, null, _assistant.IsConfigured, ct);
            return Ok(ApiResponse<object>.Ok(new
            {
                paymentId = p.Id,
                status = p.Status.ToString(),
                commands = p.AssistantCommandsPurchased,
                amountFcfa = p.AmountFcfa,
                failureReason = p.FailureReason,
                remainingCommands = s.RemainingCommands,
            }, "OK"));
        }
    }
}
