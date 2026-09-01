using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Sms;
using Idara.API.Models;
using Idara.API.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Back-office SMS : ce qui est parti, ce que ça a coûté, ce que la facture
    /// Sonatel devrait annoncer, et où en est la dépense face aux plafonds.
    ///
    /// <para><b>Raison d'être.</b> Le SMS était jusqu'ici une dépense aveugle :
    /// on ne savait ni combien il en partait, ni pour quelles écoles, ni pour
    /// quels événements — donc la facture de fin de mois ne pouvait être ni
    /// vérifiée ni contestée. Un développeur sénégalais a découvert de cette
    /// façon une facture d'un million de FCFA due à un tiers qui se servait de
    /// son compte : le manque de traçabilité n'est pas un inconfort, c'est ce qui
    /// laisse une fraude prospérer un mois entier.</para>
    ///
    /// <para>SuperAdmin uniquement, et en français (§ décision : les écrans
    /// SuperAdmin ne sont pas traduits).</para>
    /// </summary>
    [ApiController]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    [Route("api/sms")]
    public class SmsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SmsController> _logger;

        public SmsController(AppDbContext context, ILogger<SmsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ================================================================
        // ===== Vue d'ensemble d'un mois =====
        // ================================================================

        /// <summary>
        /// Tout l'écran en un appel : totaux du mois, état du budget,
        /// répartitions et écoles à surveiller.
        /// </summary>
        [HttpGet("overview")]
        public async Task<ActionResult<ApiResponse<SmsOverviewDto>>> Overview(
            [FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var y = year ?? now.Year;
            var m = month is >= 1 and <= 12 ? month.Value : now.Month;
            var from = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = from.AddMonths(1);

            var p = await _context.GetPlatformSettingsAsync(ct);

            // Le canal « SmsGrouped » est EXCLU partout ici : ce sont les lignes
            // compagnon d'un message groupé (les factures des frères et sœurs),
            // qui servent à la déduplication mais ne correspondent à aucun envoi
            // facturé. Les compter gonflerait le total d'un message parti une
            // seule fois.
            //
            // ⚠️ TOUT est agrégé EN SQL, jamais en mémoire. Ce n'est pas de la
            // coquetterie : cette page est précisément celle qu'on ouvre quand
            // la dépense s'emballe, c'est-à-dire quand la table est la plus
            // grosse. Un `ToListAsync()` sur le mois y ramènerait des dizaines
            // de milliers de lignes au pire moment.
            var monthLogs = _context.NotificationLogs.AsNoTracking()
                .Where(n => n.Channel == "Sms" && n.CreatedAt >= from && n.CreatedAt < to);
            var sent = monthLogs.Where(n => n.BlockedReason == null);

            var totals = new SmsMonthTotalsDto
            {
                Year = y,
                Month = m,
                MessagesSent = await sent.CountAsync(n => n.Success, ct),
                MessagesFailed = await sent.CountAsync(n => !n.Success, ct),
                MessagesBlocked = await monthLogs.CountAsync(n => n.BlockedReason != null, ct),
                Segments = await sent.SumAsync(n => (int?)n.Segments, ct) ?? 0,
                SegmentsFixed160 = await sent.SumAsync(n => (int?)n.SegmentsFixed160, ct) ?? 0,
                ConsumptionHtFcfa = (await sent.SumAsync(n => (long?)n.CostCentimes, ct) ?? 0) / 100.0,
                MonthlyFeeHtFcfa = p.SmsMonthlyFeeHtFcfa,
                VatPercent = p.SmsVatPercent,
            };

            var vat = 1 + p.SmsVatPercent / 100.0;
            totals.ExpectedHtFcfa = totals.ConsumptionHtFcfa + p.SmsMonthlyFeeHtFcfa;
            totals.ExpectedTtcFcfa = totals.ExpectedHtFcfa * vat;
            var invoice = await _context.SmsProviderInvoices.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Year == y && i.Month == m, ct);
            if (invoice != null)
            {
                totals.InvoiceRecorded = true;
                totals.InvoiceHtFcfa = invoice.AmountHtFcfa;
                totals.InvoiceTtcFcfa = invoice.AmountTtcFcfa;
                totals.InvoiceQuantity = invoice.ProviderQuantity;
                totals.InvoiceNote = invoice.Note;
                // Si le TTC n'est pas détaillé sur la facture, on compare le HT
                // au HT : opposer un HT à un TTC ferait apparaître un écart de
                // 18 % qui n'existe pas.
                totals.GapTtcFcfa = invoice.AmountTtcFcfa > 0
                    ? invoice.AmountTtcFcfa - totals.ExpectedTtcFcfa
                    : invoice.AmountHtFcfa - totals.ExpectedHtFcfa;
            }

            // ⚠️ DEUX contraintes de traduction, apprises au premier appel réel
            // et non déduites de la documentation :
            //
            //  ① l'identifiant est projeté BRUT, jamais converti en chaîne dans
            //    la requête — `SchoolId.Value.ToString()` dans un GROUP BY n'est
            //    pas traduisible et faisait échouer TOUT l'écran ;
            //
            //  ② la projection reste ANONYME et le tri se fait APRÈS
            //    matérialisation. Trier sur une propriété d'un `record` construit
            //    par constructeur est intraduisible : EF ne sait pas relier
            //    `r.CostCentimes` à l'agrégat qui l'a produite.
            //
            // L'agrégation lourde reste donc en SQL ; seul le tri, qui porte sur
            // quelques dizaines de groupes, se fait en mémoire — où il est
            // gratuit.
            var bySchool = (await sent
                    .GroupBy(n => new { n.SchoolId, n.SchoolNameSnapshot })
                    .Select(g => new
                    {
                        g.Key.SchoolId,
                        g.Key.SchoolNameSnapshot,
                        Messages = g.Count(),
                        Segments = g.Sum(x => x.Segments),
                        Cost = g.Sum(x => x.CostCentimes),
                        Failed = g.Count(x => !x.Success),
                    })
                    .ToListAsync(ct))
                .OrderByDescending(r => r.Cost)
                .Take(50)
                .ToList();

            var byEvent = (await sent
                    .GroupBy(n => n.TemplateCode)
                    .Select(g => new
                    {
                        Code = g.Key,
                        Messages = g.Count(),
                        Segments = g.Sum(x => x.Segments),
                        Cost = g.Sum(x => x.CostCentimes),
                        Failed = g.Count(x => !x.Success),
                    })
                    .ToListAsync(ct))
                .OrderByDescending(r => r.Cost)
                .ToList();

            var byDay = (await sent
                    .GroupBy(n => n.CreatedAt.Date)
                    .Select(g => new
                    {
                        Day = g.Key,
                        Messages = g.Count(),
                        Segments = g.Sum(x => x.Segments),
                        Cost = g.Sum(x => x.CostCentimes),
                        Failed = g.Count(x => !x.Success),
                    })
                    .ToListAsync(ct))
                .OrderBy(r => r.Day)
                .ToList();

            var byNetwork = (await sent
                    .GroupBy(n => n.Network)
                    .Select(g => new
                    {
                        Network = g.Key,
                        Messages = g.Count(),
                        Segments = g.Sum(x => x.Segments),
                        Cost = g.Sum(x => x.CostCentimes),
                        Failed = g.Count(x => !x.Success),
                    })
                    .ToListAsync(ct))
                .OrderByDescending(r => r.Cost)
                .ToList();

            // La seconde hypothèse de facturation se déduit de byNetwork : même
            // regroupement, autre découpage. Inutile de repasser en base.
            var fixedLots = await sent
                .GroupBy(n => n.Network)
                .Select(g => new { Network = g.Key, Lots = g.Sum(x => x.SegmentsFixed160) })
                .ToListAsync(ct);
            totals.ConsumptionFixed160HtFcfa =
                fixedLots.Sum(x => x.Lots * p.SmsUnitPriceCentimes(x.Network)) / 100.0;
            totals.ExpectedTtcFixed160Fcfa =
                (totals.ConsumptionFixed160HtFcfa + p.SmsMonthlyFeeHtFcfa) * vat;

            // Les blocages sont ventilés par MOTIF, et non fondus dans les
            // répartitions ci-dessus : un envoi bloqué n'a pas de coût, il a une
            // cause. Les mélanger diluerait le seul tableau qui dit qu'une
            // attaque est en cours.
            var blocked = await monthLogs
                .Where(n => n.BlockedReason != null)
                .GroupBy(n => n.BlockedReason!)
                .Select(g => new { Reason = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync(ct);

            var overview = new SmsOverviewDto
            {
                Totals = totals,
                Budget = await BuildBudgetAsync(p, ct),
                BySchool = bySchool.Select(r => Row(
                    r.SchoolId?.ToString() ?? "-",
                    r.SchoolNameSnapshot
                        ?? (r.SchoolId == null ? "Hors ecole" : $"Ecole #{r.SchoolId}"),
                    r.Messages, r.Segments, r.Cost, r.Failed, 0)).ToList(),
                ByEvent = byEvent.Select(r => Row(r.Code, SmsEventLabels.Of(r.Code),
                    r.Messages, r.Segments, r.Cost, r.Failed, 0)).ToList(),
                ByDay = byDay.Select(r => Row(r.Day.ToString("yyyy-MM-dd"),
                    r.Day.ToString("dd/MM"),
                    r.Messages, r.Segments, r.Cost, r.Failed, 0)).ToList(),
                ByNetwork = byNetwork.Select(r => Row(r.Network.ToString(),
                    SmsEventLabels.NetworkLabel(r.Network),
                    r.Messages, r.Segments, r.Cost, r.Failed, 0)).ToList(),
                SchoolsAtRisk = await BuildSchoolsAtRiskAsync(p, from, to, ct),
                MeasuredSince = await _context.NotificationLogs.AsNoTracking()
                    .Where(n => n.Channel == "Sms" && n.Segments > 0)
                    .OrderBy(n => n.CreatedAt)
                    .Select(n => (DateTime?)n.CreatedAt)
                    .FirstOrDefaultAsync(ct),
            };

            overview.Budget.BlockedLast24h = blocked
                .Select(b => Row(b.Reason, SmsBudgetGuard.ReasonLabel(b.Reason),
                    b.Count, 0, 0, 0, b.Count))
                .ToList();

            return Ok(ApiResponse<SmsOverviewDto>.Ok(overview));
        }

        // ================================================================
        // ===== Journal détaillé =====
        // ================================================================

        /// <summary>
        /// Journal paginé des envois. Filtres serveur (§121 : la recherche d'un
        /// historique se fait côté serveur, et l'export suit toujours la vue).
        /// </summary>
        [HttpGet("logs")]
        public async Task<ActionResult<ApiResponse<PaginatedResult<SmsLogDto>>>> Logs(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] int? schoolId = null,
            [FromQuery] string? templateCode = null,
            [FromQuery] string? q = null,
            [FromQuery] bool? onlyFailed = null,
            [FromQuery] bool? onlyBlocked = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = _context.NotificationLogs.AsNoTracking()
                .Where(n => n.Channel == "Sms");

            if (schoolId != null) query = query.Where(n => n.SchoolId == schoolId);
            if (!string.IsNullOrWhiteSpace(templateCode))
                query = query.Where(n => n.TemplateCode == templateCode);
            if (onlyFailed == true) query = query.Where(n => !n.Success && n.BlockedReason == null);
            if (onlyBlocked == true) query = query.Where(n => n.BlockedReason != null);
            if (from != null) query = query.Where(n => n.CreatedAt >= from.Value.ToUtcSafe());
            if (to != null) query = query.Where(n => n.CreatedAt < to.Value.ToUtcSafe().AddDays(1));

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(n =>
                    EF.Functions.ILike(n.Recipient, $"%{term}%")
                    || EF.Functions.ILike(n.SchoolNameSnapshot ?? "", $"%{term}%")
                    || EF.Functions.ILike(n.TriggerSource ?? "", $"%{term}%")
                    || EF.Functions.ILike(n.TemplateCode, $"%{term}%"));
            }

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(n => n.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync(ct);

            return Ok(ApiResponse<PaginatedResult<SmsLogDto>>.Ok(new PaginatedResult<SmsLogDto>
            {
                Data = items.Select(Map).ToList(),
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
            }));
        }

        // ================================================================
        // ===== Réglages =====
        // ================================================================

        /// <summary>
        /// Réglages SMS. Endpoint DÉDIÉ et non fondu dans le PUT des réglages
        /// plateforme : le même motif qu'au § de la mise à jour Android — un
        /// client resté sur une ancienne version renverrait le corps entier et
        /// écraserait, sans le savoir, des plafonds qu'il ne connaît pas.
        /// </summary>
        [HttpGet("settings")]
        public async Task<ActionResult<ApiResponse<SmsSettingsDto>>> GetSettings(CancellationToken ct) =>
            Ok(ApiResponse<SmsSettingsDto>.Ok(MapSettings(await _context.GetPlatformSettingsAsync(ct))));

        [HttpPut("settings")]
        public async Task<ActionResult<ApiResponse<SmsSettingsDto>>> UpdateSettings(
            [FromBody] UpdateSmsSettingsDto dto, CancellationToken ct)
        {
            // Un palier souple au-dessus du palier absolu n'aurait aucun sens :
            // le premier ne se déclencherait jamais, et on croirait avoir deux
            // niveaux de protection alors qu'il n'y en aurait qu'un.
            if (dto.SoftDailyCapFcfa > dto.HardDailyCapFcfa
                || dto.SoftMonthlyCapFcfa > dto.HardMonthlyCapFcfa)
                return BadRequest(ApiResponse<SmsSettingsDto>.Fail(
                    "Le palier d'alerte doit rester inferieur ou egal au palier absolu."));

            var s = await _context.GetPlatformSettingsAsync(ct);

            s.SmsBilingual = dto.Bilingual;
            s.SmsKillSwitch = dto.KillSwitch;
            s.SmsOnNetPriceCentimes = dto.OnNetPriceCentimes;
            s.SmsOffNetPriceCentimes = dto.OffNetPriceCentimes;
            s.SmsInternationalPriceCentimes = dto.InternationalPriceCentimes;
            s.SmsMonthlyFeeHtFcfa = dto.MonthlyFeeHtFcfa;
            s.SmsVatPercent = dto.VatPercent;
            s.SmsSoftDailyCapFcfa = dto.SoftDailyCapFcfa;
            s.SmsSoftMonthlyCapFcfa = dto.SoftMonthlyCapFcfa;
            s.SmsHardDailyCapFcfa = dto.HardDailyCapFcfa;
            s.SmsHardMonthlyCapFcfa = dto.HardMonthlyCapFcfa;
            s.SmsSchoolMonthlySegmentsPerStudent = dto.SchoolMonthlySegmentsPerStudent;
            s.SmsSchoolMonthlyFloorSegments = dto.SchoolMonthlyFloorSegments;
            s.SmsSchoolDailySegmentsPerStudent = dto.SchoolDailySegmentsPerStudent;
            s.SmsSchoolDailyFloorSegments = dto.SchoolDailyFloorSegments;
            s.SmsSchoolHourlySegmentsPerStudent = dto.SchoolHourlySegmentsPerStudent;
            s.SmsSchoolHourlyFloorSegments = dto.SchoolHourlyFloorSegments;
            s.SmsMaxMessagesPerDistinctRecipient = dto.MaxMessagesPerDistinctRecipient;
            s.SmsRatioMinMessages = dto.RatioMinMessages;
            s.SmsMaxPerRecipientPerDay = dto.MaxPerRecipientPerDay;
            s.SmsMaxPerRecipientPerMonth = dto.MaxPerRecipientPerMonth;
            s.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);

            _logger.LogWarning(
                "[sms-settings] MAJ par SuperAdmin {UserId} : coupe-circuit={Kill}, bilingue={Bi}, "
                + "palier souple={Soft} F/j, palier absolu={Hard} F/j",
                User.GetUserId(), s.SmsKillSwitch, s.SmsBilingual,
                s.SmsSoftDailyCapFcfa, s.SmsHardDailyCapFcfa);

            return Ok(ApiResponse<SmsSettingsDto>.Ok(MapSettings(s), "Reglages SMS mis a jour."));
        }

        /// <summary>
        /// Bascule seule du coupe-circuit. Endpoint distinct du PUT complet
        /// EXPRÈS : c'est le geste d'urgence, il doit tenir en un appel et ne
        /// dépendre d'aucun autre champ correctement rempli.
        /// </summary>
        [HttpPost("kill-switch")]
        public async Task<ActionResult<ApiResponse<bool>>> SetKillSwitch(
            [FromQuery] bool on, CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            s.SmsKillSwitch = on;
            s.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            _logger.LogCritical("[sms] Coupe-circuit SMS {State} par SuperAdmin {UserId}",
                on ? "ACTIVE (plus aucun SMS ne part)" : "desactive", User.GetUserId());

            return Ok(ApiResponse<bool>.Ok(on,
                on ? "Envoi de SMS suspendu." : "Envoi de SMS retabli."));
        }

        // ================================================================
        // ===== Facture Orange =====
        // ================================================================

        [HttpGet("invoices")]
        public async Task<ActionResult<ApiResponse<List<SmsProviderInvoice>>>> Invoices(
            CancellationToken ct) =>
            Ok(ApiResponse<List<SmsProviderInvoice>>.Ok(await _context.SmsProviderInvoices
                .AsNoTracking()
                .OrderByDescending(i => i.Year).ThenByDescending(i => i.Month)
                .Take(36)
                .ToListAsync(ct)));

        /// <summary>Saisit (ou corrige) la facture Sonatel d'un mois.</summary>
        [HttpPost("invoices")]
        public async Task<ActionResult<ApiResponse<SmsProviderInvoice>>> RecordInvoice(
            [FromBody] RecordSmsInvoiceDto dto, CancellationToken ct)
        {
            var existing = await _context.SmsProviderInvoices
                .FirstOrDefaultAsync(i => i.Year == dto.Year && i.Month == dto.Month, ct);

            if (existing == null)
            {
                existing = new SmsProviderInvoice
                {
                    Year = dto.Year,
                    Month = dto.Month,
                    RecordedById = User.GetUserId() ?? 0,
                    RecordedAt = DateTime.UtcNow,
                };
                _context.SmsProviderInvoices.Add(existing);
            }
            else
            {
                existing.UpdatedAt = DateTime.UtcNow;
            }

            existing.AmountHtFcfa = dto.AmountHtFcfa;
            existing.AmountTtcFcfa = dto.AmountTtcFcfa;
            existing.ProviderQuantity = dto.ProviderQuantity;
            existing.Note = dto.Note;

            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<SmsProviderInvoice>.Ok(existing, "Facture enregistree."));
        }

        // ================================================================
        // ===== Garde-fou par école =====
        // ================================================================

        /// <summary>
        /// Relève ou remet à zéro le plafond mensuel d'une école. La soupape qui
        /// rend le garde-fou vivable : une rentrée où l'on crée trois cents
        /// comptes est légitime et sortirait du plafond ordinaire. Sans elle, la
        /// seule issue serait de relever le plafond de TOUTES les écoles, donc de
        /// désarmer le dispositif pour tout le monde à cause d'une seule.
        /// </summary>
        [HttpPost("schools/{schoolId:int}/cap")]
        public async Task<ActionResult<ApiResponse<bool>>> SetSchoolCap(
            int schoolId, [FromBody] SetSchoolSmsCapDto dto, CancellationToken ct)
        {
            var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == schoolId, ct);
            if (school == null) return NotFound(ApiResponse<bool>.Fail("Ecole introuvable."));

            school.SmsMonthlyCapOverrideSegments = dto.MonthlyCapSegments;
            await _context.SaveChangesAsync(ct);

            _logger.LogWarning("[sms] Plafond mensuel de l'ecole {SchoolId} = {Cap} par SuperAdmin {UserId}",
                schoolId, dto.MonthlyCapSegments?.ToString() ?? "defaut (effectif)", User.GetUserId());

            return Ok(ApiResponse<bool>.Ok(true, "Plafond mis a jour."));
        }

        /// <summary>Suspend ou rétablit les SMS NON CRITIQUES d'une école. Ses
        /// codes de connexion continuent de partir : on isole une école qui
        /// s'emballe, on ne l'enferme pas dehors.</summary>
        [HttpPost("schools/{schoolId:int}/suspension")]
        public async Task<ActionResult<ApiResponse<bool>>> SetSchoolSuspension(
            int schoolId, [FromBody] SetSchoolSmsSuspensionDto dto, CancellationToken ct)
        {
            var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == schoolId, ct);
            if (school == null) return NotFound(ApiResponse<bool>.Fail("Ecole introuvable."));

            school.SmsSuspended = dto.Suspended;
            school.SmsSuspendedReason = dto.Suspended ? dto.Reason : null;
            school.SmsSuspendedAt = dto.Suspended ? DateTime.UtcNow : null;
            await _context.SaveChangesAsync(ct);

            _logger.LogWarning("[sms] Ecole {SchoolId} {State} par SuperAdmin {UserId} : {Reason}",
                schoolId, dto.Suspended ? "SUSPENDUE (SMS)" : "retablie", User.GetUserId(), dto.Reason);

            return Ok(ApiResponse<bool>.Ok(true,
                dto.Suspended ? "Ecole suspendue." : "Ecole retablie."));
        }

        // ================================================================
        // ===== Helpers =====
        // ================================================================

        private async Task<SmsBudgetStateDto> BuildBudgetAsync(
            Models.PlatformSettings p, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var dayStart = now.Date;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var billed = _context.NotificationLogs
                .Where(n => n.Channel == "Sms" && n.BlockedReason == null);

            var day = await billed.Where(n => n.CreatedAt >= dayStart)
                .SumAsync(n => (long?)n.CostCentimes, ct) ?? 0;
            var month = await billed.Where(n => n.CreatedAt >= monthStart)
                .SumAsync(n => (long?)n.CostCentimes, ct) ?? 0;

            var status =
                p.SmsKillSwitch ? "Coupe-circuit active — plus aucun SMS ne part"
                : day > p.SmsHardDailyCapFcfa * 100 || month > p.SmsHardMonthlyCapFcfa * 100
                    ? "Palier absolu atteint — tout est suspendu"
                : day > p.SmsSoftDailyCapFcfa * 100 || month > p.SmsSoftMonthlyCapFcfa * 100
                    ? "Palier d'alerte atteint — rappels suspendus, codes de connexion maintenus"
                : "Normal";

            return new SmsBudgetStateDto
            {
                KillSwitch = p.SmsKillSwitch,
                SpentTodayFcfa = day / 100.0,
                SpentMonthFcfa = month / 100.0,
                SoftDailyCapFcfa = p.SmsSoftDailyCapFcfa,
                SoftMonthlyCapFcfa = p.SmsSoftMonthlyCapFcfa,
                HardDailyCapFcfa = p.SmsHardDailyCapFcfa,
                HardMonthlyCapFcfa = p.SmsHardMonthlyCapFcfa,
                Status = status,
            };
        }

        /// <summary>
        /// Les écoles qui approchent ou dépassent leur plafond. On ne renvoie que
        /// celles au-delà de 50 % : une liste de cent écoles à 2 % ne se lit pas,
        /// et noierait justement celle qu'il fallait voir.
        /// </summary>
        private async Task<List<SmsSchoolUsageDto>> BuildSchoolsAtRiskAsync(
            Models.PlatformSettings p, DateTime from, DateTime to, CancellationToken ct)
        {
            var usage = await _context.NotificationLogs
                .Where(n => n.Channel == "Sms" && n.BlockedReason == null
                            && n.CreatedAt >= from && n.CreatedAt < to && n.SchoolId != null)
                .GroupBy(n => n.SchoolId!.Value)
                .Select(g => new
                {
                    SchoolId = g.Key,
                    Segments = g.Sum(x => x.Segments),
                    Cost = g.Sum(x => x.CostCentimes),
                })
                .ToListAsync(ct);
            if (usage.Count == 0) return new List<SmsSchoolUsageDto>();

            var ids = usage.Select(u => u.SchoolId).ToList();
            var schools = await _context.Schools.AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .Select(s => new
                {
                    s.Id, s.Name, s.SmsSuspended, s.SmsSuspendedReason,
                    s.SmsMonthlyCapOverrideSegments,
                })
                .ToListAsync(ct);

            // Effectif réel, source unique Enrolled() (§159) : un élève sorti ne
            // doit pas continuer à donner du plafond à son ancienne école.
            var counts = await _context.Students
                .Where(s => ids.Contains(s.SchoolId)).Enrolled()
                .GroupBy(s => s.SchoolId)
                .Select(g => new { SchoolId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SchoolId, x => x.Count, ct);

            var result = new List<SmsSchoolUsageDto>();
            foreach (var u in usage)
            {
                var s = schools.FirstOrDefault(x => x.Id == u.SchoolId);
                var students = counts.TryGetValue(u.SchoolId, out var c) ? c : 0;
                var cap = s?.SmsMonthlyCapOverrideSegments ?? p.SmsSchoolMonthlyCap(students);
                var percent = cap <= 0 ? 0 : u.Segments * 100.0 / cap;
                if (percent < 50 && s?.SmsSuspended != true) continue;

                result.Add(new SmsSchoolUsageDto
                {
                    SchoolId = u.SchoolId,
                    SchoolName = s?.Name ?? $"Ecole #{u.SchoolId}",
                    Students = students,
                    SegmentsMonth = u.Segments,
                    MonthlyCapSegments = cap,
                    CapOverridden = s?.SmsMonthlyCapOverrideSegments != null,
                    CostMonthFcfa = u.Cost / 100.0,
                    Suspended = s?.SmsSuspended ?? false,
                    SuspendedReason = s?.SmsSuspendedReason,
                    UsagePercent = Math.Round(percent, 1),
                });
            }

            return result.OrderByDescending(r => r.UsagePercent).ToList();
        }

        private static SmsBreakdownRowDto Row(
            string key, string label, int messages, int segments, long costCentimes,
            int failed, int blocked) => new()
            {
                Key = key,
                Label = label,
                Messages = messages,
                Segments = segments,
                CostFcfa = costCentimes / 100.0,
                Failed = failed,
                Blocked = blocked,
            };

        private static SmsLogDto Map(NotificationLog n) => new()
        {
            Id = n.Id,
            CreatedAt = n.CreatedAt,
            // Masqué : ce back-office sert à comprendre une dépense, pas à
            // parcourir les carnets d'adresses des daara.
            Recipient = MaskPhone(n.Recipient),
            SchoolId = n.SchoolId,
            SchoolName = n.SchoolNameSnapshot,
            Event = SmsEventLabels.Of(n.TemplateCode),
            TemplateCode = n.TemplateCode,
            TriggerSource = n.TriggerSource,
            TriggerUserId = n.TriggerUserId,
            Channel = n.Channel,
            Encoding = SmsEventLabels.EncodingLabel(n.Encoding),
            Network = SmsEventLabels.NetworkLabel(n.Network),
            CharCount = n.CharCount,
            Segments = n.Segments,
            SegmentsFixed160 = n.SegmentsFixed160,
            CostFcfa = n.CostCentimes / 100.0,
            Success = n.Success,
            Error = n.Error,
            Blocked = n.BlockedReason == null ? null : SmsBudgetGuard.ReasonLabel(n.BlockedReason),
            Priority = n.Priority.ToString(),
        };

        private static string MaskPhone(string phone) =>
            string.IsNullOrEmpty(phone) || phone.Length < 4 ? "***" : phone[..^4] + "****";

        private static SmsSettingsDto MapSettings(Models.PlatformSettings s) => new()
        {
            Bilingual = s.SmsBilingual,
            KillSwitch = s.SmsKillSwitch,
            OnNetPriceCentimes = s.SmsOnNetPriceCentimes,
            OffNetPriceCentimes = s.SmsOffNetPriceCentimes,
            InternationalPriceCentimes = s.SmsInternationalPriceCentimes,
            MonthlyFeeHtFcfa = s.SmsMonthlyFeeHtFcfa,
            VatPercent = s.SmsVatPercent,
            SoftDailyCapFcfa = s.SmsSoftDailyCapFcfa,
            SoftMonthlyCapFcfa = s.SmsSoftMonthlyCapFcfa,
            HardDailyCapFcfa = s.SmsHardDailyCapFcfa,
            HardMonthlyCapFcfa = s.SmsHardMonthlyCapFcfa,
            SchoolMonthlySegmentsPerStudent = s.SmsSchoolMonthlySegmentsPerStudent,
            SchoolMonthlyFloorSegments = s.SmsSchoolMonthlyFloorSegments,
            SchoolDailySegmentsPerStudent = s.SmsSchoolDailySegmentsPerStudent,
            SchoolDailyFloorSegments = s.SmsSchoolDailyFloorSegments,
            SchoolHourlySegmentsPerStudent = s.SmsSchoolHourlySegmentsPerStudent,
            SchoolHourlyFloorSegments = s.SmsSchoolHourlyFloorSegments,
            MaxMessagesPerDistinctRecipient = s.SmsMaxMessagesPerDistinctRecipient,
            RatioMinMessages = s.SmsRatioMinMessages,
            MaxPerRecipientPerDay = s.SmsMaxPerRecipientPerDay,
            MaxPerRecipientPerMonth = s.SmsMaxPerRecipientPerMonth,
        };
    }
}
