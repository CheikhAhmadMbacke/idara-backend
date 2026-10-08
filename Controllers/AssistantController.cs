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
    [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
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

            /// <summary>
            /// La discussion à poursuivre (2026-10-08). Absente = une discussion
            /// neuve — ou, pour une application antérieure à l'historique, la
            /// dernière de moins de deux heures.
            /// </summary>
            public int? ConversationId { get; set; }

            public List<HistoryItemDto> History { get; set; } = new();
        }

        public class RenameConversationDto
        {
            [Required(ErrorMessage = "Le titre est vide.")]
            [StringLength(120, MinimumLength = 1, ErrorMessage = "Le titre doit faire au plus 120 caractères.")]
            public string Title { get; set; } = string.Empty;
        }

        /// <summary>
        /// 🎙️ Ce qu'a donné une dictée, vu du téléphone. JOURNALISÉ seulement :
        /// c'est ce qui dira, navigateur par navigateur, où la voix échoue —
        /// au lieu de le deviner.
        /// </summary>
        public class VoiceEventDto
        {
            /// <summary>android | web</summary>
            [StringLength(20)] public string Platform { get; set; } = string.Empty;
            /// <summary>Famille de navigateur déduite côté client (chrome, samsung, opera…).</summary>
            [StringLength(40)] public string? Browser { get; set; }
            [StringLength(400)] public string? UserAgent { get; set; }
            [StringLength(10)] public string? Lang { get; set; }
            /// <summary>ok | error | unsupported | no_start | no_speech</summary>
            [Required, StringLength(30)] public string Outcome { get; set; } = string.Empty;
            [StringLength(80)] public string? Error { get; set; }
            /// <summary>Longueur de la transcription — jamais son contenu.</summary>
            public int Chars { get; set; }
        }

        public record ConversationListItemDto(
            int Id, string Title, DateTime CreatedAt, DateTime UpdatedAt, int Exchanges, int PendingCards);

        public record ConversationExchangeDto(
            int TurnId, string Prompt, string? Reply, bool Ok, DateTime CreatedAt, List<AssistantCardDto> Cards);

        public record ConversationDetailDto(
            int Id, string Title, DateTime CreatedAt, DateTime UpdatedAt, List<ConversationExchangeDto> Exchanges);

        public class BuyCommandsDto
        {
            public int Commands { get; set; }
        }

        /// <summary>
        /// 🔒 Qui parle, établi par le SERVEUR à partir du jeton (§304) :
        /// école, rôle et — pour un enseignant — ses classes. Jamais rien du
        /// modèle ni du corps de la requête. L'observateur (lecture seule) est
        /// écarté explicitement : ses jetons portent des rôles SECONDAIRES de
        /// direction, que l'attribut [Authorize] laisserait passer.
        /// </summary>
        private async Task<AssistantCaller?> CallerAsync(string? lang = null, CancellationToken ct = default)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            var role = User.GetRole();
            if (schoolId == null || userId == null) return null;
            if (User.HasClaim("readonly", "true") || !AssistantPolicy.IsAllowedRole(role)) return null;
            var l = lang == "ar" ? "ar" : "fr";
            var visible = await _context.VisibleClassIdsAsync(role, userId.Value, schoolId.Value, ct);
            return new AssistantCaller(schoolId.Value, userId.Value, role!, l, visible);
        }

        /// <summary>`GET /api/assistant/status` — solde, prix, disponibilité.</summary>
        [HttpGet("status")]
        public async Task<IActionResult> Status(CancellationToken ct)
        {
            var c = await CallerAsync(ct: ct);
            if (c == null) return Forbid();
            var s = await _credits.DescribeAsync(c.SchoolId, c.UserId, _assistant.IsConfigured, ct);
            return Ok(ApiResponse<AssistantStatus>.Ok(s, "OK"));
        }

        /// <summary>`POST /api/assistant/chat` — une commande.</summary>
        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ChatDto dto, CancellationToken ct)
        {
            var c = await CallerAsync(dto.Lang, ct);
            if (c == null) return Forbid();

            var history = dto.History
                .Select(h => new AssistantHistoryItem(h.Role, h.Text))
                .ToList();
            var r = await _assistant.ChatAsync(c, dto.Message.Trim(), history, dto.FromVoice, dto.ConversationId, ct);
            // 200 même pour un refus métier (solde épuisé, plafond) : l'écran
            // l'affiche dans la conversation, et un code d'erreur HTTP le
            // ferait passer pour une panne.
            return Ok(ApiResponse<AssistantChatResult>.Ok(r, r.Ok ? "OK" : (r.BlockedReason ?? "error")));
        }

        // =====================================================================
        // 💬 Historique des discussions (2026-10-08)
        // 🔒 Chacun ne voit que les SIENNES : le filtre porte sur l'école ET la
        // personne du jeton, jamais sur un identifiant fourni seul.
        // =====================================================================

        private IQueryable<Models.AssistantConversation> Mine(AssistantCaller c) =>
            _context.AssistantConversations.Where(x =>
                x.SchoolId == c.SchoolId && x.UserId == c.UserId && x.HiddenAt == null);

        /// <summary>`GET /api/assistant/conversations` — les plus récentes d'abord.</summary>
        [HttpGet("conversations")]
        public async Task<IActionResult> Conversations([FromQuery] int limit = 100, CancellationToken ct = default)
        {
            var c = await CallerAsync(ct: ct);
            if (c == null) return Forbid();
            limit = Math.Clamp(limit, 1, 200);
            var now = DateTime.UtcNow;

            var list = await Mine(c)
                .OrderByDescending(x => x.UpdatedAt)
                .Take(limit)
                .Select(x => new ConversationListItemDto(
                    x.Id, x.Title, x.CreatedAt, x.UpdatedAt,
                    _context.AssistantTurns.Count(t => t.ConversationId == x.Id),
                    _context.AssistantActions.Count(a =>
                        a.Status == AssistantActionStatus.Pending && a.ExpiresAt > now
                        && _context.AssistantTurns.Any(t => t.Id == a.TurnId && t.ConversationId == x.Id))))
                .ToListAsync(ct);
            return Ok(ApiResponse<List<ConversationListItemDto>>.Ok(list, "OK"));
        }

        /// <summary>`GET /api/assistant/conversations/{id}` — la discussion entière, cartes comprises.</summary>
        [HttpGet("conversations/{id:int}")]
        public async Task<IActionResult> Conversation(int id, CancellationToken ct)
        {
            var c = await CallerAsync(ct: ct);
            if (c == null) return Forbid();
            var conv = await Mine(c).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (conv == null) return NotFound(ApiResponse<bool>.Fail("Discussion introuvable."));

            var turns = await _context.AssistantTurns
                .Where(t => t.ConversationId == id)
                .OrderBy(t => t.Id)
                .Select(t => new { t.Id, t.Prompt, t.Reply, t.Success, t.CreatedAt })
                .ToListAsync(ct);
            var ids = turns.Select(t => t.Id).ToList();
            var actions = await _context.AssistantActions
                .Where(a => a.TurnId != null && ids.Contains(a.TurnId.Value)
                    && a.SchoolId == c.SchoolId && a.UserId == c.UserId)
                .OrderBy(a => a.Id)
                .ToListAsync(ct);

            var exchanges = turns.Select(t => new ConversationExchangeDto(
                t.Id, t.Prompt, t.Reply, t.Success && t.Reply != null, t.CreatedAt,
                actions.Where(a => a.TurnId == t.Id).Select(AssistantToolbox.ToCard).ToList())).ToList();

            return Ok(ApiResponse<ConversationDetailDto>.Ok(
                new ConversationDetailDto(conv.Id, conv.Title, conv.CreatedAt, conv.UpdatedAt, exchanges), "OK"));
        }

        /// <summary>`PATCH /api/assistant/conversations/{id}` — renommer.</summary>
        [HttpPatch("conversations/{id:int}")]
        public async Task<IActionResult> RenameConversation(int id, [FromBody] RenameConversationDto dto, CancellationToken ct)
        {
            var c = await CallerAsync(ct: ct);
            if (c == null) return Forbid();
            var conv = await Mine(c).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (conv == null) return NotFound(ApiResponse<bool>.Fail("Discussion introuvable."));
            var title = System.Text.RegularExpressions.Regex.Replace(dto.Title, @"\s+", " ").Trim();
            if (title.Length == 0) return BadRequest(ApiResponse<bool>.Fail("Le titre est vide."));
            conv.Title = title;
            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<object>.Ok(new { conv.Id, conv.Title }, "OK"));
        }

        /// <summary>
        /// `DELETE /api/assistant/conversations/{id}` — la discussion disparaît de
        /// la liste. 🔴 Ses échanges restent au registre (ce qui a été décompté,
        /// §55) ; ses cartes encore en attente sont annulées, pour qu'aucune ne
        /// puisse se confirmer depuis un écran resté ouvert.
        /// </summary>
        [HttpDelete("conversations/{id:int}")]
        public async Task<IActionResult> DeleteConversation(int id, CancellationToken ct)
        {
            var c = await CallerAsync(ct: ct);
            if (c == null) return Forbid();
            var conv = await Mine(c).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (conv == null) return NotFound(ApiResponse<bool>.Fail("Discussion introuvable."));

            var now = DateTime.UtcNow;
            conv.HiddenAt = now;
            var turnIds = _context.AssistantTurns.Where(t => t.ConversationId == id).Select(t => t.Id);
            var pending = await _context.AssistantActions
                .Where(a => a.Status == AssistantActionStatus.Pending && a.TurnId != null && turnIds.Contains(a.TurnId.Value))
                .ToListAsync(ct);
            foreach (var a in pending)
            {
                a.Status = AssistantActionStatus.Cancelled;
                a.ResolvedAt = now;
            }
            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<bool>.Ok(true, "Discussion supprimée."));
        }

        /// <summary>`POST /api/assistant/voice-event` — journal des dictées (aucun contenu).</summary>
        [HttpPost("voice-event")]
        public async Task<IActionResult> VoiceEvent([FromBody] VoiceEventDto dto, CancellationToken ct)
        {
            var c = await CallerAsync(ct: ct);
            if (c == null) return Forbid();
            _logger.LogInformation(
                "[assistant/voice] {Outcome} école {SchoolId} : {Platform}/{Browser} langue={Lang} erreur={Error} "
                + "caractères={Chars} UA={UserAgent}",
                dto.Outcome, c.SchoolId, dto.Platform, dto.Browser, dto.Lang, dto.Error, dto.Chars, dto.UserAgent);
            return NoContent();
        }

        /// <summary>`POST /api/assistant/actions/{id}/confirm` — l'école valide une carte.</summary>
        [HttpPost("actions/{id:int}/confirm")]
        public async Task<IActionResult> Confirm(int id, [FromQuery] string? lang, CancellationToken ct)
        {
            var c = await CallerAsync(lang, ct);
            if (c == null) return Forbid();
            var r = await _tools.ConfirmAsync(c, id, ct);
            if (r == null) return NotFound(ApiResponse<bool>.Fail("Proposition introuvable."));
            return Ok(ApiResponse<AssistantConfirmResult>.Ok(r, r.Message));
        }

        /// <summary>`POST /api/assistant/actions/{id}/cancel`.</summary>
        [HttpPost("actions/{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, CancellationToken ct)
        {
            var c = await CallerAsync(ct: ct);
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
            var c = await CallerAsync(ct: ct);
            if (c == null) return Forbid();
            if (!c.IsDirection) return Forbid();

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
            var c = await CallerAsync(ct: ct);
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
