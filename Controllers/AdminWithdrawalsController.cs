using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Admin;
using Idara.API.DTOs.Common;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services.Alerts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Tous les mouvements d'argent SORTANTS, vus de la plateforme : retraits des
    /// écoles et retraits de gains, avec leurs détails, leur suivi et les alertes
    /// qu'ils ont produites.
    ///
    /// <para><b>Ce qui manquait.</b> Chaque école voyait ses propres retraits ;
    /// la plateforme, elle, n'avait AUCUN écran pour les voir tous — ni pour
    /// consulter ses propres retraits de gains. La seule trace d'un échec était
    /// une ligne de journal que rien ne faisait remonter. On découvrait donc une
    /// réserve de décaissement à sec par l'appel d'un directeur (§111), c'est-à-dire
    /// après que plusieurs écoles ont déjà échoué.</para>
    ///
    /// <para>SuperAdmin uniquement, et en français.</para>
    /// </summary>
    [ApiController]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    [Route("api/admin/withdrawals")]
    public class AdminWithdrawalsController : ControllerBase
    {
        /// <summary>Au-delà, un retrait en vérification est considéré coincé —
        /// même seuil que l'alerte StuckUnderVerification, pour que le compteur
        /// de l'écran et l'e-mail ne puissent pas se contredire.</summary>
        private static readonly TimeSpan StuckThreshold = TimeSpan.FromHours(48);

        private readonly AppDbContext _context;
        private readonly IOpsAlertService _alerts;
        private readonly ILogger<AdminWithdrawalsController> _logger;

        public AdminWithdrawalsController(
            AppDbContext context, IOpsAlertService alerts,
            ILogger<AdminWithdrawalsController> logger)
        {
            _context = context;
            _alerts = alerts;
            _logger = logger;
        }

        /// <summary>
        /// Liste paginée de TOUS les retraits, écoles et plateforme confondues.
        /// Filtres appliqués CÔTÉ SERVEUR (§121).
        /// </summary>
        /// <param name="scope">« all » (défaut), « schools », « platform ».</param>
        [HttpGet]
        public async Task<ActionResult<ApiResponse<PaginatedResult<AdminWithdrawalDto>>>> List(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] int? schoolId = null,
            [FromQuery] WithdrawalStatus? status = null,
            [FromQuery] PaymentOperator? operatorFilter = null,
            [FromQuery] TransferCategory? category = null,
            [FromQuery] string? scope = null,
            [FromQuery] string? q = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] long? minAmount = null,
            [FromQuery] long? maxAmount = null,
            CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = Filter(_context.Withdrawals.AsNoTracking(),
                schoolId, status, operatorFilter, category, scope, q, from, to, minAmount, maxAmount);

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(w => w.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync(ct);

            return Ok(ApiResponse<PaginatedResult<AdminWithdrawalDto>>.Ok(
                new PaginatedResult<AdminWithdrawalDto>
                {
                    Data = await MapManyAsync(items, ct),
                    TotalCount = total,
                    Page = page,
                    PageSize = pageSize,
                }));
        }

        /// <summary>
        /// Compteurs d'en-tête, calculés sur le MÊME filtre que la liste — sans
        /// quoi l'utilisateur lirait un total qui ne correspond pas à ce qu'il a
        /// sous les yeux (§141 : un compteur se calcule côté serveur dès que la
        /// liste est paginée).
        /// </summary>
        [HttpGet("summary")]
        public async Task<ActionResult<ApiResponse<AdminWithdrawalSummaryDto>>> Summary(
            [FromQuery] int? schoolId = null,
            [FromQuery] WithdrawalStatus? status = null,
            [FromQuery] PaymentOperator? operatorFilter = null,
            [FromQuery] TransferCategory? category = null,
            [FromQuery] string? scope = null,
            [FromQuery] string? q = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] long? minAmount = null,
            [FromQuery] long? maxAmount = null,
            CancellationToken ct = default)
        {
            var query = Filter(_context.Withdrawals.AsNoTracking(),
                schoolId, status, operatorFilter, category, scope, q, from, to, minAmount, maxAmount);

            var stuckBefore = DateTime.UtcNow - StuckThreshold;

            var s = await query
                .GroupBy(_ => 1)
                .Select(g => new AdminWithdrawalSummaryDto
                {
                    Count = g.Count(),
                    CompletedCount = g.Count(w => w.Status == WithdrawalStatus.Completed),
                    CompletedAmountFcfa = g
                        .Where(w => w.Status == WithdrawalStatus.Completed)
                        .Sum(w => (long?)w.AmountFcfa) ?? 0,
                    FailedCount = g.Count(w => w.Status == WithdrawalStatus.Failed),
                    FailedAmountFcfa = g
                        .Where(w => w.Status == WithdrawalStatus.Failed)
                        .Sum(w => (long?)w.AmountFcfa) ?? 0,
                    PendingCount = g.Count(w => w.Status == WithdrawalStatus.Initiated
                                                || w.Status == WithdrawalStatus.UnderVerification),
                    PendingAmountFcfa = g
                        .Where(w => w.Status == WithdrawalStatus.Initiated
                                    || w.Status == WithdrawalStatus.UnderVerification)
                        .Sum(w => (long?)w.AmountFcfa) ?? 0,
                    PlatformCount = g.Count(w => w.IsPlatform),
                    PlatformAmountFcfa = g.Where(w => w.IsPlatform)
                        .Sum(w => (long?)w.AmountFcfa) ?? 0,
                    StuckCount = g.Count(w => w.Status == WithdrawalStatus.UnderVerification
                                              && w.VerificationStartedAt != null
                                              && w.VerificationStartedAt < stuckBefore),
                })
                .FirstOrDefaultAsync(ct);

            return Ok(ApiResponse<AdminWithdrawalSummaryDto>.Ok(s ?? new AdminWithdrawalSummaryDto()));
        }

        /// <summary>
        /// Détail d'un retrait : la fiche complète, ses écritures de solde et les
        /// alertes qu'il a produites. Réunies ici parce qu'un retrait qui pose
        /// question ne se comprend qu'en voyant les trois ensemble — sinon il
        /// faut ouvrir trois écrans et recouper à la main.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ApiResponse<object>>> Detail(int id, CancellationToken ct)
        {
            var w = await _context.Withdrawals.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (w == null) return NotFound(ApiResponse<object>.Fail("Retrait introuvable."));

            var dto = (await MapManyAsync(new List<Withdrawal> { w }, ct))[0];

            var movements = await _context.WalletTransactions.AsNoTracking()
                .Where(t => t.RelatedEntity == WalletRelatedEntity.Withdrawal && t.RelatedId == id)
                .OrderBy(t => t.Id)
                .Select(t => new
                {
                    t.Id, t.Type, t.Source, t.AmountFcfa, t.BalanceAfter, t.Note, t.OccurredAt
                })
                .ToListAsync(ct);

            var alerts = await _context.PayoutAlerts.AsNoTracking()
                .Where(a => a.WithdrawalId == id)
                .OrderByDescending(a => a.Id)
                .Select(a => new { a.Id, a.Type, a.Message, a.Resolved, a.CreatedAt })
                .ToListAsync(ct);

            var opsAlerts = await _context.OpsAlerts.AsNoTracking()
                .Where(a => a.RelatedId == id
                            && (a.Kind == OpsAlertKind.WithdrawalFailed
                                || a.Kind == OpsAlertKind.WithdrawalProviderOutage
                                || a.Kind == OpsAlertKind.WithdrawalStuck
                                || a.Kind == OpsAlertKind.PayoutAnomaly))
                .OrderByDescending(a => a.Id)
                .Select(a => new { a.Id, a.Subject, a.Advice, a.EmailedAt, a.CreatedAt })
                .ToListAsync(ct);

            return Ok(ApiResponse<object>.Ok(new
            {
                withdrawal = dto,
                walletMovements = movements,
                payoutAlerts = alerts,
                opsAlerts,
            }));
        }

        // ================================================================
        // ===== Journal des alertes d'exploitation =====
        // ================================================================

        /// <summary>
        /// Les alertes levées (dépense SMS, échecs de retrait, anomalies).
        /// Y compris celles qui n'ont PAS donné lieu à un e-mail : c'est
        /// justement celle-là qu'on cherchera après coup.
        /// </summary>
        [HttpGet("/api/admin/alerts")]
        public async Task<ActionResult<ApiResponse<PaginatedResult<OpsAlertDto>>>> Alerts(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] bool? resolved = null,
            [FromQuery] OpsAlertKind? kind = null,
            [FromQuery] int? schoolId = null,
            CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = _context.OpsAlerts.AsNoTracking().AsQueryable();
            if (resolved != null) query = query.Where(a => a.Resolved == resolved);
            if (kind != null) query = query.Where(a => a.Kind == kind);
            if (schoolId != null) query = query.Where(a => a.SchoolId == schoolId);

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(a => a.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync(ct);

            var schoolIds = items.Where(a => a.SchoolId != null)
                .Select(a => a.SchoolId!.Value).Distinct().ToList();
            var names = await _context.Schools.AsNoTracking()
                .Where(s => schoolIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

            return Ok(ApiResponse<PaginatedResult<OpsAlertDto>>.Ok(new PaginatedResult<OpsAlertDto>
            {
                Data = items.Select(a => new OpsAlertDto
                {
                    Id = a.Id,
                    Kind = a.Kind.ToString(),
                    KindLabel = OpsAlertService.KindLabel(a.Kind),
                    Urgent = OpsAlertService.IsUrgent(a.Kind),
                    Subject = a.Subject,
                    Body = a.Body,
                    Advice = a.Advice,
                    SchoolId = a.SchoolId,
                    SchoolName = a.SchoolId != null && names.TryGetValue(a.SchoolId.Value, out var n)
                        ? n : null,
                    RelatedId = a.RelatedId,
                    EmailedAt = a.EmailedAt,
                    Resolved = a.Resolved,
                    ResolvedAt = a.ResolvedAt,
                    CreatedAt = a.CreatedAt,
                }).ToList(),
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
            }));
        }

        /// <summary>Classe une alerte (traitée). Append-only : rien n'est
        /// supprimé, on marque.</summary>
        [HttpPost("/api/admin/alerts/{id:int}/resolve")]
        public async Task<ActionResult<ApiResponse<bool>>> ResolveAlert(int id, CancellationToken ct)
        {
            var a = await _context.OpsAlerts.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a == null) return NotFound(ApiResponse<bool>.Fail("Alerte introuvable."));

            a.Resolved = true;
            a.ResolvedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("[ops-alert] Alerte {Id} classee par SuperAdmin {UserId}",
                id, User.GetUserId());
            return Ok(ApiResponse<bool>.Ok(true, "Alerte classee."));
        }

        /// <summary>
        /// Fait sonner le numéro d'alerte, tout de suite.
        ///
        /// <para><b>Pourquoi cet endpoint existe.</b> Un dispositif d'alerte ne
        /// se vérifie pas en relisant sa configuration : il se vérifie en
        /// recevant le message. Sans ce bouton, la seule façon de savoir si le
        /// numéro est le bon, s'il passe les plafonds et si le texte est
        /// lisible, serait d'attendre une vraie panne de décaissement —
        /// c'est-à-dire le pire moment pour découvrir qu'il ne marche pas.
        /// C'est la même doctrine que <c>wave-check.sh</c> : un contrôle qui ne
        /// fait pas l'appel réel n'a rien prouvé (§190).</para>
        ///
        /// <para>L'alerte de test est une alerte comme les autres : écrite en
        /// base, visible dans la liste, et elle consomme un jeton de la bourse du
        /// jour. Une vérification qui emprunterait un chemin à part ne
        /// prouverait rien du chemin réel.</para>
        ///
        /// <para>🔴 Sa nature est <c>SmsHardCapReached</c> et non une nature
        /// inoffensive : c'est la seule façon d'éprouver AUSSI l'exemption de
        /// plafond, qui est la partie la plus facile à casser sans s'en
        /// apercevoir. Le texte, lui, dit clairement que c'est un essai — pour
        /// ne pas refaire le coup du 2026-09-06, où une alerte de banc d'essai a
        /// fait croire la plateforme à l'arrêt.</para>
        /// </summary>
        [HttpPost("/api/admin/alerts/test-sms")]
        public async Task<ActionResult<ApiResponse<bool>>> TestAlertSms(CancellationToken ct)
        {
            var quand = DateTime.UtcNow.ToString("dd/MM a HH'h'mm",
                System.Globalization.CultureInfo.InvariantCulture);

            await _alerts.SendAsync(new OpsAlertRequest(
                OpsAlertKind.SmsHardCapReached,
                // Clé horodatée à la minute : deux essais à une minute d'intervalle
                // sonnent tous les deux. Une clé fixe les aurait regroupés, et on
                // aurait conclu à une panne là où le dispositif faisait son
                // travail.
                GroupingKey: $"alert-selftest-{DateTime.UtcNow:yyyyMMddHHmm}",
                Subject: "ESSAI du canal d'alerte — aucune panne en cours",
                Facts: new[]
                {
                    new AlertFact("Nature", "Essai declenche a la main depuis le back-office"),
                    new AlertFact("Declenche par", User.GetUserId()?.ToString() ?? "-"),
                    new AlertFact("Quand", quand),
                    new AlertFact("Ce que cet essai prouve",
                        "Le numero d'alerte est joignable, le message tient en un segment, "
                        + "et le canal traverse bien les plafonds de depense."),
                },
                Advice: "Rien a faire : aucune panne n'est en cours. Si le SMS n'est PAS arrive, "
                      + "verifier OpsAlerts:SmsPhone et OpsAlerts:SmsEnabled sur le serveur, puis "
                      + "le journal (chercher \"[ops-alert]\").",
                SmsHeadline: "ESSAI du canal d'alerte, aucune panne en cours"), ct);

            _logger.LogInformation("[ops-alert] Essai du canal declenche par SuperAdmin {UserId}",
                User.GetUserId());

            return Ok(ApiResponse<bool>.Ok(true,
                "Alerte d'essai envoyee. Le SMS arrive en quelques secondes ; si rien n'arrive, "
                + "l'ecran des alertes dira si la ligne a ete ecrite."));
        }

        // ================================================================
        // ===== Helpers =====
        // ================================================================

        /// <summary>
        /// Filtres communs à la LISTE et aux COMPTEURS. Un seul endroit, pour que
        /// le total affiché ne puisse jamais démentir la liste en dessous (§116).
        ///
        /// <para>⚠️ Aucun filtre sur <c>IsHidden</c> : le masquage est une
        /// commodité d'affichage propre à l'école. La plateforme doit voir TOUT
        /// l'argent sorti — masquer ici rendrait une réconciliation fausse sans
        /// qu'aucun écran ne le signale.</para>
        /// </summary>
        private static IQueryable<Withdrawal> Filter(
            IQueryable<Withdrawal> query,
            int? schoolId, WithdrawalStatus? status, PaymentOperator? op,
            TransferCategory? category, string? scope, string? q,
            DateTime? from, DateTime? to, long? minAmount, long? maxAmount)
        {
            if (schoolId != null) query = query.Where(w => w.SchoolId == schoolId);
            if (status != null) query = query.Where(w => w.Status == status);
            if (op != null) query = query.Where(w => w.Operator == op);
            if (category != null) query = query.Where(w => w.Category == category);

            query = scope?.ToLowerInvariant() switch
            {
                "platform" => query.Where(w => w.IsPlatform),
                "schools" => query.Where(w => !w.IsPlatform),
                _ => query,
            };

            if (from != null) query = query.Where(w => w.CreatedAt >= from.Value.ToUtcSafe());
            if (to != null) query = query.Where(w => w.CreatedAt < to.Value.ToUtcSafe().AddDays(1));
            if (minAmount != null) query = query.Where(w => w.AmountFcfa >= minAmount);
            if (maxAmount != null) query = query.Where(w => w.AmountFcfa <= maxAmount);

            if (!string.IsNullOrWhiteSpace(q))
            {
                // Plié comme les colonnes en face (règle d'or : un accent
                // n'empêche jamais de trouver). Sans ce pliage, chercher
                // « Mbacké » ne renverrait plus RIEN, la colonne étant
                // désormais comparée sans ses accents.
                var term = SearchText.Fold(q);
                // La référence « RET-000087 » est saisie telle qu'elle est
                // affichée : on en extrait l'identifiant, sinon la recherche la
                // plus naturelle (recopier ce qu'on a sous les yeux) ne
                // trouverait rien.
                int? byRef = null;
                var digits = new string(term.Where(char.IsDigit).ToArray());
                if (term.StartsWith("RET", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(digits, out var parsed)) byRef = parsed;

                query = query.Where(w =>
                    EF.Functions.ILike(AppDbContext.Unaccent(w.RecipientName), $"%{term}%")
                    || EF.Functions.ILike(AppDbContext.Unaccent(w.RecipientPhone), $"%{term}%")
                    || EF.Functions.ILike(AppDbContext.Unaccent(w.Motif ?? ""), $"%{term}%")
                    || EF.Functions.ILike(AppDbContext.Unaccent(w.CategoryLabel ?? ""), $"%{term}%")
                    || EF.Functions.ILike(AppDbContext.Unaccent(w.ProviderDisbursementId ?? ""), $"%{term}%")
                    || (byRef != null && w.Id == byRef));
            }

            return query;
        }

        /// <summary>Complète les retraits avec le nom de l'école et celui de qui
        /// a initié — deux requêtes en lot, jamais une par ligne.</summary>
        private async Task<List<AdminWithdrawalDto>> MapManyAsync(
            List<Withdrawal> items, CancellationToken ct)
        {
            var schoolIds = items.Where(w => w.SchoolId != null)
                .Select(w => w.SchoolId!.Value).Distinct().ToList();
            var schools = await _context.Schools.AsNoTracking()
                .Where(s => schoolIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

            var userIds = items.Select(w => w.InitiatedById).Distinct().ToList();
            var users = await _context.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

            return items.Select(w => new AdminWithdrawalDto
            {
                Id = w.Id,
                Reference = IdaraReference.Withdrawal(w.Id),
                SchoolId = w.SchoolId,
                SchoolName = w.IsPlatform
                    ? "Plateforme (gains)"
                    : (w.SchoolId != null && schools.TryGetValue(w.SchoolId.Value, out var n)
                        ? (n ?? $"Ecole #{w.SchoolId}")
                        : $"Ecole #{w.SchoolId}"),
                IsPlatform = w.IsPlatform,
                AmountFcfa = w.AmountFcfa,
                WalletDebitedFcfa = w.WalletDebitedFcfa > 0 ? w.WalletDebitedFcfa : w.AmountFcfa,
                FeesFcfa = w.FeesFcfa,
                NetReceivedFcfa = w.NetReceivedFcfa,
                Operator = w.Operator,
                Category = w.Category,
                CategoryLabel = w.CategoryLabel,
                Motif = w.Motif,
                Source = w.Source,
                DonationAmountFcfa = w.DonationAmountFcfa,
                RecipientName = w.RecipientName,
                RecipientPhone = SenegalPhone.ToDisplay(w.RecipientPhone, "-"),
                Status = w.Status,
                StatusLabel = StatusLabel(w.Status),
                SenePayDisbursementId = w.ProviderDisbursementId,
                FailureReason = w.FailureReason,
                FailureCause = w.Status == WithdrawalStatus.Failed
                    ? PayoutFailureClassifier.Label(
                        PayoutFailureClassifier.Classify(w.FailureReason))
                    : null,
                InitiatedById = w.InitiatedById,
                InitiatedByName = users.TryGetValue(w.InitiatedById, out var un) ? un : null,
                VerificationAttempts = w.VerificationAttempts,
                VerificationStartedAt = w.VerificationStartedAt,
                NextVerificationAt = w.NextVerificationAt,
                LastCheckedAt = w.LastCheckedAt,
                ReversedAt = w.ReversedAt,
                IsHiddenFromSchool = w.IsHidden,
                CreatedAt = w.CreatedAt,
                CompletedAt = w.CompletedAt,
                FailedAt = w.FailedAt,
            }).ToList();
        }

        /// <summary>
        /// Statut en clair. « En verification » dit ce qui se passe RÉELLEMENT
        /// (les fonds sont réservés, l'issue n'est pas tranchée) là où
        /// « UnderVerification » n'apprend rien à qui n'a pas lu le code.
        /// </summary>
        private static string StatusLabel(WithdrawalStatus status) => status switch
        {
            WithdrawalStatus.Initiated => "En cours",
            WithdrawalStatus.UnderVerification => "En verification (fonds reserves)",
            WithdrawalStatus.Completed => "Effectue",
            WithdrawalStatus.Failed => "Echoue (solde restitue)",
            WithdrawalStatus.Cancelled => "Annule",
            _ => status.ToString(),
        };
    }
}
