using Idara.API.Data;
using Idara.API.Enums;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>
    /// Réconciliation quotidienne (02:30 UTC — loin de l'invoice-cron 02:00 et du
    /// backup 03:00). Deux responsabilités :
    ///
    /// 1. <b>Invariant comptable</b> : le solde réserve du compte marchand SenePay
    ///    doit couvrir ce qu'on doit aux écoles, soit
    ///    <c>réserve ≥ Σ(AvailableBalance + PendingBalance) − epsilon</c>. Si on
    ///    doit PLUS que ce que SenePay détient (au-delà de l'epsilon), c'est le
    ///    signe d'une double dépense ou d'une fuite → alerte ReconciliationMismatch.
    ///    (réserve &gt; Σ est normal : marge plateforme + frais non répartis.)
    ///
    /// 2. <b>Backstop</b> : déclenche un passage du PayoutVerificationJob pour
    ///    re-poller les retraits restés en UnderVerification, au cas où le job
    ///    de poll aurait pris du retard.
    /// </summary>
    public class PayoutReconciliationJob : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly PayoutVerificationJob _verificationJob;
        private readonly ILogger<PayoutReconciliationJob> _logger;

        private static readonly TimeSpan RunAtUtc = new(hours: 2, minutes: 30, seconds: 0);

        /// <summary>Tolérance (FCFA) : sous cet écart on ne lève pas d'alerte (arrondis, timing).</summary>
        private const long EpsilonFcfa = 50;

        public PayoutReconciliationJob(
            IServiceScopeFactory scopeFactory,
            PayoutVerificationJob verificationJob,
            ILogger<PayoutReconciliationJob> logger)
        {
            _scopeFactory = scopeFactory;
            _verificationJob = verificationJob;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "[payout-reconcile] Démarré. Prochain tick {Next:yyyy-MM-dd HH:mm} UTC",
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
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[payout-reconcile] Échec du tick");
                }
            }
        }

        /// <summary>Public pour rejeu manuel depuis un endpoint admin.</summary>
        public async Task RunOnceAsync(CancellationToken ct)
        {
            // Backstop : re-poll les UnderVerification en retard.
            try { await _verificationJob.RunOnceAsync(ct); }
            catch (Exception ex) { _logger.LogError(ex, "[payout-reconcile] Backstop verify échoué"); }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var senepay = scope.ServiceProvider.GetRequiredService<ISenePayClient>();
            var settlement = scope.ServiceProvider.GetRequiredService<IPayoutSettlementService>();

            // Σ(Available + Pending) dû aux écoles.
            var owedToSchools = await db.SchoolWallets
                .SumAsync(w => w.AvailableBalance + w.PendingBalance, ct);

            long reserve;
            try
            {
                var balance = await senepay.GetMerchantBalanceAsync(ct);
                reserve = balance.ReserveBalanceFcfa;
            }
            catch (SenePayApiException ex)
            {
                _logger.LogError(ex, "[payout-reconcile] Solde marchand SenePay indisponible — invariant non vérifié ce tick");
                return;
            }

            var shortfall = owedToSchools - reserve; // > 0 = on doit plus que la réserve.

            _logger.LogInformation(
                "[payout-reconcile] Invariant : owedToSchools={Owed} reserve={Reserve} shortfall={Shortfall}",
                owedToSchools, reserve, shortfall);

            if (shortfall > EpsilonFcfa)
            {
                await settlement.RaiseAlertAsync(
                    PayoutAlertType.ReconciliationMismatch,
                    null, null,
                    $"Écart de réconciliation : on doit {owedToSchools} FCFA aux écoles mais la réserve " +
                    $"marchand n'est que de {reserve} FCFA (manque {shortfall} FCFA). " +
                    "Possible double dépense ou fuite — audit requis.",
                    new { owedToSchools, reserve, shortfall, epsilon = EpsilonFcfa },
                    ct);
            }
        }

        private static DateTime NextFireUtc(DateTime nowUtc)
        {
            var todayRun = nowUtc.Date + RunAtUtc;
            return nowUtc < todayRun ? todayRun : todayRun.AddDays(1);
        }
    }
}
