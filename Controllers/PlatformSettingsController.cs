using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Options;
using Microsoft.Extensions.Options;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Réglages globaux de la plateforme (montants minimums + frais %),
    /// éditables uniquement par le SuperAdmin. Une seule ligne singleton —
    /// s'applique à toutes les écoles. Permet d'ajuster la tarification sans
    /// toucher au code source ni redéployer.
    /// </summary>
    [ApiController]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    [Route("api/platform-settings")]
    public class PlatformSettingsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly UploadSettings _uploads;
        private readonly ILogger<PlatformSettingsController> _logger;

        public PlatformSettingsController(
            AppDbContext context,
            IWebHostEnvironment env,
            IOptions<UploadSettings> uploads,
            ILogger<PlatformSettingsController> logger)
        {
            _context = context;
            _env = env;
            _uploads = uploads.Value;
            _logger = logger;
        }

        /// <summary>
        /// `GET /api/platform-settings/landing-image` — l'image d'illustration de
        /// la page d'accueil. **Public** : la landing est vue par des visiteurs
        /// sans compte.
        /// </summary>
        /// <remarks>
        /// Endpoint séparé et volontairement minuscule : le reste des réglages
        /// plateforme (tarifs, plafonds SMS, coupe-circuit) n'a rien à faire
        /// dans une réponse publique.
        /// </remarks>
        /// <summary>
        /// `GET /api/platform-settings/legal` — les mentions légales affichées
        /// sur les deux documents juridiques.
        /// </summary>
        [HttpGet("legal")]
        public async Task<ActionResult<ApiResponse<LegalMentionsDto>>> GetLegal(CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            return Ok(ApiResponse<LegalMentionsDto>.Ok(new LegalMentionsDto
            {
                CompanyName = s.LegalCompanyName,
                Form = s.LegalForm,
                Ninea = s.LegalNinea,
                Rccm = s.LegalRccm,
                Address = s.LegalAddress,
                Representative = s.LegalRepresentative,
                CdpNumber = s.LegalCdpNumber,
                ContactEmail = s.LegalContactEmail,
                ContactPhone = s.LegalContactPhone,
                Version = s.LegalVersion
            }));
        }

        /// <summary>
        /// `PUT /api/platform-settings/legal` — mettre à jour les mentions.
        /// </summary>
        /// <remarks>
        /// Chaque champ est appliqué tel quel, vide compris : ici, effacer une
        /// mention est une action légitime (une information erronée doit pouvoir
        /// disparaître de la page, et une valeur vide est simplement omise au
        /// rendu). Seule la VERSION résiste au vide : sans elle, les
        /// acceptations horodatées ne prouveraient plus rien.
        /// </remarks>
        [HttpPut("legal")]
        public async Task<ActionResult<ApiResponse<LegalMentionsDto>>> UpdateLegal(
            [FromBody] LegalMentionsDto dto, CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

            s.LegalCompanyName = Clean(dto.CompanyName);
            s.LegalForm = Clean(dto.Form);
            s.LegalNinea = Clean(dto.Ninea);
            s.LegalRccm = Clean(dto.Rccm);
            s.LegalAddress = Clean(dto.Address);
            s.LegalRepresentative = Clean(dto.Representative);
            s.LegalCdpNumber = Clean(dto.CdpNumber);
            s.LegalContactEmail = Clean(dto.ContactEmail);
            s.LegalContactPhone = Clean(dto.ContactPhone);
            if (!string.IsNullOrWhiteSpace(dto.Version))
                s.LegalVersion = dto.Version!.Trim();

            s.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("[platform] Mentions légales mises à jour (version {Version})", s.LegalVersion);

            return Ok(ApiResponse<LegalMentionsDto>.Ok(new LegalMentionsDto
            {
                CompanyName = s.LegalCompanyName,
                Form = s.LegalForm,
                Ninea = s.LegalNinea,
                Rccm = s.LegalRccm,
                Address = s.LegalAddress,
                Representative = s.LegalRepresentative,
                CdpNumber = s.LegalCdpNumber,
                ContactEmail = s.LegalContactEmail,
                ContactPhone = s.LegalContactPhone,
                Version = s.LegalVersion
            }, "Mentions légales enregistrées."));
        }

        [HttpGet("landing-image")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<LandingImageDto>>> GetLandingImage(
            CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            return Ok(ApiResponse<LandingImageDto>.Ok(
                new LandingImageDto { ImageUrl = s.LandingHeroImagePath }));
        }

        /// <summary>
        /// `PUT /api/platform-settings/landing-image` — remplacer l'image
        /// (base64), ou la retirer pour revenir à celle livrée avec l'app.
        /// </summary>
        [HttpPut("landing-image")]
        public async Task<ActionResult<ApiResponse<LandingImageDto>>> SetLandingImage(
            [FromBody] SetLandingImageDto dto, CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            var previous = s.LandingHeroImagePath;

            if (dto.Remove)
            {
                s.LandingHeroImagePath = null;
            }
            else
            {
                var decoded = FileUploadValidator.DecodeAndValidate(
                    dto.ImageBase64 ?? string.Empty,
                    _uploads.MaxPhotoSizeMb, _uploads.AllowedPhotoMimeTypes);
                if (decoded == null)
                    return BadRequest(ApiResponse<LandingImageDto>.Fail("Image invalide ou trop lourde."));

                var folder = Path.Combine(_env.WebRootPath, "uploads", "landing");
                Directory.CreateDirectory(folder);
                var fileName = $"{Guid.NewGuid():N}{decoded.Extension}";
                await System.IO.File.WriteAllBytesAsync(
                    Path.Combine(folder, fileName), decoded.Bytes, ct);
                s.LandingHeroImagePath = $"/uploads/landing/{fileName}";
            }

            await _context.SaveChangesAsync(ct);

            // L'ancienne image part APRÈS l'enregistrement : si celui-ci échoue,
            // on n'a pas détruit celle qui s'affiche encore.
            if (previous != null && previous != s.LandingHeroImagePath)
            {
                try
                {
                    var root = Path.GetFullPath(_env.WebRootPath);
                    var full = Path.GetFullPath(Path.Combine(_env.WebRootPath, previous.TrimStart('/')));
                    if (full.StartsWith(root) && System.IO.File.Exists(full))
                        System.IO.File.Delete(full);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[platform] Ancienne image d'accueil non supprimée (non bloquant)");
                }
            }

            _logger.LogInformation("[platform] Image d'accueil {Action}",
                dto.Remove ? "retirée" : "remplacée");
            return Ok(ApiResponse<LandingImageDto>.Ok(
                new LandingImageDto { ImageUrl = s.LandingHeroImagePath },
                dto.Remove ? "Image d'origine rétablie." : "Image d'accueil remplacée."));
        }

        // ================================================================
        // ===== Captures d'écran de la page publique (2026-09-16) =====
        // ================================================================

        /// <summary>
        /// `GET /api/platform-settings/landing-screenshots` — les captures
        /// montrées sur idara.sn. **Anonyme** : la page d'accueil est vue par
        /// des visiteurs sans compte.
        /// </summary>
        [HttpGet("landing-screenshots")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<LandingScreenshotsDto>>> GetLandingScreenshots(
            CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            return Ok(ApiResponse<LandingScreenshotsDto>.Ok(
                new LandingScreenshotsDto { ImageUrls = ReadScreenshots(s) }));
        }

        /// <summary>
        /// `PUT /api/platform-settings/landing-screenshots` — ajouter une
        /// capture (base64) ou en retirer une par son rang.
        /// </summary>
        /// <remarks>
        /// Trois au plus : au-delà, la section devient une galerie que personne
        /// ne fait défiler, et chaque image supplémentaire retarde l'affichage
        /// de la page sur une connexion lente.
        /// </remarks>
        [HttpPut("landing-screenshots")]
        public async Task<ActionResult<ApiResponse<LandingScreenshotsDto>>> SetLandingScreenshots(
            [FromBody] SetLandingScreenshotDto dto, CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            var list = ReadScreenshots(s);
            string? removed = null;

            if (dto.RemoveIndex is int idx)
            {
                if (idx < 0 || idx >= list.Count)
                    return BadRequest(ApiResponse<LandingScreenshotsDto>.Fail("Capture introuvable."));
                removed = list[idx];
                list.RemoveAt(idx);
            }
            else
            {
                if (list.Count >= MaxLandingScreenshots)
                    return BadRequest(ApiResponse<LandingScreenshotsDto>.Fail(
                        $"Trois captures au maximum. Retirez-en une avant d'en ajouter."));

                var decoded = FileUploadValidator.DecodeAndValidate(
                    dto.ImageBase64 ?? string.Empty,
                    _uploads.MaxPhotoSizeMb, _uploads.AllowedPhotoMimeTypes);
                if (decoded == null)
                    return BadRequest(ApiResponse<LandingScreenshotsDto>.Fail("Image invalide ou trop lourde."));

                var folder = Path.Combine(_env.WebRootPath, "uploads", "landing");
                Directory.CreateDirectory(folder);
                var fileName = $"{Guid.NewGuid():N}{decoded.Extension}";
                await System.IO.File.WriteAllBytesAsync(
                    Path.Combine(folder, fileName), decoded.Bytes, ct);
                list.Add($"/uploads/landing/{fileName}");
            }

            s.LandingScreenshotsJson = list.Count == 0
                ? null
                : System.Text.Json.JsonSerializer.Serialize(list);
            s.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            // Le fichier part APRÈS l'enregistrement — même raison que pour
            // l'image d'accueil : un échec d'écriture ne doit pas détruire ce
            // qui s'affiche encore.
            if (removed != null) DeleteUploadedFile(removed);

            _logger.LogInformation("[platform] Captures de la page d'accueil : {Count} au total", list.Count);
            return Ok(ApiResponse<LandingScreenshotsDto>.Ok(
                new LandingScreenshotsDto { ImageUrls = list },
                removed != null ? "Capture retirée." : "Capture ajoutée."));
        }

        /// <summary>Trois au plus (cf. remarques de l'endpoint).</summary>
        public const int MaxLandingScreenshots = 3;

        /// <summary>
        /// Lit la liste stockée. Une colonne illisible (JSON corrompu à la main)
        /// est traitée comme une absence de captures : la page publique doit
        /// s'afficher quoi qu'il arrive.
        /// </summary>
        private static List<string> ReadScreenshots(Models.PlatformSettings s)
        {
            if (string.IsNullOrWhiteSpace(s.LandingScreenshotsJson)) return new List<string>();
            try
            {
                return System.Text.Json.JsonSerializer
                           .Deserialize<List<string>>(s.LandingScreenshotsJson!)
                       ?? new List<string>();
            }
            catch (System.Text.Json.JsonException)
            {
                return new List<string>();
            }
        }

        /// <summary>Supprime un fichier d'upload (best-effort, défense path-traversal).</summary>
        private void DeleteUploadedFile(string relativePath)
        {
            try
            {
                var root = Path.GetFullPath(_env.WebRootPath);
                var full = Path.GetFullPath(Path.Combine(_env.WebRootPath, relativePath.TrimStart('/')));
                if (full.StartsWith(root) && System.IO.File.Exists(full))
                    System.IO.File.Delete(full);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[platform] Fichier {Path} non supprimé (non bloquant)", relativePath);
            }
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<PlatformSettingsDto>>> Get(CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            return Ok(ApiResponse<PlatformSettingsDto>.Ok(await BuildAsync(s, ct)));
        }

        [HttpPut]
        public async Task<ActionResult<ApiResponse<PlatformSettingsDto>>> Update(
            [FromBody] UpdatePlatformSettingsDto dto, CancellationToken ct)
        {
            // Garde-fou métier : le minimum SenePay absolu est de 200 FCFA, on
            // ne laisse pas descendre en dessous (un payin < 200 serait rejeté
            // par SenePay → l'école croirait pouvoir facturer moins).
            if (dto.MinPayinFcfa < 200)
                return BadRequest(ApiResponse<PlatformSettingsDto>.Fail(
                    "Le montant minimum de paiement ne peut pas être inférieur à 200 FCFA."));

            var s = await _context.GetPlatformSettingsAsync(ct);

            s.MinPayinFcfa = dto.MinPayinFcfa;
            s.MinWithdrawalFcfa = dto.MinWithdrawalFcfa;
            // Absent = inchangé (§140) : un client antérieur au 2026-09-13
            // n'envoie pas ces champs et ne doit rien écraser.
            if (dto.PayinProviderFeePercent.HasValue)
                s.PayinProviderFeePercent = dto.PayinProviderFeePercent.Value;
            if (dto.PayinOperatorFeePercentHt.HasValue)
                s.PayinOperatorFeePercentHt = dto.PayinOperatorFeePercentHt.Value;
            if (dto.PayoutOperatorFeePercentHt.HasValue)
                s.PayoutOperatorFeePercentHt = dto.PayoutOperatorFeePercentHt.Value;
            if (dto.FeeVatPercent.HasValue)
                s.FeeVatPercent = dto.FeeVatPercent.Value;
            s.SmsBilingual = dto.SmsBilingual;
            s.SubscriptionEnforcementEnabled = dto.SubscriptionEnforcementEnabled;
            s.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);

            // On trace aussi ce que ça donne pour une famille : c'est le seul
            // chiffre qu'elle verra, et le journal doit permettre de DATER un
            // changement de ce qu'on lui réclame.
            _logger.LogInformation(
                "[platform-settings] MAJ par SuperAdmin {UserId} : minPayin={MinPayin}, "
                + "minWithdraw={MinWithdraw}, encaissement={Provider}%+{OperatorIn}%HT, "
                + "décaissement={OperatorOut}%HT, TVA={Vat}% → configuré={Configured}",
                User.GetUserId(), s.MinPayinFcfa, s.MinWithdrawalFcfa,
                s.PayinProviderFeePercent, s.PayinOperatorFeePercentHt,
                s.PayoutOperatorFeePercentHt, s.FeeVatPercent, s.Fees.IsConfigured);

            return Ok(ApiResponse<PlatformSettingsDto>.Ok(
                await BuildAsync(s, ct), "Réglages mis à jour."));
        }

        /// <summary>
        /// Montants d'aperçu. Ce sont des MONTANTS, pas des taux : ils servent à
        /// montrer ce que les réglages donnent concrètement, et n'entrent dans
        /// aucun calcul de facturation.
        /// </summary>
        private static readonly long[] PreviewTargets = { 500, 5_000, 15_000, 50_000 };

        private async Task<PlatformSettingsDto> BuildAsync(
            Models.PlatformSettings s, CancellationToken ct)
        {
            var dto = Map(s);
            if (s.Fees.IsConfigured)
            {
                foreach (var target in PreviewTargets)
                {
                    var charged = s.Fees.ChargeFor(target);
                    var payin = s.Fees.PayinFeesFor(charged);
                    var payout = s.Fees.PayoutFeesFor(target);
                    dto.Preview.Add(new FeePreviewRowDto
                    {
                        TargetFcfa = target,
                        ChargedFcfa = charged,
                        PayinFeesFcfa = payin,
                        PayoutFeesFcfa = payout,
                        // Ce qui reste une fois l'école servie ET le retrait payé.
                        PlatformBalanceFcfa = charged - payin - target - payout,
                        MarkupPercent = Math.Round((charged - target) * 100.0 / target, 3),
                    });
                }
            }
            dto.Calibration = await MeasureFeesAsync(s, ct);
            return dto;
        }

        /// <summary>
        /// Confronte les frais que nos taux PRÉDISENT à ceux que le prestataire
        /// a réellement prélevés, paiement par paiement.
        /// </summary>
        /// <remarks>
        /// <para>🔎 <b>La comparaison porte sur des FRANCS, pas sur des taux</b>,
        /// et c'est délibéré. Un taux moyen lisse exactement ce qu'on cherche à
        /// voir : les arrondis. C'est en comparant franc à franc qu'on a
        /// découvert que la règle du prestataire n'était pas un pourcentage mais
        /// <c>round(C × 3,6 %) + ceil(ceil(C × 1,5 %) × 1,18)</c> — 197/197
        /// exacts là où le meilleur pourcentage unique n'en expliquait que 6.</para>
        ///
        /// <para>⚠️ <b>Les espèces sont exclues</b> : un paiement au guichet a des
        /// frais nuls par construction (§182), et les inclure ferait croire le
        /// prestataire moins cher à mesure que l'école encaisse au comptant.</para>
        ///
        /// <para>⚠️ <c>FeesFcfa</c> n'est PAS fiable et n'est pas utilisé : le
        /// webhook y met tantôt la seule part du prestataire, tantôt le total
        /// (constaté sur 8 paiements). <c>NetCreditedFcfa</c>, lui, est cohérent
        /// — c'est donc <c>débité − net</c> qui fait foi.</para>
        /// </remarks>
        private async Task<FeeCalibrationDto> MeasureFeesAsync(
            Models.PlatformSettings s, CancellationToken ct)
        {
            var result = new FeeCalibrationDto();
            var configured = s.Fees.IsConfigured;

            // --- Encaissement ---
            var payins = await _context.Payments
                .Where(p => p.Status == Enums.PaymentStatus.Completed
                            && p.Operator != Enums.PaymentOperator.Cash
                            && p.AmountFcfa > 0
                            && p.NetCreditedFcfa > 0)
                .Select(p => new { p.AmountFcfa, p.NetCreditedFcfa })
                .ToListAsync(ct);

            result.PayinSampleCount = payins.Count;
            if (payins.Count > 0)
            {
                long gross = 0, taken = 0;
                foreach (var p in payins)
                {
                    var reel = p.AmountFcfa - p.NetCreditedFcfa;
                    gross += p.AmountFcfa;
                    taken += reel;
                    if (!configured) continue;
                    var predit = s.Fees.PayinFeesFor(p.AmountFcfa);
                    if (predit == reel) result.PayinExactCount++;
                    result.PayinGapFcfa += predit - reel;
                }
                if (gross > 0)
                    result.ObservedPayinPercent = Math.Round(taken * 100.0 / gross, 3);
            }

            // --- Décaissement ---
            var payouts = await _context.Withdrawals
                .Where(w => w.Status == Enums.WithdrawalStatus.Completed
                            && w.AmountFcfa > 0
                            && w.FeesFcfa > 0)
                .Select(w => new { w.AmountFcfa, w.FeesFcfa })
                .ToListAsync(ct);

            result.PayoutSampleCount = payouts.Count;
            if (payouts.Count > 0)
            {
                long sent = 0, fees = 0;
                foreach (var w in payouts)
                {
                    sent += w.AmountFcfa;
                    fees += w.FeesFcfa;
                    if (!configured) continue;
                    var predit = s.Fees.PayoutFeesFor(w.AmountFcfa);
                    if (predit == w.FeesFcfa) result.PayoutExactCount++;
                    result.PayoutGapFcfa += predit - w.FeesFcfa;
                }
                if (sent > 0)
                    result.ObservedPayoutPercent = Math.Round(fees * 100.0 / sent, 3);
            }

            return result;
        }

        private static PlatformSettingsDto Map(Models.PlatformSettings s) => new()
        {
            MinPayinFcfa = s.MinPayinFcfa,
            MinWithdrawalFcfa = s.MinWithdrawalFcfa,
            PayinProviderFeePercent = s.PayinProviderFeePercent,
            PayinOperatorFeePercentHt = s.PayinOperatorFeePercentHt,
            PayoutOperatorFeePercentHt = s.PayoutOperatorFeePercentHt,
            FeeVatPercent = s.FeeVatPercent,
            FeesConfigured = s.Fees.IsConfigured,
            SmsBilingual = s.SmsBilingual,
            SubscriptionEnforcementEnabled = s.SubscriptionEnforcementEnabled,
            UpdatedAt = s.UpdatedAt
        };
    }

    /// <summary>L'image d'illustration de la page d'accueil. `null` = celle de l'app.</summary>
    public class LandingImageDto
    {
        public string? ImageUrl { get; set; }
    }

    public class LandingScreenshotsDto
    {
        public List<string> ImageUrls { get; set; } = new();
    }

    public class SetLandingScreenshotDto
    {
        /// <summary>Capture à ajouter, encodée en base64 (préfixe `data:` accepté).</summary>
        public string? ImageBase64 { get; set; }

        /// <summary>Rang de la capture à retirer. Renseigné, il l'emporte sur l'ajout.</summary>
        public int? RemoveIndex { get; set; }
    }

    public class SetLandingImageDto
    {
        /// <summary>Image encodée en base64 (préfixe `data:` accepté).</summary>
        public string? ImageBase64 { get; set; }

        /// <summary>`true` = revenir à l'image livrée avec l'application.</summary>
        public bool Remove { get; set; }
    }

    /// <summary>Mentions affichées en tête des deux documents juridiques.</summary>
    public class LegalMentionsDto
    {
        public string? CompanyName { get; set; }
        public string? Form { get; set; }
        public string? Ninea { get; set; }
        public string? Rccm { get; set; }
        public string? Address { get; set; }
        public string? Representative { get; set; }
        public string? CdpNumber { get; set; }
        public string? ContactEmail { get; set; }
        public string? ContactPhone { get; set; }
        public string? Version { get; set; }
    }
}
