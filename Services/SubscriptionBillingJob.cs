namespace Idara.API.Services
{
    /// <summary>
    /// Cron quotidien (02:30 UTC) qui prélève les abonnements plateforme échus
    /// et fait avancer la machine à états (grâce → ReadOnly → Suspended). Délègue
    /// toute la logique à <see cref="ISubscriptionBillingService"/> (réutilisée
    /// aussi par le retry post-crédit-wallet). Enregistré en singleton +
    /// HostedService pour permettre un déclenchement manuel (endpoint SuperAdmin).
    ///
    /// 02:15 UTC : après le cron de factures parents (02:00), avant la
    /// réconciliation payout (02:30) et le backup (03:00).
    /// </summary>
    public class SubscriptionBillingJob : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SubscriptionBillingJob> _logger;

        /// <summary>
        /// 10:00 UTC = 10 h à Dakar.
        /// </summary>
        /// <remarks>
        /// 🔴 <b>Déplacé de 02:15 le 2026-09-19</b>, quand l'échéance est passée
        /// au 8 de chaque mois. Deux raisons, et la seconde est la plus lourde :
        /// <list type="number">
        ///   <item>à 2 h du matin, on prélève avec l'argent de la <b>veille au
        ///   soir</b> — or c'est justement le jour du prélèvement que la
        ///   trésorerie des écoles est au plus haut ;</item>
        ///   <item>surtout, le SMS « nous n'avons pas pu prélever » partait à
        ///   2 h du matin, quand personne ne peut réagir. Une relance qui
        ///   réveille est une relance perdue.</item>
        /// </list>
        /// </remarks>
        private static readonly TimeSpan RunAtUtc = new(hours: 10, minutes: 0, seconds: 0);

        public SubscriptionBillingJob(IServiceScopeFactory scopeFactory, ILogger<SubscriptionBillingJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "[subscription-billing] Démarré. Prochain tick à {Next:yyyy-MM-dd HH:mm} UTC",
                NextFireUtc(DateTime.UtcNow));

            // 🔁 RATTRAPAGE au démarrage (2026-10-10, §305) : si l'heure du jour
            // est déjà passée, on rejoue le cycle tout de suite. Un service qui
            // redémarre à 10:00 pile — un déploiement suffit — manquait le tick
            // du jour, et une échéance corrigée à la main attendait le lendemain.
            // Sans risque : un cycle rejoué ne débite pas deux fois (verrou du
            // wallet + échéance avancée) et les SMS sont dédupliqués par cycle.
            // Jamais AVANT l'heure : prélever à 2 h du matin est précisément ce
            // que le passage à 10:00 a voulu éviter.
            if (DateTime.UtcNow >= DateTime.UtcNow.Date + RunAtUtc)
            {
                try
                {
                    // Laisse la reprise de démarrage (DbInitializer) finir.
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    _logger.LogInformation("[subscription-billing] Rattrapage du cycle du jour au démarrage.");
                    await RunOnceAsync(DateTime.UtcNow, stoppingToken);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[subscription-billing] Échec du rattrapage au démarrage");
                }
            }

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
                    _logger.LogError(ex, "[subscription-billing] Échec du tick");
                }
            }
        }

        /// <summary>Logique extraite (publique) pour le déclenchement manuel SuperAdmin.</summary>
        public async Task<SubscriptionBillingReport> RunOnceAsync(DateTime nowUtc, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ISubscriptionBillingService>();
            return await svc.RunOnceAsync(nowUtc, ct);
        }

        private static DateTime NextFireUtc(DateTime nowUtc)
        {
            var todayRun = nowUtc.Date + RunAtUtc;
            return nowUtc < todayRun ? todayRun : todayRun.AddDays(1);
        }
    }
}
