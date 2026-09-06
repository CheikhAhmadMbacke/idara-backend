using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Payment;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Idara.API.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Diffusion du lien de paiement permanent à tous les responsables de la
    /// plateforme (campagne SuperAdmin, 2026-09-06).
    ///
    /// <para><b>Pourquoi la plateforme le fait à la place des écoles.</b> Le lien
    /// de paiement existe depuis le 2026-08-20 (§161) : permanent, par
    /// responsable, il recalcule à chaque ouverture la dette de toute la fratrie.
    /// Mais il faut aller le chercher élève par élève dans l'application, et la
    /// plupart des écoles ignorent qu'il existe. Un outil que personne n'utilise
    /// ne vaut pas mieux qu'un outil qui n'existe pas.</para>
    ///
    /// <para><b>Deux temps, jamais un seul.</b> <c>preview</c> recense et CHIFFRE
    /// sans rien envoyer ; <c>run</c> envoie, par tranches, sous un plafond de
    /// dépense que l'appelant a dû écrire lui-même. On ne déclenche pas un envoi
    /// de masse dont on découvrirait le coût sur la facture.</para>
    ///
    /// <para><b>Idempotent.</b> Un responsable déjà servi est sauté (le registre
    /// des envois fait foi, §191) : relancer après une coupure ne double ni les
    /// SMS ni la note.</para>
    /// </summary>
    [ApiController]
    [Route("api/superadmin/payment-link-campaign")]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    public class PaymentLinkCampaignController : ControllerBase
    {
        /// <summary>Jeton factice de la LONGUEUR EXACTE d'un vrai (32 hexa) : le
        /// chiffrage d'un lien pas encore créé doit coûter ce que coûtera le vrai
        /// message, au caractère près.</summary>
        private const string TokenGabarit = "00000000000000000000000000000000";

        /// <summary>Tranche maximale d'un appel : au-delà, la requête HTTP
        /// expirerait avant la fin des envois, et on ne saurait plus où on en est.</summary>
        private const int MaxRecipientsPerCall = 100;

        /// <summary>Échecs d'affilée au-delà desquels on arrête tout : c'est la
        /// signature d'un plafond atteint ou d'un fournisseur en panne, et
        /// continuer ne ferait qu'allonger la liste des perdus.</summary>
        private const int MaxConsecutiveFailures = 10;

        private readonly AppDbContext _context;
        private readonly INotificationService _notifications;
        private readonly SenePaySettings _senepay;
        private readonly ILogger<PaymentLinkCampaignController> _logger;

        public PaymentLinkCampaignController(
            AppDbContext context,
            INotificationService notifications,
            IOptions<SenePaySettings> senepay,
            ILogger<PaymentLinkCampaignController> logger)
        {
            _context = context;
            _notifications = notifications;
            _senepay = senepay.Value;
            _logger = logger;
        }

        /// <summary>
        /// `GET /api/superadmin/payment-link-campaign/preview` — recensement
        /// chiffré. N'écrit rien, n'envoie rien.
        /// </summary>
        [HttpGet("preview")]
        public async Task<ActionResult<ApiResponse<PaymentLinkCampaignPreviewDto>>> Preview(
            [FromQuery] string? schoolIds, CancellationToken ct)
        {
            var filter = ParseIds(schoolIds);
            var settings = await _context.GetPlatformSettingsAsync(ct);
            var cibles = await CollectAsync(filter, ct);

            var dto = new PaymentLinkCampaignPreviewDto
            {
                OnNetPriceCentimes = settings.SmsOnNetPriceCentimes,
                OffNetPriceCentimes = settings.SmsOffNetPriceCentimes,
            };

            foreach (var ecole in cibles)
            {
                var ligne = new PaymentLinkCampaignSchoolDto
                {
                    SchoolId = ecole.SchoolId,
                    SchoolName = ecole.SchoolName,
                    Recipients = ecole.Recipients.Count,
                    SkippedNoPhone = ecole.SkippedNoPhone,
                };

                foreach (var r in ecole.Recipients)
                {
                    if (r.AlreadySent) { ligne.AlreadySent++; continue; }
                    ligne.Pending++;

                    var (segments, cost, network) = Chiffrer(ecole, r.PhoneE164, settings);
                    ligne.PendingSegments += segments;
                    ligne.PendingCostFcfa += cost;
                    if (network == SmsNetwork.OnNet) ligne.OnNetRecipients++;
                    else ligne.OffNetRecipients++;
                }

                // Les centimes ne s'arrondissent qu'une fois, sur le total (§208).
                ligne.PendingCostFcfa = (ligne.PendingCostFcfa + 99) / 100;
                dto.BySchool.Add(ligne);

                dto.Recipients += ligne.Recipients;
                dto.AlreadySent += ligne.AlreadySent;
                dto.Pending += ligne.Pending;
                dto.SkippedNoPhone += ligne.SkippedNoPhone;
                dto.PendingSegments += ligne.PendingSegments;
                dto.PendingCostFcfa += ligne.PendingCostFcfa;
                dto.OnNetRecipients += ligne.OnNetRecipients;
                dto.OffNetRecipients += ligne.OffNetRecipients;
            }

            dto.Schools = dto.BySchool.Count;
            dto.BySchool = dto.BySchool.OrderByDescending(s => s.Pending).ToList();

            // Un exemple RÉEL, pris sur la première école qui a du monde : le
            // message affiché à l'écran doit être celui qui partira, pas une
            // maquette qui pourrait diverger.
            var modele = cibles.FirstOrDefault(e => e.Recipients.Any(r => !r.AlreadySent)) ?? cibles.FirstOrDefault();
            var exemple = NotificationTemplates
                .PaymentLinkShare(modele?.SchoolName, modele?.SchoolNameAr, BuildUrl(TokenGabarit))
                .Compose(bilingual: true);
            var mesure = SmsSegmentCalculator.Measure(exemple);
            dto.SampleMessage = exemple;
            dto.SampleSegments = mesure.Segments;
            dto.SampleCharCount = mesure.CharCount;

            return Ok(ApiResponse<PaymentLinkCampaignPreviewDto>.Ok(dto));
        }

        /// <summary>
        /// `POST /api/superadmin/payment-link-campaign/run` — envoie une TRANCHE,
        /// sous plafond de dépense. À rappeler tant que `remainingPending > 0`.
        /// </summary>
        [HttpPost("run")]
        public async Task<ActionResult<ApiResponse<PaymentLinkCampaignRunResultDto>>> Run(
            [FromBody] PaymentLinkCampaignRunRequestDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();
            if (dto.MaxCostFcfa <= 0)
            {
                return BadRequest(ApiResponse<PaymentLinkCampaignRunResultDto>.Fail(
                    "Indiquez le budget maximal de cet envoi, en FCFA."));
            }

            var settings = await _context.GetPlatformSettingsAsync(ct);
            var cibles = await CollectAsync(dto.SchoolIds, ct);
            var quota = Math.Clamp(dto.MaxRecipients, 1, MaxRecipientsPerCall);

            var result = new PaymentLinkCampaignRunResultDto { StoppedReason = "done" };
            var depensePrevue = 0L;     // en centimes, pour le frein budgétaire
            var echecsDaffilee = 0;
            var debut = DateTime.UtcNow;
            var envoyesIds = new List<int>();

            foreach (var ecole in cibles)
            {
                foreach (var r in ecole.Recipients.Where(x => !x.AlreadySent))
                {
                    if (result.Sent + result.Failed >= quota)
                    {
                        result.StoppedReason = "max_recipients";
                        goto fin;
                    }

                    var (_, cout, _) = Chiffrer(ecole, r.PhoneE164, settings);
                    if ((depensePrevue + cout + 99) / 100 > dto.MaxCostFcfa)
                    {
                        result.StoppedReason = "budget";
                        goto fin;
                    }

                    var lien = await GetOrCreateLinkAsync(ecole.SchoolId, r.GuardianId, userId.Value, ct);
                    if (lien.venaitDetreCree) result.LinksCreated++;

                    var message = NotificationTemplates.PaymentLinkShare(
                        ecole.SchoolName, ecole.SchoolNameAr, BuildUrl(lien.link.Token));

                    var parti = await _notifications.SendSmsAsync(new NotificationSmsRequest(
                        UserId: r.GuardianId,
                        RawPhone: r.PhoneE164,
                        PreferredLanguage: "fr",
                        Message: message,
                        Bilingual: true,
                        TemplateCode: NotificationTemplates.PaymentLinkShareCode,
                        RelatedEntityId: lien.link.Id,
                        PushRoute: null,
                        SchoolId: ecole.SchoolId,
                        Priority: SmsPriority.Bulk,
                        TriggerSource: "api:superadmin/payment-link-campaign",
                        TriggerUserId: userId.Value,
                        GroupedEntityIds: null,
                        AuthorizedCampaign: true), ct);

                    depensePrevue += cout;
                    envoyesIds.Add(r.GuardianId);

                    if (parti)
                    {
                        result.Sent++;
                        echecsDaffilee = 0;
                    }
                    else
                    {
                        result.Failed++;
                        if (++echecsDaffilee >= MaxConsecutiveFailures)
                        {
                            result.StoppedReason = "too_many_failures";
                            goto fin;
                        }
                    }
                }
            }

        fin:
            // Dépense RÉELLE, relue du registre : le montant qui compte est celui
            // que le garde-fou a figé à l'envoi, jamais notre estimation.
            var centimes = await _context.NotificationLogs
                .Where(n => n.Channel == "Sms"
                            && n.TemplateCode == NotificationTemplates.PaymentLinkShareCode
                            && n.CreatedAt >= debut
                            && envoyesIds.Contains(n.UserId!.Value))
                .SumAsync(n => (long?)n.CostCentimes, ct) ?? 0;
            result.SpentFcfa = (centimes + 99) / 100;

            var restants = cibles.Sum(e => e.Recipients.Count(x => !x.AlreadySent))
                           - (result.Sent + result.Failed);
            result.RemainingPending = Math.Max(0, restants);

            _logger.LogInformation(
                "[campagne-lien] {Sent} envoyés, {Failed} échoués, {Links} liens créés, "
                + "{Spent} FCFA dépensés, {Rest} restants — arrêt : {Reason}",
                result.Sent, result.Failed, result.LinksCreated, result.SpentFcfa,
                result.RemainingPending, result.StoppedReason);

            return Ok(ApiResponse<PaymentLinkCampaignRunResultDto>.Ok(result));
        }

        // ===================== Interne =====================

        private sealed class Cible
        {
            public int SchoolId { get; init; }
            public string SchoolName { get; init; } = string.Empty;
            public string? SchoolNameAr { get; init; }
            public List<Destinataire> Recipients { get; } = new();
            public int SkippedNoPhone { get; set; }
        }

        private sealed class Destinataire
        {
            public int GuardianId { get; init; }
            public string PhoneE164 { get; init; } = string.Empty;
            public bool AlreadySent { get; set; }
        }

        /// <summary>
        /// Le recensement. Un responsable est retenu s'il a au moins un élève
        /// INSCRIT (§159) dans une école validée, et un numéro sénégalais
        /// exploitable.
        /// </summary>
        private async Task<List<Cible>> CollectAsync(List<int>? schoolIds, CancellationToken ct)
        {
            // L'école de démonstration est exclue sans discussion : ses
            // « responsables » sont des comptes de test (§107).
            var demoSchoolId = await _context.Users
                .Where(u => u.Email == DemoAccounts.SchoolAdminEmail)
                .Select(u => u.SchoolId)
                .FirstOrDefaultAsync(ct);

            var ecolesQuery = _context.Schools
                .Where(s => s.KycStatus == KycStatus.Validated && !s.SmsSuspended);
            if (demoSchoolId != null)
                ecolesQuery = ecolesQuery.Where(s => s.Id != demoSchoolId.Value);
            if (schoolIds is { Count: > 0 })
                ecolesQuery = ecolesQuery.Where(s => schoolIds.Contains(s.Id));

            var ecoles = await ecolesQuery
                .OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.Name, s.NameAr })
                .ToListAsync(ct);
            if (ecoles.Count == 0) return new List<Cible>();

            var ecoleIds = ecoles.Select(e => e.Id).ToList();

            // Responsables joignables : le lien attribue les paiements au compte
            // du responsable, donc un responsable supprimé n'a rien à recevoir.
            var couples = await _context.StudentGuardians
                .Where(sg => ecoleIds.Contains(sg.Student.SchoolId)
                             && !sg.Guardian.IsDeleted
                             && sg.Guardian.Role == UserRoles.Guardian)
                .Where(sg => !sg.Student.IsDeleted
                             && (sg.Student.ExitDate == null || sg.Student.ExitDate > DateTime.UtcNow.Date))
                .Select(sg => new { sg.Student.SchoolId, sg.GuardianId, sg.Guardian.PhoneNumber })
                .Distinct()
                .ToListAsync(ct);

            // Déjà servis : le registre des envois fait foi. La clé est le couple
            // (responsable, école) — un parent qui a des enfants dans deux daara
            // reçoit bien deux liens, un par école.
            var deja = await _context.NotificationLogs
                .Where(n => n.TemplateCode == NotificationTemplates.PaymentLinkShareCode
                            && n.Success
                            && n.UserId != null
                            && n.SchoolId != null
                            && ecoleIds.Contains(n.SchoolId!.Value))
                .Select(n => new { UserId = n.UserId!.Value, SchoolId = n.SchoolId!.Value })
                .Distinct()
                .ToListAsync(ct);
            var dejaSet = deja.Select(d => (d.SchoolId, d.UserId)).ToHashSet();

            var result = new List<Cible>();
            foreach (var e in ecoles)
            {
                var cible = new Cible
                {
                    SchoolId = e.Id,
                    SchoolName = e.Name ?? e.NameAr ?? string.Empty,
                    SchoolNameAr = e.NameAr,
                };

                foreach (var c in couples.Where(c => c.SchoolId == e.Id)
                                         .GroupBy(c => c.GuardianId)
                                         .Select(g => g.First()))
                {
                    var phone = SenegalPhone.Normalize(c.PhoneNumber);
                    if (phone == null || !SmsSegmentCalculator.IsSenegalMobileE164(phone))
                    {
                        cible.SkippedNoPhone++;
                        continue;
                    }
                    cible.Recipients.Add(new Destinataire
                    {
                        GuardianId = c.GuardianId,
                        PhoneE164 = phone,
                        AlreadySent = dejaSet.Contains((e.Id, c.GuardianId)),
                    });
                }

                if (cible.Recipients.Count > 0 || cible.SkippedNoPhone > 0) result.Add(cible);
            }

            return result;
        }

        /// <summary>Segments, coût en centimes et réseau du message qui partirait.</summary>
        private (int segments, long costCentimes, SmsNetwork network) Chiffrer(
            Cible ecole, string phoneE164, PlatformSettings settings)
        {
            var texte = NotificationTemplates
                .PaymentLinkShare(ecole.SchoolName, ecole.SchoolNameAr, BuildUrl(TokenGabarit))
                .Compose(bilingual: true);
            var mesure = SmsSegmentCalculator.Measure(texte);
            var network = SmsSegmentCalculator.NetworkOf(phoneE164);
            var cout = SmsSegmentCalculator.CostCentimes(
                mesure.Segments, network,
                settings.SmsOnNetPriceCentimes, settings.SmsOffNetPriceCentimes,
                settings.SmsInternationalPriceCentimes);
            return (mesure.Segments, cout, network);
        }

        private async Task<(PaymentLink link, bool venaitDetreCree)> GetOrCreateLinkAsync(
            int schoolId, int guardianId, int createdById, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var lien = await _context.PaymentLinks.FirstOrDefaultAsync(
                l => l.SchoolId == schoolId && l.GuardianId == guardianId && l.RevokedAt == null, ct);
            if (lien != null)
            {
                lien.LastSharedAt = now;
                await _context.SaveChangesAsync(ct);
                return (lien, false);
            }

            lien = new PaymentLink
            {
                Token = Guid.NewGuid().ToString("N"),
                SchoolId = schoolId,
                GuardianId = guardianId,
                CreatedById = createdById,
                CreatedAt = now,
                LastSharedAt = now,
            };
            _context.PaymentLinks.Add(lien);
            try
            {
                await _context.SaveChangesAsync(ct);
                return (lien, true);
            }
            catch (DbUpdateException)
            {
                // Course avec l'école qui générerait le même lien au même moment :
                // l'index unique tranche, on relit le gagnant.
                _context.Entry(lien).State = EntityState.Detached;
                var gagnant = await _context.PaymentLinks.FirstAsync(
                    l => l.SchoolId == schoolId && l.GuardianId == guardianId && l.RevokedAt == null, ct);
                return (gagnant, false);
            }
        }

        private string BuildUrl(string token) =>
            $"{_senepay.PublicBaseUrl.TrimEnd('/')}/pay/link/{token}";

        private static List<int>? ParseIds(string? csv) =>
            string.IsNullOrWhiteSpace(csv)
                ? null
                : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(s => int.TryParse(s, out var v) ? v : 0)
                     .Where(v => v > 0)
                     .ToList();
    }
}
