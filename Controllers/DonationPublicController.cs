using System.Collections.Concurrent;
using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Data;
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
    /// Page PUBLIQUE d'une collecte de dons (<c>/don/{slug}</c>), ouverte depuis
    /// WhatsApp SANS compte.
    ///
    /// <para>Calquée sur la page du lien de paiement parent, à trois différences
    /// près, toutes dictées par la nature d'un don :</para>
    /// <list type="number">
    /// <item><b>L'identité est déclarée, jamais vérifiée.</b> C'est Wave qui
    /// authentifie le payeur ; nous n'affichons qu'un nom saisi.</item>
    /// <item><b>Plusieurs personnes donnent en même temps.</b> Le blocage « un
    /// seul paiement en cours » ne peut donc pas porter sur la collecte — il
    /// porte sur le NUMÉRO du donateur, sinon le premier don en attente
    /// fermerait la collecte à tout le quartier.</item>
    /// <item><b>L'adresse est lisible</b> (<c>salle-bleue</c>), donc devinable.
    /// C'est assumé : la page n'expose que ce que l'école a publié — aucune
    /// donnée d'élève, aucune dette de famille.</item>
    /// </list>
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("don")]
    public class DonationPublicController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IDonationCampaignService _campaigns;

        /// <summary>
        /// ⚠️ Le client de paiement est résolu À LA DEMANDE, jamais injecté.
        /// </summary>
        /// <remarks>
        /// Trouvé au premier appel réel, pas à la compilation : <c>ISenePayClient</c>
        /// est un HttpClient typé dont la fabrique LÈVE si les clés du prestataire
        /// manquent. Injecté au constructeur, il faisait échouer en <b>500</b> la
        /// simple <i>lecture</i> d'une collecte — un donateur ne pouvait même plus
        /// voir la page. Or lire une collecte ne demande rien au prestataire :
        /// seul le démarrage d'un don en a besoin, et c'est là, et là seulement,
        /// que l'absence de configuration doit se voir.
        /// </remarks>
        private readonly IServiceProvider _services;

        private readonly IMemoryCache _cache;
        private readonly SenePaySettings _senepaySettings;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<DonationPublicController> _logger;

        /// <summary>Au-delà, un Pending n'est plus bloquant : SenePay l'a tranché depuis longtemps.</summary>
        private static readonly TimeSpan PendingWindow = TimeSpan.FromHours(6);
        private static readonly TimeSpan StartWindow = TimeSpan.FromMinutes(15);

        /// <summary>Anti-rafale par NUMÉRO : trois tentatives par quart d'heure suffisent à n'importe quel donateur.</summary>
        private const int MaxStartsPerPhone = 3;

        /// <summary>Anti-rafale par COLLECTE : large, parce qu'une collecte qui marche voit des dons simultanés.</summary>
        private const int MaxStartsPerCampaign = 40;

        /// <summary>Un verrou par (collecte, numéro) : deux onglets ne créent pas deux dons.</summary>
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> StartGates = new();

        public DonationPublicController(
            AppDbContext context,
            IDonationCampaignService campaigns,
            IServiceProvider services,
            IMemoryCache cache,
            IOptions<SenePaySettings> senepaySettings,
            IWebHostEnvironment env,
            ILogger<DonationPublicController> logger)
        {
            _context = context;
            _campaigns = campaigns;
            _services = services;
            _cache = cache;
            _senepaySettings = senepaySettings.Value;
            _env = env;
            _logger = logger;
        }

        /// <summary>`GET /don/{slug}` — la page HTML (statique ; le JS lit l'état).</summary>
        [HttpGet("{slug}")]
        public IActionResult GetPage(string slug)
        {
            var pagePath = Path.Combine(_env.WebRootPath, "don", "campaign.html");
            if (!System.IO.File.Exists(pagePath))
            {
                _logger.LogError("[don] campaign.html introuvable à {Path}", pagePath);
                return NotFound("Page indisponible.");
            }
            Response.Headers.CacheControl = "no-store";
            return PhysicalFile(pagePath, "text/html; charset=utf-8");
        }

        /// <summary>`GET /don/{slug}/state` — la collecte, son total, son mur.</summary>
        [HttpGet("{slug}/state")]
        public async Task<IActionResult> GetState(string slug, CancellationToken ct)
        {
            var campaign = await LookupAsync(slug, ct);
            if (campaign == null) return NotFound(new { status = "not_found" });

            // Anti-martèlement : 60 lectures / collecte / minute couvrent
            // largement le rafraîchissement d'une page qui attend un paiement.
            var stateKey = $"don:state:{campaign.Id}";
            var reads = _cache.GetOrCreate(stateKey, e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                return 0;
            });
            if (reads >= 120)
                return StatusCode(429, new { status = "rate_limited", message = "Trop de requêtes. Patientez une minute." });
            _cache.Set(stateKey, reads + 1, TimeSpan.FromMinutes(1));

            // Une « ouverture » est une visite, pas chaque rafraîchissement :
            // on ne compte qu'une fois par minute, comme le lien parent.
            if (campaign.LastOpenedAt == null || campaign.LastOpenedAt < DateTime.UtcNow.AddMinutes(-1))
            {
                campaign.LastOpenedAt = DateTime.UtcNow;
                campaign.OpenCount++;
                await _context.SaveChangesAsync(ct);
            }

            return Ok(await BuildStateAsync(campaign, ct));
        }

        public class StartDonationRequest
        {
            /// <summary>Nom complet du donateur, ou du représentant pour une organisation.</summary>
            public string? DonorName { get; set; }
            public string? DonorPhone { get; set; }

            /// <summary>Renseigné = don au nom d'une organisation.</summary>
            public string? Organization { get; set; }

            public bool Anonymous { get; set; }

            /// <summary>Ignoré si la collecte impose un montant.</summary>
            public long AmountFcfa { get; set; }
        }

        /// <summary>
        /// `POST /don/{slug}/start` — crée le don et renvoie l'URL Wave.
        /// </summary>
        [HttpPost("{slug}/start")]
        public async Task<IActionResult> Start(string slug, [FromBody] StartDonationRequest body, CancellationToken ct)
        {
            var campaign = await LookupAsync(slug, ct);
            if (campaign == null) return NotFound(new { status = "not_found" });

            if (!campaign.IsOpen)
            {
                return Ok(new
                {
                    status = "closed",
                    message = campaign.Status == DonationCampaignStatus.Paused
                        ? "Cette collecte est momentanément suspendue."
                        : "Cette collecte est terminée. Merci à tous ceux qui y ont participé."
                });
            }

            // ----- Identité déclarée -----
            var donorName = (body?.DonorName ?? string.Empty).Trim();
            if (donorName.Length < 3 || donorName.Length > 120)
                return BadRequest(new { status = "error", message = "Indiquez votre nom complet." });

            var phone = SenegalPhone.Normalize(body?.DonorPhone);
            if (phone == null)
                return BadRequest(new { status = "error", message = "Numéro de téléphone invalide." });

            var organization = string.IsNullOrWhiteSpace(body?.Organization) ? null : body!.Organization!.Trim();
            if (organization is { Length: > 120 })
                return BadRequest(new { status = "error", message = "Le nom de l'organisation est trop long." });

            // ----- Montant -----
            var platform = await _context.GetPlatformSettingsAsync(ct);
            long targetAmount;
            if (campaign.AmountMode == DonationAmountMode.Fixed)
            {
                // Le montant vient de la collecte, JAMAIS du corps de la requête :
                // un client modifié ne doit pas pouvoir donner 1 F sur un lien à
                // 25 000 F et repartir avec le reçu correspondant.
                targetAmount = campaign.FixedAmountFcfa ?? 0;
                if (targetAmount <= 0)
                    return StatusCode(500, new { status = "error", message = "Cette collecte est mal configurée. Contactez le daara." });
            }
            else
            {
                targetAmount = body?.AmountFcfa ?? 0;
                if (targetAmount < platform.MinDonationFcfa)
                    return BadRequest(new { status = "error", message = $"Le don minimum est de {platform.MinDonationFcfa} FCFA." });
                if (targetAmount > platform.MaxDonationFcfa)
                {
                    return BadRequest(new
                    {
                        status = "error",
                        message = $"Pour un don supérieur à {platform.MaxDonationFcfa:N0} FCFA, appelez directement le daara — il vous accompagnera."
                    });
                }
            }

            // ----- Anti-rafale : par numéro d'abord, par collecte ensuite -----
            var phoneKey = $"don:start:{campaign.Id}:{phone}";
            var phoneCount = _cache.GetOrCreate(phoneKey, e => { e.AbsoluteExpirationRelativeToNow = StartWindow; return 0; });
            if (phoneCount >= MaxStartsPerPhone)
                return StatusCode(429, new { status = "rate_limited", message = "Trop de tentatives. Réessayez dans quelques minutes." });

            var campaignKey = $"don:start:{campaign.Id}";
            var campaignCount = _cache.GetOrCreate(campaignKey, e => { e.AbsoluteExpirationRelativeToNow = StartWindow; return 0; });
            if (campaignCount >= MaxStartsPerCampaign)
                return StatusCode(429, new { status = "rate_limited", message = "Beaucoup de dons en ce moment. Réessayez dans une minute." });

            var gate = StartGates.GetOrAdd($"{campaign.Id}:{phone}", _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(TimeSpan.FromSeconds(10), ct))
                return StatusCode(429, new { status = "rate_limited", message = "Un don est déjà en cours. Réessayez." });

            try
            {
                // Un don en attente pour CE numéro sur CETTE collecte bloque,
                // après vérification live auprès de SenePay : celui qui a
                // abandonné sur Wave ne doit pas rester coincé six heures.
                var pending = await ResolvePendingAsync(campaign.Id, phone, ct);
                if (pending != null)
                {
                    return Conflict(new
                    {
                        status = "pending",
                        message = "Un don est déjà en cours pour ce numéro. Terminez-le ou patientez avant de recommencer.",
                        pendingPaymentId = pending.Id,
                        pendingResultUrl = ResultUrl(pending)
                    });
                }

                _cache.Set(phoneKey, phoneCount + 1, StartWindow);
                _cache.Set(campaignKey, campaignCount + 1, StartWindow);

                // Le portefeuille de l'école doit exister avant tout crédit.
                await _context.EnsurePaymentFoundationsAsync(campaign.SchoolId, ct);

                // FeesPayer.Parent → le donateur paie la majoration, le daara
                // reçoit le montant plein (§82). School → le donateur paie le
                // montant exact, le daara reçoit le net (§106). La collecte porte
                // le choix figé à sa création : changer le réglage global ne doit
                // pas modifier un lien déjà partagé.
                var amountToCharge = campaign.FeesPayer == FeesPayer.Parent
                    ? (long)Math.Ceiling(targetAmount * platform.ParentFeeMultiplier)
                    : targetAmount;

                var payment = new Payment
                {
                    SchoolId = campaign.SchoolId,
                    StudentId = null,
                    GuardianId = null,
                    InvoiceId = null,
                    Purpose = PaymentPurpose.Donation,
                    DonorId = null,                       // don SANS compte
                    DonationCampaignId = campaign.Id,
                    DonorName = donorName,
                    DonorPhone = phone,
                    DonorOrganization = organization,
                    DonorAnonymous = body?.Anonymous ?? false,
                    AmountFcfa = amountToCharge,
                    TargetAmountFcfa = targetAmount,
                    FeesFcfa = 0,
                    NetCreditedFcfa = 0,
                    Operator = PaymentOperator.Wave,
                    FeesPayer = campaign.FeesPayer,
                    Status = PaymentStatus.Pending,
                    InitiatedAt = DateTime.UtcNow,
                    PublicResultToken = Guid.NewGuid().ToString("N")
                };
                _context.Payments.Add(payment);
                await _context.SaveChangesAsync(ct);

                SenePayInitiatePaymentResponse resp;
                try
                {
                    var senepay = _services.GetRequiredService<ISenePayClient>();
                    resp = await senepay.InitiatePaymentAsync(BuildSenePayRequest(payment, phone, donorName), ct);
                }
                catch (InvalidOperationException ex)
                {
                    // Prestataire non configure : le don ne part pas, mais on le
                    // dit franchement plutot que de renvoyer une page en panne.
                    _logger.LogError(ex, "[don] Prestataire de paiement non configure — don {PaymentId} impossible", payment.Id);
                    return StatusCode(503, new { status = "error", message = "Les paiements sont momentanement indisponibles. Reessayez plus tard." });
                }
                catch (SenePayApiException ex)
                {
                    _logger.LogError(ex, "[don] SenePay indisponible pour le don {PaymentId}", payment.Id);
                    return StatusCode(502, new { status = "error", message = "Wave est momentanément injoignable. Réessayez dans un instant." });
                }

                payment.SenePayInternalId = resp.InternalId;
                payment.SenePayTransactionId = resp.Token;
                await _context.SaveChangesAsync(ct);

                if (string.IsNullOrWhiteSpace(resp.RedirectUrl))
                {
                    // Sans URL Wave, personne n'a pu payer. Le Pending sera
                    // tranché par le webhook ou le poll — jamais par nous (§78).
                    _logger.LogWarning("[don] Pas d'URL Wave pour le don {PaymentId} (statut {Status})", payment.Id, resp.Status);
                    return StatusCode(502, new { status = "error", message = "Wave n'a pas renvoyé de page de paiement. Réessayez." });
                }

                _logger.LogInformation("[don] Don {PaymentId} lancé sur la collecte {Slug} ({Amount} FCFA)",
                    payment.Id, campaign.Slug, payment.AmountFcfa);

                return Ok(new
                {
                    status = "redirect",
                    paymentId = payment.Id,
                    redirectUrl = resp.RedirectUrl,
                    resultUrl = ResultUrl(payment),
                    amountChargedFcfa = payment.AmountFcfa
                });
            }
            finally
            {
                gate.Release();
            }
        }

        // ====================================================================
        // ===== Helpers =====
        // ====================================================================

        private async Task<DonationCampaign?> LookupAsync(string slug, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(slug) || slug.Length > 80) return null;
            return await _context.DonationCampaigns
                .Include(c => c.School)
                .FirstOrDefaultAsync(c => c.Slug == slug, ct);
        }

        private async Task<Payment?> ResolvePendingAsync(int campaignId, string phone, CancellationToken ct)
        {
            var since = DateTime.UtcNow - PendingWindow;
            var pendingIds = await _context.Payments
                .Where(p => p.DonationCampaignId == campaignId
                            && p.DonorPhone == phone
                            && p.Status == PaymentStatus.Pending
                            && p.InitiatedAt >= since)
                .OrderByDescending(p => p.InitiatedAt)
                .Select(p => p.Id)
                .ToListAsync(ct);

            if (pendingIds.Count == 0) return null;

            // Meme raison que ci-dessus : ce job parle au prestataire, on ne le
            // resout que si un Pending existe vraiment.
            var payinVerification = _services.GetRequiredService<PayinVerificationJob>();

            Payment? stillPending = null;
            foreach (var id in pendingIds)
            {
                PaymentStatus? status;
                try
                {
                    status = await payinVerification.VerifyPaymentNowAsync(id, ct);
                }
                catch (Exception ex)
                {
                    // Le prestataire n'a pas repondu (panne, cles absentes, reseau).
                    // On ne CONCLUT rien sur un etat ambigu (§78) : le don en
                    // attente reste bloquant, et le donateur est renvoye vers sa
                    // page de resultat plutot que de relancer un second paiement.
                    _logger.LogWarning(ex,
                        "[don] Verification live impossible pour le paiement {PaymentId} — traite comme encore en attente", id);
                    status = PaymentStatus.Pending;
                }
                if (status != PaymentStatus.Pending || stillPending != null) continue;
                var p = await _context.Payments.AsNoTracking().FirstAsync(x => x.Id == id, ct);
                // Pending SANS jeton SenePay = l'initiation a échoué avant toute
                // redirection : le donateur n'a jamais vu Wave, aucun double
                // paiement possible (même raisonnement que le lien parent).
                if (string.IsNullOrWhiteSpace(p.SenePayTransactionId)
                    && p.InitiatedAt < DateTime.UtcNow.AddMinutes(-2))
                    continue;
                stillPending = p;
            }
            return stillPending;
        }

        private async Task<object> BuildStateAsync(DonationCampaign campaign, CancellationToken ct)
        {
            var totals = await _campaigns.GetTotalsAsync(campaign.Id, ct);
            var platform = await _context.GetPlatformSettingsAsync(ct);

            // Mur des donateurs : ce que le PUBLIC voit. Le nom de famille est
            // réduit à son initiale, et un don marqué anonyme perd son nom ici —
            // mais jamais dans l'écran de l'école, qui voit toujours qui a donné.
            List<object> wall = new();
            if (campaign.ShowDonorWall)
            {
                var recent = await _context.Payments
                    .Where(p => p.DonationCampaignId == campaign.Id && p.Status == PaymentStatus.Completed)
                    .OrderByDescending(p => p.PaidAt)
                    .Take(20)
                    .Select(p => new { p.DonorName, p.DonorOrganization, p.DonorAnonymous, p.TargetAmountFcfa, p.PaidAt })
                    .ToListAsync(ct);

                wall = recent.Select(d => (object)new
                {
                    name = d.DonorAnonymous
                        ? null
                        : (d.DonorOrganization ?? PublicName(d.DonorName)),
                    anonymous = d.DonorAnonymous,
                    amountFcfa = d.TargetAmountFcfa,
                    paidAt = d.PaidAt
                }).ToList();
            }

            var goal = campaign.GoalAmountFcfa;
            return new
            {
                status = campaign.IsOpen ? "open"
                    : campaign.Status == DonationCampaignStatus.Paused ? "paused" : "closed",
                school = new
                {
                    name = campaign.School.Name,
                    nameAr = campaign.School.NameAr,
                    logoUrl = campaign.School.LogoUrl,
                    phone = campaign.School.PhoneNumber
                },
                campaign = new
                {
                    name = campaign.Name,
                    description = campaign.Description,
                    coverImageUrl = campaign.CoverImagePath,
                    goalAmountFcfa = goal,
                    collectedFcfa = totals.CollectedFcfa,
                    progressPercent = goal is > 0 ? (int)Math.Min(999, totals.CollectedFcfa * 100 / goal.Value) : (int?)null,
                    donationCount = totals.DonationCount,
                    amountMode = campaign.AmountMode == DonationAmountMode.Fixed ? "fixed" : "free",
                    fixedAmountFcfa = campaign.FixedAmountFcfa,
                    suggestedAmounts = campaign.SuggestedAmounts(),
                    closesAt = campaign.ClosesAt,
                    daysLeft = campaign.ClosesAt is { } end && end > DateTime.UtcNow
                        ? (int)Math.Ceiling((end - DateTime.UtcNow).TotalDays)
                        : (int?)null,
                    showDonorWall = campaign.ShowDonorWall
                },
                // Les frais ne s'affichent QUE si le donateur les paie : quand
                // l'école les absorbe, ils ne le regardent pas, et les montrer
                // ferait douter de ce qui arrive au daara.
                fees = campaign.FeesPayer == FeesPayer.Parent
                    ? new { payerPays = true, multiplier = platform.ParentFeeMultiplier }
                    : null,
                limits = new
                {
                    minFcfa = platform.MinDonationFcfa,
                    maxFcfa = platform.MaxDonationFcfa
                },
                donors = wall
            };
        }

        /// <summary>« Abdou Salam Lo » → « Abdou Salam L. ». Le mur nomme sans exposer.</summary>
        private static string? PublicName(string? fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return null;
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0];
            return string.Join(' ', parts[..^1]) + " " + char.ToUpperInvariant(parts[^1][0]) + ".";
        }

        private string ResultUrl(Payment payment) =>
            $"{_senepaySettings.PublicBaseUrl.TrimEnd('/')}/pay/{payment.Id}/{payment.PublicResultToken}";

        private SenePayInitiatePaymentRequest BuildSenePayRequest(Payment payment, string payerPhone, string donorName)
        {
            var resultBase = ResultUrl(payment);
            return new SenePayInitiatePaymentRequest
            {
                Amount = payment.AmountFcfa,
                Currency = "XOF",
                CountryCode = "SN",
                Operator = "wave",
                CustomerPhone = PaymentPhone.ForSenePay(payerPhone),
                OtpCode = null,
                OrderId = payment.Id.ToString(),
                CustomerName = donorName,
                WebhookUrl = _senepaySettings.WebhookPayinUrl,
                // ⚠️ returnUrl, PAS successUrl : l'API Direct ignore successUrl et
                // le donateur retomberait sur une page blanche (§66).
                ReturnUrl = $"{resultBase}?status=success",
                CancelUrl = $"{resultBase}?status=cancel"
            };
        }
    }
}
