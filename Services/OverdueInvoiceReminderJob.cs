using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>
    /// Cron quotidien (09:00 UTC) qui envoie UN rappel SMS aux responsables pour
    /// les factures en retard (échéance dépassée, non réglées). Dédup stricte via
    /// <see cref="INotificationService.HasSentSuccessfullyAsync"/> sur
    /// (INVOICE_OVERDUE, InvoiceId) : un seul rappel par facture, jamais de spam
    /// quotidien. Marque aussi la facture <see cref="InvoiceStatus.Overdue"/>.
    ///
    /// 09:00 UTC = 09:00 Dakar (pas de DST) : heure du matin raisonnable pour un
    /// rappel, et loin des crons 02:00 (factures) / 02:30 (réconciliation) / 03:00 (backup).
    /// </summary>
    public class OverdueInvoiceReminderJob : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OverdueInvoiceReminderJob> _logger;

        private static readonly TimeSpan RunAtUtc = new(hours: 9, minutes: 0, seconds: 0);

        public OverdueInvoiceReminderJob(
            IServiceScopeFactory scopeFactory,
            ILogger<OverdueInvoiceReminderJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "[overdue-cron] Démarré. Prochain tick à {Next:yyyy-MM-dd HH:mm} UTC",
                NextFireUtc(DateTime.UtcNow));

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = NextFireUtc(DateTime.UtcNow) - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    try { await Task.Delay(delay, stoppingToken); }
                    catch (OperationCanceledException) { return; }
                }

                try
                {
                    await RunOnceAsync(DateTime.UtcNow, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[overdue-cron] Échec exécution du tick");
                }
            }
        }

        /// <summary>Public pour rejeu manuel (endpoint admin éventuel).</summary>
        public async Task<int> RunOnceAsync(DateTime nowUtc, CancellationToken ct)
        {
            var today = nowUtc.Date;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notif = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var platform = await db.GetPlatformSettingsAsync(ct);
            var bilingual = platform.SmsBilingual;
            var minPayin = platform.MinPayinFcfa;

            // Factures échues, non réglées, reste >= min payin (sinon non payable).
            var overdue = await db.Invoices
                .Where(i => (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Overdue)
                            && i.DueDate < today
                            && (i.AmountDueFcfa - i.AmountPaidFcfa) >= minPayin)
                .Select(i => new
                {
                    i.Id, i.StudentId, i.Type,
                    Remaining = i.AmountDueFcfa - i.AmountPaidFcfa,
                    i.Status
                })
                .ToListAsync(ct);

            // ===== Rappel AVANT la limite (2026-08-27, décision utilisateur) =====
            // La famille était prévenue à l'émission puis relancée APRÈS la
            // limite — rien entre les deux. Ce rappel part entre J-2 et le jour J
            // (fenêtre de 3 jours : un tick raté ne fait pas perdre le rappel),
            // une seule fois par facture (dédup INVOICE_DUE_SOON).
            // ⚠️ UNIQUEMENT si la fenêtre émission → limite fait au moins 5
            // jours : sur une fenêtre courte, le SMS d'émission vient de partir,
            // un deuxième serait du harcèlement facturé.
            var dueSoonMax = today.AddDays(2);
            var dueSoon = await db.Invoices
                .Where(i => i.Status == InvoiceStatus.Pending
                            && i.DueDate >= today && i.DueDate <= dueSoonMax
                            && (i.AmountDueFcfa - i.AmountPaidFcfa) >= minPayin
                            && i.CreatedAt <= i.DueDate.AddDays(-5))
                .Select(i => new
                {
                    i.Id, i.StudentId, i.Type, i.DueDate,
                    Remaining = i.AmountDueFcfa - i.AmountPaidFcfa
                })
                .ToListAsync(ct);

            if (overdue.Count == 0 && dueSoon.Count == 0) return 0;

            // Responsables joignables, groupés par élève. Enrolled() (2026-08-17) :
            // le recouvrement d'une famille partie se fait par téléphone, pas par
            // un SMS automatique répété — un élève SORTI (ou supprimé : oubli
            // préexistant, un rappel pouvait partir pour lui) sort du dictionnaire,
            // et la boucle plus bas saute sa facture. Sa dette reste visible côté
            // école et payable côté parent.
            var studentIds = overdue.Select(o => o.StudentId)
                .Concat(dueSoon.Select(d => d.StudentId))
                .Distinct().ToList();
            var students = await db.Students
                .Where(s => studentIds.Contains(s.Id))
                .Enrolled()
                .Select(s => new { s.Id, s.FirstName, s.LastName })
                .ToDictionaryAsync(s => s.Id, s => $"{s.FirstName} {s.LastName}".Trim(), ct);

            var guardiansByStudent = (await db.StudentGuardians
                    .Where(sg => studentIds.Contains(sg.StudentId)
                                 && !sg.Guardian.IsDeleted
                                 && sg.Guardian.PhoneNumber != null)
                    .Select(sg => new GuardianRef(
                        sg.StudentId, sg.GuardianId, sg.Guardian.PhoneNumber!, sg.Guardian.PreferredLanguage))
                    .ToListAsync(ct))
                .GroupBy(g => g.StudentId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Le passage à Overdue est cosmétique et indépendant du rappel : il
            // doit avoir lieu même pour une facture déjà rappelée ou dont l'élève
            // est sorti, sinon l'écran de l'école mentirait sur son état.
            foreach (var inv in overdue.Where(o => o.Status == InvoiceStatus.Pending))
            {
                await db.Invoices.Where(i => i.Id == inv.Id && i.Status == InvoiceStatus.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvoiceStatus.Overdue), ct);
            }

            // ================================================================
            // Rappels GROUPÉS PAR RESPONSABLE (2026-09-01), et non plus par
            // facture. Une famille de trois enfants recevait trois SMS
            // identiques à trois minutes d'intervalle, facturés trois fois ;
            // dans un daara la fratrie est la norme, donc c'était le premier
            // poste de dépense évitable. Le groupage se fait par (responsable,
            // TYPE de facture) : mélanger mensualité et frais d'inscription
            // obligerait à un mot générique, et un mot faux sur un SMS d'argent
            // se paie en appels à l'école (§158).
            //
            // La déduplication reste EXACTE grâce à `GroupedEntityIds` : chaque
            // facture couverte reçoit sa ligne de registre, à coût nul.
            // ================================================================
            var sent = await SendGroupedAsync(
                notif, guardiansByStudent, students, bilingual, "INVOICE_OVERDUE",
                overdue.Select(o => (o.Id, o.StudentId, o.Type, o.Remaining, DueDate: (DateTime?)null)),
                (type, count, total, name, _) => count == 1
                    ? (type == InvoiceType.Registration
                        ? NotificationTemplates.RegistrationOverdue(name!, total)
                        : NotificationTemplates.InvoiceOverdue(name!, total))
                    : (type == InvoiceType.Registration
                        ? NotificationTemplates.RegistrationOverdueFamily(count, total)
                        : NotificationTemplates.InvoiceOverdueFamily(count, total)),
                ct);

            var dueSoonSent = await SendGroupedAsync(
                notif, guardiansByStudent, students, bilingual, "INVOICE_DUE_SOON",
                dueSoon.Select(d => (d.Id, d.StudentId, d.Type, d.Remaining, DueDate: (DateTime?)d.DueDate)),
                (type, count, total, name, due) => count == 1
                    ? NotificationTemplates.PaymentDueSoon(name!, total, due!.Value, type)
                    : NotificationTemplates.PaymentDueSoonFamily(count, total, due!.Value, type),
                ct);

            _logger.LogInformation(
                "[overdue-cron] Tick {Date:yyyy-MM-dd} : {Overdue} facture(s) en retard ({Sent} rappel(s)), {DueSoon} limite(s) proche(s) ({DueSoonSent} rappel(s))",
                today, overdue.Count, sent, dueSoon.Count, dueSoonSent);
            return sent + dueSoonSent;
        }

        /// <summary>Responsable joignable d'un élève (type nommé et non anonyme :
        /// il traverse une frontière de méthode).</summary>
        private sealed record GuardianRef(
            int StudentId, int GuardianId, string PhoneNumber, string? PreferredLanguage);

        /// <summary>Une facture à rappeler, réduite à ce dont le rappel a besoin.</summary>
        private sealed record InvoiceRef(
            int Id, int StudentId, InvoiceType Type, long Remaining, DateTime? DueDate);

        /// <summary>
        /// Envoie UN rappel par responsable, couvrant toutes les factures de même
        /// type qu'il doit encore régler.
        ///
        /// <para><b>Trois garanties, et chacune répare un défaut réel.</b>
        /// ① La déduplication se fait facture par facture AVANT le groupage :
        /// une facture déjà rappelée ne relance rien, et n'entraîne pas non plus
        /// les factures de ses frères et sœurs. ② Un élève hors effectif (sorti
        /// ou supprimé) est écarté ici, pas plus bas : le recouvrement d'une
        /// famille partie se fait par téléphone (§159). ③ Le total annoncé est
        /// la somme des factures RÉELLEMENT couvertes — jamais celle de toutes
        /// les factures de la famille, ce qui ferait payer deux fois ce qui a
        /// déjà été rappelé.</para>
        /// </summary>
        private static async Task<int> SendGroupedAsync(
            INotificationService notif,
            IReadOnlyDictionary<int, List<GuardianRef>> guardiansByStudent,
            IReadOnlyDictionary<int, string> students,
            bool bilingual,
            string templateCode,
            IEnumerable<(int Id, int StudentId, InvoiceType Type, long Remaining, DateTime? DueDate)> invoices,
            Func<InvoiceType, int, long, string?, DateTime?, BilingualMessage> compose,
            CancellationToken ct)
        {
            // (responsable, type) → factures à couvrir par un seul message.
            var buckets = new Dictionary<(int GuardianId, InvoiceType Type), List<InvoiceRef>>();
            var guardianById = new Dictionary<int, GuardianRef>();

            foreach (var inv in invoices)
            {
                // Dédup PAR FACTURE, avant tout groupage.
                if (await notif.HasAttemptedAsync(templateCode, inv.Id, ct)) continue;
                if (!guardiansByStudent.TryGetValue(inv.StudentId, out var guardians)) continue;
                if (!students.ContainsKey(inv.StudentId)) continue;

                foreach (var g in guardians)
                {
                    guardianById[g.GuardianId] = g;
                    var key = (g.GuardianId, inv.Type);
                    if (!buckets.TryGetValue(key, out var list))
                        buckets[key] = list = new List<InvoiceRef>();
                    list.Add(new InvoiceRef(inv.Id, inv.StudentId, inv.Type, inv.Remaining, inv.DueDate));
                }
            }

            var sent = 0;
            foreach (var ((guardianId, type), list) in buckets)
            {
                var g = guardianById[guardianId];
                var total = list.Sum(i => i.Remaining);
                // Le nombre d'ENFANTS distincts, pas de factures : deux factures
                // du même enfant ne font pas « vos 2 enfants ».
                var childIds = list.Select(i => i.StudentId).Distinct().ToList();
                var name = childIds.Count == 1 ? students[childIds[0]] : null;
                // Échéance annoncée = la PLUS PROCHE du lot : c'est celle qui
                // engage, et annoncer la plus lointaine ferait rater les autres.
                var due = list.Where(i => i.DueDate != null).Select(i => i.DueDate!.Value)
                    .DefaultIfEmpty().Min();

                await notif.SendSmsAsync(new NotificationSmsRequest(
                    UserId: g.GuardianId,
                    RawPhone: g.PhoneNumber,
                    PreferredLanguage: g.PreferredLanguage ?? "fr",
                    Message: compose(type, childIds.Count, total, name,
                        due == default ? null : due),
                    Bilingual: bilingual,
                    TemplateCode: templateCode,
                    RelatedEntityId: list[0].Id,
                    PushRoute: "/guardian/invoices",
                    TriggerSource: "cron:overdue-reminder",
                    GroupedEntityIds: list.Select(i => i.Id).ToList()), ct);
                sent++;
            }

            return sent;
        }

        private static DateTime NextFireUtc(DateTime nowUtc)
        {
            var todayRun = nowUtc.Date + RunAtUtc;
            return nowUtc < todayRun ? todayRun : todayRun.AddDays(1);
        }
    }
}
