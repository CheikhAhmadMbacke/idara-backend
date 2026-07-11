using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>
    /// Cron quotidien (08:30 UTC) qui prévient les écoles dont l'abonnement arrive
    /// à échéance (J-3) AVANT le prélèvement, pour qu'elles rechargent à temps et
    /// évitent le passage en lecture seule. Comble le trou produit : sans ce
    /// rappel, l'école ne découvrait l'impayé qu'APRÈS coup, en tapant sur un
    /// écran devenu bloqué.
    ///
    /// Push uniquement (SMS dormant + coûteux). Dédup par cycle via
    /// <see cref="INotificationService.HasAttemptedSinceAsync"/> (au plus un
    /// rappel par échéance). Prévient TOUJOURS (décision produit : l'école veut
    /// savoir avant chaque prélèvement) avec un message adapté : « sera prélevé
    /// automatiquement » si le wallet couvre déjà, sinon « rechargez pour éviter
    /// l'interruption ».
    ///
    /// 08:30 UTC = 08:30 Dakar : matin raisonnable, distinct des crons 02:00/02:15/
    /// 02:30/03:00 et du rappel factures parents (09:00).
    /// </summary>
    public class SubscriptionRenewalReminderJob : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SubscriptionRenewalReminderJob> _logger;

        /// <summary>Fenêtre d'avertissement : on prévient dès que l'échéance est à ≤ N jours.</summary>
        private const int LeadDays = 3;

        private static readonly TimeSpan RunAtUtc = new(hours: 8, minutes: 30, seconds: 0);

        public SubscriptionRenewalReminderJob(
            IServiceScopeFactory scopeFactory,
            ILogger<SubscriptionRenewalReminderJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "[subscription-reminder] Démarré. Prochain tick à {Next:yyyy-MM-dd HH:mm} UTC",
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
                    // Catch global : un throw ici tuerait le service (plus de tick demain).
                    _logger.LogError(ex, "[subscription-reminder] Échec exécution du tick");
                }
            }
        }

        /// <summary>Public pour rejeu manuel (endpoint admin éventuel).</summary>
        public async Task<int> RunOnceAsync(DateTime nowUtc, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notif = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var cutoff = nowUtc.AddDays(LeadDays);

            // Abos actifs/essai dont l'échéance tombe dans la fenêtre [maintenant, J+3].
            // (Les impayés sont déjà en lecture seule → ils voient le blocage 402,
            // pas besoin d'un rappel « à venir ».)
            var due = await db.Subscriptions
                .Where(s => (s.Status == SubscriptionStatus.Trial || s.Status == SubscriptionStatus.Active)
                            && s.NextBillingAt > nowUtc
                            && s.NextBillingAt <= cutoff)
                .Select(s => new { s.Id, s.SchoolId, s.AmountFcfa, s.NextBillingAt })
                .ToListAsync(ct);

            if (due.Count == 0) return 0;

            int sent = 0;
            foreach (var sub in due)
            {
                // Dédup par cycle : au plus un rappel par échéance. La fenêtre de
                // regard « depuis J-10 » ne peut couvrir que l'échéance courante
                // (le cycle le plus court = mensuel ≈ 28 j), jamais la précédente.
                if (await notif.HasAttemptedSinceAsync(
                        "SUBSCRIPTION_DUE_SOON", sub.Id, sub.NextBillingAt.AddDays(-10), ct))
                    continue;

                // Le wallet couvre-t-il déjà le prélèvement ? → adapte le message
                // (« sera prélevé automatiquement » vs « rechargez »).
                var wallet = await db.SchoolWallets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(w => w.SchoolId == sub.SchoolId, ct);
                var walletCovers = wallet != null && wallet.AvailableBalance >= sub.AmountFcfa;

                var admins = await db.Users
                    .Where(u => u.SchoolId == sub.SchoolId && !u.IsDeleted
                                && (u.Role == UserRoles.SchoolAdmin || u.Role == UserRoles.SchoolStaff))
                    .Select(u => new { u.Id, u.PreferredLanguage })
                    .ToListAsync(ct);
                if (admins.Count == 0) continue;

                var msg = NotificationTemplates.SubscriptionDueSoon(
                    sub.AmountFcfa, sub.NextBillingAt, walletCovers);
                foreach (var a in admins)
                {
                    await notif.SendPushOnlyAsync(new PushOnlyRequest(
                        UserId: a.Id,
                        PreferredLanguage: a.PreferredLanguage ?? "fr",
                        Message: msg,
                        TemplateCode: "SUBSCRIPTION_DUE_SOON",
                        RelatedEntityId: sub.Id,
                        PushRoute: "/school/wallet/topup"), ct);
                    sent++;
                }
            }

            _logger.LogInformation(
                "[subscription-reminder] Tick {Date:yyyy-MM-dd} : {Due} échéance(s) proche(s), {Sent} rappel(s) push envoyé(s)",
                nowUtc.Date, due.Count, sent);
            return sent;
        }

        private static DateTime NextFireUtc(DateTime nowUtc)
        {
            var todayRun = nowUtc.Date + RunAtUtc;
            return nowUtc < todayRun ? todayRun : todayRun.AddDays(1);
        }
    }
}
