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

                // Élève absent du dictionnaire = hors effectif (sorti/supprimé) :
                // aucun rappel automatique. Avant : repli « votre enfant » et le
                // SMS partait quand même.
                if (!students.TryGetValue(inv.StudentId, out var eleve))
                    continue;
                // Libellé dérivé du TYPE (§158) : avant le 2026-08-27, une
                // facture d'inscription en retard était rappelée avec le mot
                // « mensualite » — un mot faux sur un SMS d'argent.
                var msg = inv.Type == InvoiceType.Registration
                    ? NotificationTemplates.RegistrationOverdue(eleve, inv.Remaining)
                    : NotificationTemplates.InvoiceOverdue(eleve, inv.Remaining);
                foreach (var g in guardians)
                {
                    await notif.SendSmsAsync(new NotificationSmsRequest(
                        UserId: g.GuardianId,
                        RawPhone: g.PhoneNumber,
                        PreferredLanguage: g.PreferredLanguage ?? "fr",
                        Message: msg,
                        Bilingual: bilingual,
                        TemplateCode: "INVOICE_OVERDUE",
                        RelatedEntityId: inv.Id,
                        PushRoute: "/guardian/invoices"), ct);
                    sent++;
                }
            }

            // ----- Passe « échéance proche » (une seule fois par facture) -----
            int dueSoonSent = 0;
            foreach (var inv in dueSoon)
            {
                if (await notif.HasAttemptedAsync("INVOICE_DUE_SOON", inv.Id, ct))
                    continue;
                if (!guardiansByStudent.TryGetValue(inv.StudentId, out var guardians))
                    continue;
                if (!students.TryGetValue(inv.StudentId, out var eleve))
                    continue;
                var msg = NotificationTemplates.PaymentDueSoon(
                    eleve, inv.Remaining, inv.DueDate, inv.Type);
                foreach (var g in guardians)
                {
                    await notif.SendSmsAsync(new NotificationSmsRequest(
                        UserId: g.GuardianId,
                        RawPhone: g.PhoneNumber,
                        PreferredLanguage: g.PreferredLanguage ?? "fr",
                        Message: msg,
                        Bilingual: bilingual,
                        TemplateCode: "INVOICE_DUE_SOON",
                        RelatedEntityId: inv.Id,
                        PushRoute: "/guardian/invoices"), ct);
                    dueSoonSent++;
                }
            }

            _logger.LogInformation(
                "[overdue-cron] Tick {Date:yyyy-MM-dd} : {Overdue} facture(s) en retard ({Sent} rappel(s)), {DueSoon} limite(s) proche(s) ({DueSoonSent} rappel(s))",
                today, overdue.Count, sent, dueSoon.Count, dueSoonSent);
            return sent + dueSoonSent;
        }

        private static DateTime NextFireUtc(DateTime nowUtc)
        {
            var todayRun = nowUtc.Date + RunAtUtc;
            return nowUtc < todayRun ? todayRun : todayRun.AddDays(1);
        }
    }
}
