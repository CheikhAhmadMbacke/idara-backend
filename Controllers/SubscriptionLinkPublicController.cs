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
    /// 🔗 La page PUBLIQUE où une école règle son abonnement, sans se connecter.
    ///
    /// <para><b>Pourquoi publique.</b> C'est toute sa raison d'être : au 15, une
    /// école impayée n'a plus accès à l'application. Si son moyen de paiement
    /// vivait derrière la connexion, on l'aurait enfermée dehors avec la clé à
    /// l'intérieur. Le jeton de 128 bits est la seule authentification — comme
    /// pour le lien des familles (§161).</para>
    ///
    /// <para>🔴 <b>Le paiement solde la FACTURE, il ne recharge pas le wallet.</b>
    /// Il porte <see cref="PaymentPurpose.Subscription"/> et ne crédite aucun
    /// solde : c'est un revenu de la plateforme, qui entre dans P (§112),
    /// exactement comme l'achat de pages (§233).</para>
    ///
    /// <para>Le lien ne crée AUCUNE facture et ne fait AUCUNE transition d'état
    /// sur l'horloge : il lit ce que le cron a écrit, et le règle.</para>
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("pay/abo")]
    public class SubscriptionLinkPublicController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWavePayinService _wavePayin;
        private readonly IMemoryCache _cache;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<SubscriptionLinkPublicController> _logger;

        /// <summary>Un Pending plus vieux que ça ne bloque plus : Wave l'a tranché depuis longtemps.</summary>
        private static readonly TimeSpan PendingWindow = TimeSpan.FromHours(6);
        private const int MaxStartsPerWindow = 5;
        private static readonly TimeSpan StartWindow = TimeSpan.FromMinutes(15);

        /// <summary>Un verrou par lien : deux onglets ne doivent pas créer deux paiements.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> StartGates = new();

        public SubscriptionLinkPublicController(
            AppDbContext context,
            IWavePayinService wavePayin,
            IMemoryCache cache,
            IWebHostEnvironment env,
            ILogger<SubscriptionLinkPublicController> logger)
        {
            _context = context;
            _wavePayin = wavePayin;
            _cache = cache;
            _env = env;
            _logger = logger;
        }

        /// <summary>`GET /pay/abo/{token}` — la page HTML (le JS lit l'état).</summary>
        [HttpGet("{token}")]
        public IActionResult GetPage(string token)
        {
            var pagePath = Path.Combine(_env.WebRootPath, "pay", "abo.html");
            if (!System.IO.File.Exists(pagePath))
            {
                _logger.LogError("[pay/abo] abo.html introuvable à {Path}", pagePath);
                return NotFound("Page indisponible.");
            }
            Response.Headers.CacheControl = "no-store";
            return PhysicalFile(pagePath, "text/html; charset=utf-8");
        }

        /// <summary>
        /// `GET /pay/abo/{token}/state` — ce que l'école doit AUJOURD'HUI.
        /// </summary>
        /// <remarks>
        /// Recalculé à chaque ouverture, jamais figé : le lien sert mois après
        /// mois et doit dire « rien à payer » quand l'abonnement est à jour.
        /// </remarks>
        [HttpGet("{token}/state")]
        public async Task<IActionResult> State(string token, CancellationToken ct)
        {
            var link = await LookupAsync(token, ct);
            if (link == null) return NotFound(new { status = "not_found" });
            if (link.RevokedAt != null) return Ok(new { status = "revoked" });

            // Compteurs d'ouverture : c'est ce qui dira si la relance fonctionne
            // (délai entre le SMS et le premier clic).
            var now = DateTime.UtcNow;
            link.FirstOpenedAt ??= now;
            link.LastOpenedAt = now;
            link.OpenCount++;
            await _context.SaveChangesAsync(ct);

            var sub = await _context.Subscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SchoolId == link.SchoolId, ct);
            if (sub == null) return Ok(new { status = "nothing_due" });

            var invoice = await PendingInvoiceAsync(link.SchoolId, ct);
            var pending = await ResolvePendingPaymentAsync(link.SchoolId, ct);

            var school = await _context.Schools.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == link.SchoolId, ct);
            var schoolName = SchoolDisplayName.From(school).Primary();

            if (invoice == null)
            {
                return Ok(new
                {
                    status = "nothing_due",
                    schoolName,
                    nextBillingAt = sub.NextBillingAt,
                    amountFcfa = sub.AmountFcfa
                });
            }

            return Ok(new
            {
                status = pending != null ? "pending" : "due",
                schoolName,
                amountFcfa = invoice.AmountFcfa,
                smsRefactureFcfa = invoice.SmsRefactureFcfa,
                smsRefactureCount = invoice.SmsRefactureCount,
                periodStart = invoice.PeriodStart,
                periodEnd = invoice.PeriodEnd,
                subscriptionStatus = sub.Status.ToString(),
                // Date à laquelle l'accès sera bloqué si rien n'est payé. Une
                // relance qui ne dit pas QUAND ne fait que la moitié du chemin.
                readOnlyEndsAt = sub.ReadOnlyEndsAt,
                pendingPaymentId = pending?.Id,
                pendingResultUrl = pending == null ? null : _wavePayin.ResultPageUrl(pending)
            });
        }

        /// <summary>`POST /pay/abo/{token}/start` — lance le paiement Wave.</summary>
        [HttpPost("{token}/start")]
        public async Task<IActionResult> Start(string token, CancellationToken ct)
        {
            var link = await LookupAsync(token, ct);
            if (link == null) return NotFound(new { status = "not_found" });
            if (link.RevokedAt != null) return Ok(new { status = "revoked" });

            // Anti-rafale : un double-clic ne doit pas empiler des Pending.
            var key = $"abolink:start:{link.Id}";
            var count = _cache.GetOrCreate(key, e =>
            {
                e.AbsoluteExpirationRelativeToNow = StartWindow;
                return 0;
            });
            if (count >= MaxStartsPerWindow)
            {
                return StatusCode(429, new { status = "rate_limited", message = "Trop de tentatives. Réessayez dans quelques minutes." });
            }
            _cache.Set(key, count + 1, StartWindow);

            var gate = StartGates.GetOrAdd(link.Id, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(TimeSpan.FromSeconds(10), ct))
            {
                return StatusCode(429, new { status = "rate_limited", message = "Un démarrage est déjà en cours. Réessayez." });
            }
            try
            {
                var existing = await ResolvePendingPaymentAsync(link.SchoolId, ct);
                if (existing != null)
                {
                    return Conflict(new
                    {
                        status = "pending",
                        message = "Un paiement est déjà en cours. Patientez qu'il soit confirmé avant de réessayer.",
                        pendingPaymentId = existing.Id,
                        pendingResultUrl = _wavePayin.ResultPageUrl(existing)
                    });
                }

                var invoice = await PendingInvoiceAsync(link.SchoolId, ct);
                if (invoice == null)
                {
                    return Ok(new { status = "nothing_due", message = "Votre abonnement est à jour." });
                }

                // 🔴 FeesPayer = School et TargetAmountFcfa = 0, comme l'achat de
                // pages : l'école paie le prix affiché, la plateforme absorbe la
                // commission Wave (article 8.2, §145), et ce couple exclut le
                // paiement du calcul de la marge de majoration dans P (§112) —
                // sans quoi la recette serait comptée deux fois.
                var payment = new Payment
                {
                    SchoolId = link.SchoolId,
                    Purpose = PaymentPurpose.Subscription,
                    SubscriptionInvoiceId = invoice.Id,
                    AmountFcfa = invoice.AmountFcfa,
                    TargetAmountFcfa = 0,
                    FeesFcfa = 0,
                    NetCreditedFcfa = 0,
                    Operator = PaymentOperator.Wave,
                    FeesPayer = FeesPayer.School,
                    Status = PaymentStatus.Pending,
                    InitiatedAt = DateTime.UtcNow,
                    PublicResultToken = Guid.NewGuid().ToString("N")
                };
                _context.Payments.Add(payment);
                await _context.SaveChangesAsync(ct);

                var school = await _context.Schools.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == link.SchoolId, ct);
                var outcome = await _wavePayin.StartAsync(
                    payment, SchoolDisplayName.From(school).Primary(), ct);

                if (!outcome.Ok)
                {
                    _logger.LogWarning("[pay/abo] Session refusée Payment {Id} : {Msg}",
                        payment.Id, outcome.ErrorMessage);
                    return StatusCode(outcome.HttpStatus == 502 ? 502 : 400,
                        new { status = "error", message = outcome.ErrorMessage ?? "Le paiement n'a pas pu être lancé." });
                }
                if (string.IsNullOrWhiteSpace(outcome.RedirectUrl))
                {
                    return StatusCode(502, new { status = "error", message = "Wave n'a pas renvoyé de page de paiement. Réessayez dans un instant." });
                }

                _logger.LogInformation(
                    "[pay/abo] Payment {PaymentId} lancé depuis le lien {LinkId} — école {SchoolId}, {Amount} FCFA",
                    payment.Id, link.Id, link.SchoolId, payment.AmountFcfa);

                return Ok(new
                {
                    status = "redirect",
                    paymentId = payment.Id,
                    redirectUrl = outcome.RedirectUrl,
                    resultUrl = _wavePayin.ResultPageUrl(payment),
                    amountChargedFcfa = payment.AmountFcfa
                });
            }
            finally
            {
                gate.Release();
            }
        }

        // ===== Helpers =====

        private async Task<SubscriptionPaymentLink?> LookupAsync(string token, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length != 32 || !token.All(Uri.IsHexDigit))
                return null;
            return await _context.SubscriptionPaymentLinks
                .FirstOrDefaultAsync(l => l.Token == token, ct);
        }

        /// <summary>
        /// La facture d'abonnement en attente de cette école.
        /// </summary>
        /// <remarks>
        /// Il ne peut y en avoir qu'UNE : tant qu'un prélèvement échoue, le cycle
        /// n'avance pas, donc aucune nouvelle période n'est facturée. La plus
        /// ancienne est prise par précaution, pour que deux factures laissées par
        /// un incident ne fassent pas payer la mauvaise.
        /// </remarks>
        private Task<SubscriptionInvoice?> PendingInvoiceAsync(int schoolId, CancellationToken ct) =>
            _context.SubscriptionInvoices
                .Where(i => i.SchoolId == schoolId && i.Status == SubscriptionInvoiceStatus.Pending)
                .OrderBy(i => i.PeriodStart)
                .FirstOrDefaultAsync(ct);

        /// <summary>Paiement d'abonnement encore en cours pour cette école, s'il y en a un.</summary>
        private Task<Payment?> ResolvePendingPaymentAsync(int schoolId, CancellationToken ct)
        {
            var since = DateTime.UtcNow - PendingWindow;
            return _context.Payments
                .Where(p => p.SchoolId == schoolId
                            && p.Purpose == PaymentPurpose.Subscription
                            && p.Status == PaymentStatus.Pending
                            && p.InitiatedAt >= since)
                .OrderByDescending(p => p.Id)
                .FirstOrDefaultAsync(ct);
        }
    }
}
