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
                    i.Id, i.StudentId,
                    Remaining = i.AmountDueFcfa - i.AmountPaidFcfa,
                    i.Status
                })
                .ToListAsync(ct);

            if (overdue.Count == 0) return 0;

            // Responsables joignables, groupés par élève.
            var studentIds = overdue.Select(o => o.StudentId).Distinct().ToList();
            var students = await db.Students
                .Where(s => studentIds.Contains(s.Id))
                .Select(s => new { s.Id, s.FirstName, s.LastName })
                .ToDictionaryAsync(s => s.Id, s => $"{s.FirstName} {s.LastName}".Trim(), ct);

            var guardiansByStudent = (await db.StudentGuardians
                    .Where(sg => studentIds.Contains(sg.StudentId)
                                 && !sg.Guardian.IsDeleted
                                 && sg.Guardian.PhoneNumber != null)
                    .Select(sg => new { sg.StudentId, sg.GuardianId, sg.Guardian.PhoneNumber, sg.Guardian.PreferredLanguage })
                    .ToListAsync(ct))
                .GroupBy(g => g.StudentId)
                .ToDictionary(g => g.Key, g => g.ToList());

            int sent = 0;
            foreach (var inv in overdue)
            {
                // Marque la facture Overdue (cosmétique, idempotent) si encore Pending.
                if (inv.Status == InvoiceStatus.Pending)
                {
                    await db.Invoices.Where(i => i.Id == inv.Id && i.Status == InvoiceStatus.Pending)
                        .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvoiceStatus.Overdue), ct);
                }

                // Dédup : une seule tentative de rappel par facture (évite la
                // re-tentative quotidienne sans plafond si l'envoi échoue).
                if (await notif.HasAttemptedAsync("INVOICE_OVERDUE", inv.Id, ct))
                    continue;

                if (!guardiansByStudent.TryGetValue(inv.StudentId, out var guardians))
                    continue;

                var eleve = students.TryGetValue(inv.StudentId, out var name) ? name : "votre enfant";
                var msg = NotificationTemplates.InvoiceOverdue(eleve, inv.Remaining);
                foreach (var g in guardians)
                {
                    await notif.SendSmsAsync(new NotificationSmsRequest(
                        UserId: g.GuardianId,
                        RawPhone: g.PhoneNumber,
                        PreferredLanguage: g.PreferredLanguage ?? "fr",
                        Message: msg,
                        Bilingual: bilingual,
                        TemplateCode: "INVOICE_OVERDUE",
                        RelatedEntityId: inv.Id), ct);
                    sent++;
                }
            }

            _logger.LogInformation(
                "[overdue-cron] Tick {Date:yyyy-MM-dd} : {Overdue} facture(s) en retard, {Sent} rappel(s) envoyé(s)",
                today, overdue.Count, sent);
            return sent;
        }

        private static DateTime NextFireUtc(DateTime nowUtc)
        {
            var todayRun = nowUtc.Date + RunAtUtc;
            return nowUtc < todayRun ? todayRun : todayRun.AddDays(1);
        }
    }
}
