using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Donation;
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
    /// Collectes de dons, côté ÉCOLE — l'écran « Dons et collectes » de l'onglet
    /// Argent.
    ///
    /// <para><b>Lire est ouvert à l'équipe, écrire est réservé à la direction</b>
    /// (décision produit du 2026-09-03). Ouvrir une collecte, c'est encaisser de
    /// l'argent au nom du daara : un acte de direction, comme le retrait. Le
    /// personnel et l'observateur voient les dons reçus, ils n'ouvrent ni ne
    /// ferment rien.</para>
    ///
    /// <para>⚠️ Le <c>[Authorize]</c> de classe ne suffit jamais à protéger un
    /// endpoint monétaire : chaque méthode d'écriture porte son propre
    /// <c>[Authorize(Roles = SchoolAdmin)]</c> (§167).</para>
    /// </summary>
    [ApiController]
    [Route("api/donation-campaigns")]
    [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.SchoolViewer}")]
    public class DonationCampaignsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IDonationCampaignService _campaigns;
        private readonly SenePaySettings _senepaySettings;
        private readonly UploadSettings _uploads;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<DonationCampaignsController> _logger;

        public DonationCampaignsController(
            AppDbContext context,
            IDonationCampaignService campaigns,
            IOptions<SenePaySettings> senepaySettings,
            IOptions<UploadSettings> uploads,
            IWebHostEnvironment env,
            ILogger<DonationCampaignsController> logger)
        {
            _context = context;
            _campaigns = campaigns;
            _senepaySettings = senepaySettings.Value;
            _uploads = uploads.Value;
            _env = env;
            _logger = logger;
        }

        // ====================================================================
        // ===== Lecture =====
        // ====================================================================

        /// <summary>
        /// `GET /api/donation-campaigns` — les collectes de l'école, la
        /// permanente en tête, les actives avant les closes.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<ApiResponse<DonationCampaignsResponse>>> List(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Forbid();

            // 🔴 C'est le SERVEUR qui dit ce que l'utilisateur peut faire. L'écran
            // testait le rôle de son côté et faisait disparaître le bouton
            // « Nouvelle collecte » sans un mot dès que ce test échouait — un
            // utilisateur devant un écran muet n'a aucun moyen de comprendre.
            var canManage = User.GetRole() == UserRoles.SchoolAdmin;

            // La collecte permanente naît ici, à la première visite : aucune
            // école existante n'a besoin d'être migrée (§175). Réservé à la
            // direction — l'observateur ne doit rien créer en consultant.
            if (canManage)
            {
                var userId = User.GetUserId();
                if (userId != null)
                    await _campaigns.EnsurePermanentAsync(schoolId.Value, userId.Value, ct);
            }

            var campaigns = await _context.DonationCampaigns
                .Where(c => c.SchoolId == schoolId.Value)
                .OrderByDescending(c => c.IsPermanent)
                .ThenBy(c => c.Status)
                .ThenByDescending(c => c.CreatedAt)
                .ToListAsync(ct);

            var totals = await _campaigns.GetTotalsAsync(campaigns.Select(c => c.Id).ToList(), ct);
            var dtos = campaigns.Select(c => ToDto(c, totals[c.Id])).ToList();
            return Ok(ApiResponse<DonationCampaignsResponse>.Ok(new DonationCampaignsResponse
            {
                Campaigns = dtos,
                CanManage = canManage
            }));
        }

        /// <summary>`GET /api/donation-campaigns/{id}` — une collecte et ses dons.</summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ApiResponse<DonationCampaignDetailDto>>> Get(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Forbid();

            var campaign = await _context.DonationCampaigns
                .FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == schoolId.Value, ct);
            if (campaign == null) return NotFound(ApiResponse<DonationCampaignDetailDto>.Fail("Collecte introuvable."));

            var totals = await _campaigns.GetTotalsAsync(id, ct);

            var donations = await _context.Payments
                .Where(p => p.DonationCampaignId == id
                            && (p.Status == PaymentStatus.Completed || p.Status == PaymentStatus.Pending))
                .OrderByDescending(p => p.InitiatedAt)
                .Take(200)
                .Select(p => new CampaignDonationDto
                {
                    Id = p.Id,
                    DonorName = p.DonorName,
                    DonorPhone = p.DonorPhone,
                    DonorOrganization = p.DonorOrganization,
                    DonorAnonymous = p.DonorAnonymous,
                    AmountFcfa = p.TargetAmountFcfa,
                    Status = p.Status,
                    InitiatedAt = p.InitiatedAt,
                    PaidAt = p.PaidAt,
                    HasReceipt = p.ReceiptPdfPath != null
                })
                .ToListAsync(ct);

            return Ok(ApiResponse<DonationCampaignDetailDto>.Ok(new DonationCampaignDetailDto
            {
                Campaign = ToDto(campaign, totals),
                Donations = donations
            }));
        }

        // ====================================================================
        // ===== Écriture — direction seule =====
        // ====================================================================

        /// <summary>`POST /api/donation-campaigns` — ouvrir une collecte.</summary>
        [HttpPost]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<DonationCampaignDto>>> Create(
            [FromBody] CreateDonationCampaignRequest dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Forbid();

            if (dto.AmountMode == DonationAmountMode.Fixed
                && (dto.FixedAmountFcfa == null || dto.FixedAmountFcfa <= 0))
            {
                return BadRequest(ApiResponse<DonationCampaignDto>.Fail(
                    "Indiquez le montant que vous imposez, ou laissez le montant libre."));
            }

            var platform = await _context.GetPlatformSettingsAsync(ct);
            if (dto.AmountMode == DonationAmountMode.Fixed
                && dto.FixedAmountFcfa < platform.MinDonationFcfa)
            {
                return BadRequest(ApiResponse<DonationCampaignDto>.Fail(
                    $"Le montant d'un don ne peut pas être inférieur à {platform.MinDonationFcfa} FCFA."));
            }

            var feesPayer = dto.FeesPayer ?? await _context.SchoolPaymentSettings
                .Where(s => s.SchoolId == schoolId.Value)
                .Select(s => (FeesPayer?)s.DonationFeesPayer)
                .FirstOrDefaultAsync(ct) ?? FeesPayer.School;

            var campaign = new DonationCampaign
            {
                SchoolId = schoolId.Value,
                Slug = await _campaigns.GenerateSlugAsync(dto.Name, ct),
                Name = dto.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                GoalAmountFcfa = dto.GoalAmountFcfa,
                AmountMode = dto.AmountMode,
                FixedAmountFcfa = dto.AmountMode == DonationAmountMode.Fixed ? dto.FixedAmountFcfa : null,
                SuggestedAmountsCsv = JoinSuggested(dto.SuggestedAmounts),
                FeesPayer = feesPayer,
                ShowDonorWall = dto.ShowDonorWall,
                Status = DonationCampaignStatus.Active,
                ClosesAt = NormalizeClosesAt(dto.ClosesAt),
                IsPermanent = false,
                CreatedById = userId.Value,
                CreatedAt = DateTime.UtcNow
            };

            _context.DonationCampaigns.Add(campaign);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("[donations] Collecte {Id} ({Slug}) ouverte par {UserId} — école {SchoolId}",
                campaign.Id, campaign.Slug, userId, schoolId);

            return Ok(ApiResponse<DonationCampaignDto>.Ok(
                ToDto(campaign, new CampaignTotals(0, 0, 0)), "Collecte ouverte."));
        }

        /// <summary>
        /// `POST /api/donation-campaigns/quick` — le lien libre, sans rien à
        /// remplir : la collecte permanente de l'école, créée si besoin.
        /// </summary>
        [HttpPost("quick")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<DonationCampaignDto>>> Quick(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Forbid();

            var campaign = await _campaigns.EnsurePermanentAsync(schoolId.Value, userId.Value, ct);
            var totals = await _campaigns.GetTotalsAsync(campaign.Id, ct);
            return Ok(ApiResponse<DonationCampaignDto>.Ok(ToDto(campaign, totals)));
        }

        /// <summary>`PUT /api/donation-campaigns/{id}` — modifier. Partiel : un champ null ne touche à rien.</summary>
        [HttpPut("{id:int}")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<DonationCampaignDto>>> Update(
            int id, [FromBody] UpdateDonationCampaignRequest dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Forbid();

            var campaign = await _context.DonationCampaigns
                .FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == schoolId.Value, ct);
            if (campaign == null) return NotFound(ApiResponse<DonationCampaignDto>.Fail("Collecte introuvable."));

            // Le nom de la collecte permanente ne bouge pas : c'est le repère de
            // l'école, et son adresse est déjà partagée.
            if (!campaign.IsPermanent && !string.IsNullOrWhiteSpace(dto.Name))
                campaign.Name = dto.Name.Trim();

            if (dto.Description != null)
                campaign.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();

            if (dto.GoalAmountFcfa != null)
                campaign.GoalAmountFcfa = dto.GoalAmountFcfa == 0 ? null : dto.GoalAmountFcfa;

            if (dto.AmountMode != null)
            {
                campaign.AmountMode = dto.AmountMode.Value;
                if (campaign.AmountMode == DonationAmountMode.Free) campaign.FixedAmountFcfa = null;
            }
            if (dto.FixedAmountFcfa != null && campaign.AmountMode == DonationAmountMode.Fixed)
                campaign.FixedAmountFcfa = dto.FixedAmountFcfa == 0 ? null : dto.FixedAmountFcfa;

            if (dto.SuggestedAmounts != null)
                campaign.SuggestedAmountsCsv = JoinSuggested(dto.SuggestedAmounts);

            if (dto.FeesPayer != null) campaign.FeesPayer = dto.FeesPayer.Value;
            if (dto.ShowDonorWall != null) campaign.ShowDonorWall = dto.ShowDonorWall.Value;

            if (dto.ClearClosesAt) campaign.ClosesAt = null;
            else if (dto.ClosesAt != null) campaign.ClosesAt = NormalizeClosesAt(dto.ClosesAt);

            if (dto.ClearCoverImage && campaign.CoverImagePath != null)
            {
                DeleteUploadedFile(campaign.CoverImagePath);
                campaign.CoverImagePath = null;
            }

            if (campaign.AmountMode == DonationAmountMode.Fixed
                && (campaign.FixedAmountFcfa == null || campaign.FixedAmountFcfa <= 0))
            {
                return BadRequest(ApiResponse<DonationCampaignDto>.Fail(
                    "Une collecte à montant imposé doit porter un montant."));
            }

            await _context.SaveChangesAsync(ct);
            var totals = await _campaigns.GetTotalsAsync(id, ct);
            return Ok(ApiResponse<DonationCampaignDto>.Ok(ToDto(campaign, totals), "Collecte modifiée."));
        }

        /// <summary>`PUT /api/donation-campaigns/{id}/cover` — photo de couverture (base64).</summary>
        [HttpPut("{id:int}/cover")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<DonationCampaignDto>>> SetCover(
            int id, [FromBody] CoverImageRequest dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Forbid();

            var campaign = await _context.DonationCampaigns
                .FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == schoolId.Value, ct);
            if (campaign == null) return NotFound(ApiResponse<DonationCampaignDto>.Fail("Collecte introuvable."));

            var decoded = FileUploadValidator.DecodeAndValidate(
                dto.ImageBase64, _uploads.MaxPhotoSizeMb, _uploads.AllowedPhotoMimeTypes);
            if (decoded == null)
                return BadRequest(ApiResponse<DonationCampaignDto>.Fail("Image invalide ou trop lourde."));

            var folder = Path.Combine(_env.WebRootPath, "uploads", "donations");
            Directory.CreateDirectory(folder);
            var fileName = $"{Guid.NewGuid():N}{decoded.Extension}";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), decoded.Bytes, ct);

            var previous = campaign.CoverImagePath;
            campaign.CoverImagePath = $"/uploads/donations/{fileName}";
            await _context.SaveChangesAsync(ct);
            DeleteUploadedFile(previous);

            var totals = await _campaigns.GetTotalsAsync(id, ct);
            return Ok(ApiResponse<DonationCampaignDto>.Ok(ToDto(campaign, totals), "Photo enregistrée."));
        }

        /// <summary>`POST /api/donation-campaigns/{id}/status` — pause, reprise, fermeture.</summary>
        [HttpPost("{id:int}/status")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<DonationCampaignDto>>> SetStatus(
            int id, [FromBody] CampaignStatusRequest dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Forbid();

            var campaign = await _context.DonationCampaigns
                .FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == schoolId.Value, ct);
            if (campaign == null) return NotFound(ApiResponse<DonationCampaignDto>.Fail("Collecte introuvable."));

            // La collecte permanente peut se mettre en pause (le daara qui ne
            // veut plus rien recevoir pour un temps) mais jamais se fermer : ce
            // lien est censé survivre à tout, il est partout dans les WhatsApp.
            if (campaign.IsPermanent && dto.Status == DonationCampaignStatus.Closed)
            {
                return BadRequest(ApiResponse<DonationCampaignDto>.Fail(
                    "Le lien permanent du daara ne se ferme pas. Mettez-le en pause si vous ne voulez plus recevoir de dons."));
            }

            if (campaign.Status == DonationCampaignStatus.Closed
                && dto.Status != DonationCampaignStatus.Closed)
            {
                return BadRequest(ApiResponse<DonationCampaignDto>.Fail(
                    "Une collecte fermée ne se rouvre pas. Ouvrez-en une nouvelle."));
            }

            campaign.Status = dto.Status;
            if (dto.Status == DonationCampaignStatus.Closed)
            {
                campaign.ClosedAt = DateTime.UtcNow;
                campaign.ClosedById = userId.Value;
            }
            await _context.SaveChangesAsync(ct);

            var totals = await _campaigns.GetTotalsAsync(id, ct);
            var message = dto.Status switch
            {
                DonationCampaignStatus.Paused => "Collecte en pause. Le lien reste valable.",
                DonationCampaignStatus.Closed => "Collecte fermée.",
                _ => "Collecte rouverte."
            };
            return Ok(ApiResponse<DonationCampaignDto>.Ok(ToDto(campaign, totals), message));
        }

        /// <summary>
        /// `DELETE /api/donation-campaigns/{id}` — supprimer, UNIQUEMENT si la
        /// collecte n'a jamais rien reçu.
        /// </summary>
        /// <remarks>
        /// Une collecte qui a encaissé est une origine comptable : ses dons ont
        /// crédité le portefeuille de l'école. La supprimer laisserait de l'argent
        /// sans provenance et la réconciliation ne tomberait plus juste (§55). Le
        /// refus est expliqué à l'école, avec le geste qui la débloque — plutôt
        /// qu'un bouton grisé sans raison.
        /// </remarks>
        [HttpDelete("{id:int}")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Forbid();

            var campaign = await _context.DonationCampaigns
                .FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == schoolId.Value, ct);
            if (campaign == null) return NotFound(ApiResponse<object?>.Fail("Collecte introuvable."));

            if (campaign.IsPermanent)
                return BadRequest(ApiResponse<object?>.Fail("Le lien permanent du daara ne se supprime pas."));

            var hasPayments = await _context.Payments.AnyAsync(p => p.DonationCampaignId == id, ct);
            if (hasPayments)
            {
                return BadRequest(ApiResponse<object?>.Fail(
                    "Cette collecte a reçu des dons : ils ne peuvent pas être effacés. Fermez-la, elle quittera la liste des collectes en cours."));
            }

            DeleteUploadedFile(campaign.CoverImagePath);
            _context.DonationCampaigns.Remove(campaign);
            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<object?>.Ok(null, "Collecte supprimée."));
        }

        // ====================================================================
        // ===== Helpers =====
        // ====================================================================

        private DonationCampaignDto ToDto(DonationCampaign c, CampaignTotals totals) => new()
        {
            Id = c.Id,
            Slug = c.Slug,
            PublicUrl = $"{_senepaySettings.PublicBaseUrl.TrimEnd('/')}/don/{c.Slug}",
            Name = c.Name,
            Description = c.Description,
            CoverImageUrl = c.CoverImagePath,
            GoalAmountFcfa = c.GoalAmountFcfa,
            AmountMode = c.AmountMode,
            FixedAmountFcfa = c.FixedAmountFcfa,
            SuggestedAmounts = c.SuggestedAmounts().ToList(),
            FeesPayer = c.FeesPayer,
            ShowDonorWall = c.ShowDonorWall,
            Status = c.Status,
            ClosesAt = c.ClosesAt,
            IsPermanent = c.IsPermanent,
            IsOpen = c.IsOpen,
            CollectedFcfa = totals.CollectedFcfa,
            DonationCount = totals.DonationCount,
            PendingCount = totals.PendingCount,
            OpenCount = c.OpenCount,
            CanDelete = !c.IsPermanent && totals.DonationCount == 0 && totals.PendingCount == 0,
            CreatedAt = c.CreatedAt
        };

        private static string? JoinSuggested(List<long>? amounts)
        {
            if (amounts == null || amounts.Count == 0) return null;
            var clean = amounts.Where(a => a > 0).Distinct().OrderBy(a => a).Take(3).ToList();
            return clean.Count == 0 ? null : string.Join(',', clean);
        }

        /// <summary>
        /// PostgreSQL refuse un DateTime non-UTC sur une colonne timestamptz
        /// (§47) : une date limite saisie par l'app arrive souvent en Unspecified.
        /// </summary>
        private static DateTime? NormalizeClosesAt(DateTime? value)
        {
            if (value == null) return null;
            return value.Value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.Value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            };
        }

        /// <summary>Supprime un fichier téléversé (best-effort, défense path-traversal).</summary>
        private void DeleteUploadedFile(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                var root = Path.GetFullPath(_env.WebRootPath);
                var full = Path.GetFullPath(Path.Combine(_env.WebRootPath, url.TrimStart('/')));
                if (full.StartsWith(root) && System.IO.File.Exists(full))
                    System.IO.File.Delete(full);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[donations] Échec suppression fichier {Url} (non bloquant)", url);
            }
        }
    }

    public class DonationCampaignDetailDto
    {
        public DonationCampaignDto Campaign { get; set; } = new();
        public List<CampaignDonationDto> Donations { get; set; } = new();
    }

    public class CoverImageRequest
    {
        public string ImageBase64 { get; set; } = string.Empty;
    }

    public class CampaignStatusRequest
    {
        public DonationCampaignStatus Status { get; set; }
    }
}
