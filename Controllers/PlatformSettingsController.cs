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

        [HttpGet]
        public async Task<ActionResult<ApiResponse<PlatformSettingsDto>>> Get(CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            var dto = Map(s);
            dto.Calibration = await MeasureFeesAsync(s, ct);
            return Ok(ApiResponse<PlatformSettingsDto>.Ok(dto));
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
                    "Le montant minimum de paiement ne peut pas être inférieur à 200 FCFA (contrainte SenePay)."));

            var s = await _context.GetPlatformSettingsAsync(ct);

            s.MinPayinFcfa = dto.MinPayinFcfa;
            s.MinWithdrawalFcfa = dto.MinWithdrawalFcfa;
            // Absent = inchangé (§140) : un client antérieur au 2026-09-13
            // n'envoie pas ces champs et ne doit rien écraser.
            if (dto.PayinFeePercent.HasValue) s.PayinFeePercent = dto.PayinFeePercent.Value;
            if (dto.PayoutFeePercent.HasValue) s.PayoutFeePercent = dto.PayoutFeePercent.Value;
            s.SmsBilingual = dto.SmsBilingual;
            s.SubscriptionEnforcementEnabled = dto.SubscriptionEnforcementEnabled;
            s.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);

            // La majoration est tracée elle aussi, bien qu'elle ne soit plus
            // saisie : c'est le chiffre que les familles verront, et le journal
            // doit permettre de dater un changement de ce qu'on leur demande.
            _logger.LogInformation(
                "[platform-settings] MAJ par SuperAdmin {UserId} : minPayin={MinPayin}, minWithdraw={MinWithdraw}, payinFee={PayinFee}%, payoutFee={PayoutFee}% -> majoration déduite {ParentFee}%",
                User.GetUserId(), s.MinPayinFcfa, s.MinWithdrawalFcfa, s.PayinFeePercent, s.PayoutFeePercent,
                Math.Round(s.ParentFeePercent, 3));

            var updated = Map(s);
            updated.Calibration = await MeasureFeesAsync(s, ct);
            return Ok(ApiResponse<PlatformSettingsDto>.Ok(updated, "Réglages mis à jour."));
        }

        /// <summary>
        /// Confronte les taux SAISIS à ce que le prestataire a réellement
        /// prélevé, lu dans les paiements et les retraits déjà réglés.
        /// </summary>
        /// <remarks>
        /// <para>🔎 <b>Pourquoi cette mesure vit ici et pas dans une note.</b>
        /// Une majoration mal calibrée ne provoque aucune erreur : elle se paie.
        /// La seule façon de la voir est de comparer, en continu, ce qu'on
        /// demande au payeur à ce que le prestataire prend. C'est ce qui a
        /// manqué pendant quatre mois.</para>
        ///
        /// <para>⚠️ <b>Les espèces sont exclues du calcul de l'encaissement</b> :
        /// un paiement au guichet a des frais nuls par construction (§182), et
        /// les inclure ferait mécaniquement baisser le taux mesuré — on croirait
        /// SenePay moins cher à mesure que l'école encaisse au comptant.</para>
        ///
        /// <para>⚠️ <c>SumAsync</c> porte sur un type NULLABLE : sur un
        /// ensemble vide, SQL renvoie NULL et EF échoue à le matérialiser en
        /// <c>long</c> (§195). Une plateforme neuve n'a aucun paiement.</para>
        /// </remarks>
        private async Task<FeeCalibrationDto> MeasureFeesAsync(
            Models.PlatformSettings s, CancellationToken ct)
        {
            var result = new FeeCalibrationDto();

            // --- Encaissement : (brut - net crédité) / brut, hors espèces ---
            var payins = _context.Payments.Where(p =>
                p.Status == Enums.PaymentStatus.Completed
                && p.Operator != Enums.PaymentOperator.Cash
                && p.AmountFcfa > 0
                && p.NetCreditedFcfa > 0);

            result.PayinSampleCount = await payins.CountAsync(ct);
            if (result.PayinSampleCount > 0)
            {
                var gross = await payins.SumAsync(p => (long?)p.AmountFcfa, ct) ?? 0L;
                var net = await payins.SumAsync(p => (long?)p.NetCreditedFcfa, ct) ?? 0L;
                if (gross > 0)
                    result.MeasuredPayinFeePercent = Math.Round((gross - net) * 100.0 / gross, 3);
            }

            // --- Décaissement : frais / montant retiré ---
            var payouts = _context.Withdrawals.Where(w =>
                w.Status == Enums.WithdrawalStatus.Completed
                && w.AmountFcfa > 0
                && w.FeesFcfa > 0);

            result.PayoutSampleCount = await payouts.CountAsync(ct);
            if (result.PayoutSampleCount > 0)
            {
                var sent = await payouts.SumAsync(w => (long?)w.AmountFcfa, ct) ?? 0L;
                var fees = await payouts.SumAsync(w => (long?)w.FeesFcfa, ct) ?? 0L;
                if (sent > 0)
                    result.MeasuredPayoutFeePercent = Math.Round(fees * 100.0 / sent, 3);
            }

            // --- La majoration qui SERAIT neutre d'après ces mesures ---
            // Même formule que ParentFeeMultiplier, appliquée aux taux mesurés
            // plutôt qu'aux taux saisis. Si l'un des deux manque (plateforme
            // neuve, aucun retrait encore réglé), on retombe sur le taux saisi :
            // afficher un écart calculé sur une moitié de mesure serait pire que
            // ne rien afficher.
            var a = (result.MeasuredPayinFeePercent ?? s.PayinFeePercent) / 100.0;
            var b = (result.MeasuredPayoutFeePercent ?? s.PayoutFeePercent) / 100.0;
            if (a is >= 0 and < 0.95 && b is >= 0 and < 0.95)
            {
                var neutral = ((1.0 + b) / (1.0 - a) - 1.0) * 100.0;
                result.MeasuredNeutralParentFeePercent = Math.Round(neutral, 3);

                // Ce que l'écart pèse sur 1 000 000 F facturés aux familles.
                // Négatif = la plateforme avance la différence.
                const long reference = 1_000_000L;
                var charged = reference * (1.0 + s.ParentFeePercent / 100.0);
                var cost = reference * (1.0 + b) / (1.0 - a);
                result.GapPerMillionFcfa = (long)Math.Round(charged - cost, MidpointRounding.AwayFromZero);
            }

            return result;
        }

        private static PlatformSettingsDto Map(Models.PlatformSettings s) => new()
        {
            MinPayinFcfa = s.MinPayinFcfa,
            MinWithdrawalFcfa = s.MinWithdrawalFcfa,
            PayinFeePercent = s.PayinFeePercent,
            PayoutFeePercent = s.PayoutFeePercent,
            ParentFeePercent = Math.Round(s.ParentFeePercent, 3),
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
