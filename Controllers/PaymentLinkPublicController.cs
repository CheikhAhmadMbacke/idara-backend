using Idara.API.Common.Utilities;
using Idara.API.Data;
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
    /// Page PUBLIQUE du lien de paiement (<c>/pay/link/{token}</c>), ouverte
    /// depuis WhatsApp SANS compte. Le jeton (128 bits, unique) est la seule
    /// authentification. La page :
    /// <list type="number">
    /// <item>recalcule la dette du responsable À CHAQUE OUVERTURE (même calcul
    /// que « Tout payer » dans l'app — <see cref="IGuardianPaymentService"/>) ;</item>
    /// <item>refuse de lancer un 2ᵉ paiement tant qu'un Pending n'est pas tranché
    /// par SenePay — avec une vérification LIVE pour ne pas faire attendre 45 min
    /// un parent qui a annulé sur Wave ;</item>
    /// <item>crée le Payment, appelle SenePay et renvoie l'URL Wave. Le retour
    /// se fait par la page de résultat existante (<c>/pay/{id}/{token}</c>).</item>
    /// </list>
    /// Le lien ne crée AUCUNE facture. Jamais de transition de statut sur l'horloge.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("pay/link")]
    public class PaymentLinkPublicController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IGuardianPaymentService _guardianPayments;
        private readonly PayinVerificationJob _payinVerification;
        private readonly IMemoryCache _cache;
        private readonly SenePaySettings _senepaySettings;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<PaymentLinkPublicController> _logger;

        /// <summary>Un Pending plus vieux que ça n'est plus considéré comme bloquant (SenePay l'a tranché depuis longtemps ; le poll le verra).</summary>
        private static readonly TimeSpan PendingWindow = TimeSpan.FromHours(6);
        private const int MaxStartsPerWindow = 5;
        private static readonly TimeSpan StartWindow = TimeSpan.FromMinutes(15);
        /// <summary>Un verrou par lien pour sérialiser /start (mono-instance, comme le rate-limit mémoire §92).</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> StartGates = new();

        public PaymentLinkPublicController(
            AppDbContext context,
            IGuardianPaymentService guardianPayments,
            PayinVerificationJob payinVerification,
            IMemoryCache cache,
            IOptions<SenePaySettings> senepaySettings,
            IWebHostEnvironment env,
            ILogger<PaymentLinkPublicController> logger)
        {
            _context = context;
            _guardianPayments = guardianPayments;
            _payinVerification = payinVerification;
            _cache = cache;
            _senepaySettings = senepaySettings.Value;
            _env = env;
            _logger = logger;
        }

        /// <summary>`GET /pay/link/{token}` — la page HTML (statique, le JS lit l'état).</summary>
        [HttpGet("{token}")]
        public IActionResult GetPage(string token)
        {
            var pagePath = Path.Combine(_env.WebRootPath, "pay", "link.html");
            if (!System.IO.File.Exists(pagePath))
            {
                _logger.LogError("[pay/link] link.html introuvable à {Path}", pagePath);
                return NotFound("Page indisponible.");
            }
            Response.Headers.CacheControl = "no-store";
            return PhysicalFile(pagePath, "text/html; charset=utf-8");
        }

        /// <summary>`GET /pay/link/{token}/state` — dette du jour + éventuel paiement en cours.</summary>
        [HttpGet("{token}/state")]
        public async Task<IActionResult> GetState(string token, CancellationToken ct)
        {
            var link = await LookupAsync(token, ct);
            if (link == null) return NotFound(new { status = "not_found" });
            if (link.RevokedAt != null || link.Guardian.IsDeleted)
            {
                return Ok(new { status = "revoked" });
            }

            // Anti-martèlement : chaque lecture peut interroger SenePay (vérif
            // live d'un Pending). 60 lectures / lien / minute couvrent largement
            // le poll de 5 s de la page.
            var stateKey = $"paylink:state:{link.Id}";
            var reads = _cache.GetOrCreate(stateKey, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1); return 0; });
            if (reads >= 60)
            {
                return StatusCode(429, new { status = "rate_limited", message = "Trop de requêtes. Patientez une minute." });
            }
            _cache.Set(stateKey, reads + 1, TimeSpan.FromMinutes(1));

            // Une « ouverture » = une visite, pas chaque poll de 5 s en attente
            // d'un Pending : on ne compte qu'une fois par minute.
            if (link.LastOpenedAt == null || link.LastOpenedAt < DateTime.UtcNow.AddMinutes(-1))
            {
                var maintenant = DateTime.UtcNow;
                link.FirstOpenedAt ??= maintenant;
                link.LastOpenedAt = maintenant;
                link.OpenCount++;
                await _context.SaveChangesAsync(ct);
            }

            var pending = await ResolvePendingAsync(link, ct);
            var state = await BuildStateAsync(link, pending, ct);
            return Ok(state);
        }

        public class StartRequest
        {
            /// <summary>Montant libre uniquement : part par enfant (FCFA).</summary>
            public List<StartAmount>? Amounts { get; set; }
        }

        public class StartAmount
        {
            public int StudentId { get; set; }
            public long AmountFcfa { get; set; }
        }

        /// <summary>
        /// `POST /pay/link/{token}/start` — crée le Payment et renvoie l'URL Wave.
        /// Refuse (409) tant qu'un paiement Pending du responsable n'est pas tranché.
        /// </summary>
        [HttpPost("{token}/start")]
        public async Task<IActionResult> Start(string token, [FromBody] StartRequest? body, CancellationToken ct)
        {
            var link = await LookupAsync(token, ct);
            if (link == null) return NotFound(new { status = "not_found" });
            if (link.RevokedAt != null || link.Guardian.IsDeleted)
            {
                return Ok(new { status = "revoked" });
            }

            // Anti-rafale : 5 démarrages / lien / 15 min. Un double-clic ou un
            // rafraîchissement ne doit pas empiler des Payment Pending.
            var key = $"paylink:start:{link.Id}";
            var count = _cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = StartWindow; return 0; });
            if (count >= MaxStartsPerWindow)
            {
                return StatusCode(429, new { status = "rate_limited", message = "Trop de tentatives. Réessayez dans quelques minutes." });
            }
            _cache.Set(key, count + 1, StartWindow);

            // Sérialise les démarrages d'un MÊME lien (deux onglets, double-clic
            // réseau) : sans ce verrou, deux POST simultanés passeraient tous deux
            // le contrôle « aucun Pending » et créeraient deux Payments identiques
            // — le double paiement que la page veut précisément empêcher.
            var gate = StartGates.GetOrAdd(link.Id, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(TimeSpan.FromSeconds(10), ct))
            {
                return StatusCode(429, new { status = "rate_limited", message = "Un démarrage est déjà en cours. Réessayez." });
            }
            try
            {
            var pending = await ResolvePendingAsync(link, ct);
            if (pending != null)
            {
                return Conflict(new
                {
                    status = "pending",
                    message = "Un paiement est déjà en cours. Patientez qu'il soit confirmé ou annulé avant de réessayer.",
                    pendingPaymentId = pending.Id,
                    pendingResultUrl = ResultUrl(pending)
                });
            }

            var outstanding = await _guardianPayments.GetOutstandingAsync(link.GuardianId, link.SchoolId, ct);
            if (outstanding == null)
            {
                return StatusCode(500, new { status = "error", message = "Configuration de paiement de l'école introuvable." });
            }

            Payment payment;
            try
            {
                if (outstanding.IsFreeAmount)
                {
                    var amounts = (body?.Amounts ?? new List<StartAmount>())
                        .GroupBy(a => a.StudentId)
                        .ToDictionary(g => g.Key, g => g.Sum(a => a.AmountFcfa));
                    payment = await _guardianPayments.CreateFreeAmountPaymentAsync(outstanding, amounts, link.Id, ct);
                }
                else
                {
                    payment = await _guardianPayments.CreateConsolidatedPaymentAsync(outstanding, link.Id, ct);
                }
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { status = "error", message = ex.Message });
            }

            var outcome = await _guardianPayments.InitiateWithSenePayAsync(payment, link.Guardian.FullName, ct);
            if (!outcome.Ok)
            {
                _logger.LogWarning("[pay/link] Initiation refusée Payment {Id} : {Msg}", payment.Id, outcome.ErrorMessage);
                return StatusCode(outcome.HttpStatus == 502 ? 502 : 400,
                    new { status = "error", message = outcome.ErrorMessage ?? "Le paiement n'a pas pu être lancé." });
            }
            if (string.IsNullOrWhiteSpace(outcome.RedirectUrl))
            {
                // Sans URL Wave on ne peut pas continuer ; le Payment Pending sera
                // tranché par le webhook / le poll, jamais par nous.
                return StatusCode(502, new { status = "error", message = "Wave n'a pas renvoyé de page de paiement. Réessayez dans un instant." });
            }

            _logger.LogInformation("[pay/link] Payment {PaymentId} lancé depuis le lien {LinkId} ({Amount} FCFA)",
                payment.Id, link.Id, payment.AmountFcfa);
            return Ok(new
            {
                status = "redirect",
                paymentId = payment.Id,
                redirectUrl = outcome.RedirectUrl,
                resultUrl = ResultUrl(payment),
                amountChargedFcfa = payment.AmountFcfa
            });
            }
            finally
            {
                gate.Release();
            }
        }

        // ===== Helpers =====

        private async Task<PaymentLink?> LookupAsync(string token, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length != 32 || !token.All(Uri.IsHexDigit))
                return null;
            return await _context.PaymentLinks
                .Include(l => l.Guardian)
                .FirstOrDefaultAsync(l => l.Token == token, ct);
        }

        /// <summary>
        /// Paiement Pending du responsable dans cette école, APRÈS vérification
        /// live auprès de SenePay. Null = aucun paiement en cours (un Pending
        /// tranché Completed/Failed par la vérification n'est plus bloquant).
        /// </summary>
        private async Task<Payment?> ResolvePendingAsync(PaymentLink link, CancellationToken ct)
        {
            var since = DateTime.UtcNow - PendingWindow;
            var pendingIds = await _context.Payments
                .Where(p => p.GuardianId == link.GuardianId
                            && p.SchoolId == link.SchoolId
                            && p.Purpose == PaymentPurpose.SchoolFee
                            && p.Status == PaymentStatus.Pending
                            && p.InitiatedAt >= since)
                .OrderByDescending(p => p.InitiatedAt)
                .Select(p => p.Id)
                .ToListAsync(ct);

            Payment? stillPending = null;
            foreach (var id in pendingIds)
            {
                var status = await _payinVerification.VerifyPaymentNowAsync(id, ct);
                if (status != PaymentStatus.Pending || stillPending != null) continue;
                var p = await _context.Payments.AsNoTracking().FirstAsync(x => x.Id == id, ct);
                // Un Pending SANS jeton SenePay = l'appel d'initiation a échoué
                // (502/timeout) AVANT toute redirection : le parent n'a jamais vu
                // Wave, aucun double paiement possible. Le bloquer 6 h sur une
                // panne SenePay serait absurde → non bloquant passé 2 min (le
                // webhook pourrait encore le compléter par OrderId, §108, mais
                // sans redirection personne n'a pu payer).
                if (string.IsNullOrWhiteSpace(p.SenePayTransactionId)
                    && p.InitiatedAt < DateTime.UtcNow.AddMinutes(-2))
                    continue;
                stillPending = p;
            }
            return stillPending;
        }

        private async Task<object> BuildStateAsync(PaymentLink link, Payment? pending, CancellationToken ct)
        {
            var school = await _context.Schools
                .Where(s => s.Id == link.SchoolId)
                .Select(s => new { s.Name, s.NameAr, s.LogoUrl })
                .FirstAsync(ct);

            // Un paiement complété il y a peu (≤ 15 min) : on le signale avec
            // son reçu — c'est le cas « je reviens sur le lien juste après Wave ».
            var recent = await _context.Payments
                .Where(p => p.GuardianId == link.GuardianId && p.SchoolId == link.SchoolId
                            && p.Status == PaymentStatus.Completed
                            && p.PaidAt != null && p.PaidAt >= DateTime.UtcNow.AddMinutes(-15))
                .OrderByDescending(p => p.PaidAt)
                .Select(p => new { p.Id, p.PublicResultToken, p.TargetAmountFcfa })
                .FirstOrDefaultAsync(ct);

            var outstanding = await _guardianPayments.GetOutstandingAsync(link.GuardianId, link.SchoolId, ct);
            var publicBase = _senepaySettings.PublicBaseUrl.TrimEnd('/');

            var lines = (outstanding?.Lines ?? new List<OutstandingLine>())
                .Select(l => new
                {
                    studentId = l.StudentId,
                    studentName = l.StudentName,
                    label = InvoiceLabel.Fr(l.Type, l.PeriodStart),
                    labelAr = InvoiceLabel.Ar(l.Type, l.PeriodStart),
                    amountFcfa = l.RemainingFcfa
                })
                .ToList();
            var total = outstanding?.TotalDueFcfa ?? 0;
            var freeAmount = outstanding?.IsFreeAmount == true;

            string status;
            if (pending != null) status = "pending";
            else if (freeAmount) status = "free_amount";
            else if (total > 0) status = "due";
            else status = "nothing_due";

            return new
            {
                status,
                schoolName = school.Name,
                schoolNameAr = school.NameAr,
                schoolLogoUrl = string.IsNullOrWhiteSpace(school.LogoUrl) ? null : publicBase + school.LogoUrl,
                guardianName = link.Guardian.FullName,
                lines,
                children = (outstanding?.Children ?? new List<OutstandingChild>())
                    .Where(c => !c.IsExited)
                    .Select(c => new { studentId = c.StudentId, studentName = c.StudentName, className = c.ClassName }),
                totalDueFcfa = total,
                amountToChargeFcfa = outstanding?.ChargeFor(total) ?? total,
                feesPayer = outstanding?.FeesPayer.ToString(),
                feePercent = outstanding?.FeesPayer == FeesPayer.Parent ? outstanding.ParentFeePercent : 0,
                minAmountFcfa = outstanding?.MinPayinFcfa ?? 200,
                pendingPaymentId = pending?.Id,
                pendingResultUrl = pending != null ? ResultUrl(pending) : null,
                recentPaymentResultUrl = recent != null ? $"{publicBase}/pay/{recent.Id}/{recent.PublicResultToken}" : null,
                recentPaymentAmountFcfa = recent?.TargetAmountFcfa
            };
        }

        private string ResultUrl(Payment p) =>
            $"{_senepaySettings.PublicBaseUrl.TrimEnd('/')}/pay/{p.Id}/{p.PublicResultToken}";
    }
}
