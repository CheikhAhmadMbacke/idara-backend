using Idara.API.Data;
using Idara.API.Enums;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>
    /// Poll des retraits restés en <see cref="WithdrawalStatus.UnderVerification"/>
    /// (issue indéterminée). Toutes les ~60s, interroge
    /// <c>GET /api/v1/payouts/{id}</c> (autoritatif) pour chaque retrait dont le
    /// <c>NextVerificationAt</c> est échu, avec back-off, jusqu'à un état terminal :
    ///
    /// - `completed`            → <c>SettleCompletedAsync</c> (débit définitif).
    /// - `failed` / `cancelled` → <c>SettleFailedAsync</c> (restitution).
    /// - 404 (jamais créé)      → <c>SettleFailedAsync</c> (aucun fonds sorti — sûr).
    /// - non terminal / erreur  → back-off, on RESTE en vérification (jamais de
    ///                            restitution sur état ambigu — anti double dépense).
    ///
    /// Au-delà de 48h sans résolution → alerte <c>StuckUnderVerification</c> (une fois).
    ///
    /// Pattern singleton + HostedService (cf. MonthlyInvoiceGenerationJob) pour
    /// pouvoir aussi être déclenché manuellement depuis un endpoint admin.
    /// </summary>
    public class PayoutVerificationJob : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PayoutVerificationJob> _logger;

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan StuckThreshold = TimeSpan.FromHours(48);
        private const int BatchSize = 50;

        public PayoutVerificationJob(
            IServiceScopeFactory scopeFactory,
            ILogger<PayoutVerificationJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[payout-verify] Démarré (intervalle {Interval}s)", PollInterval.TotalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // Catch global obligatoire : un throw tuerait le service.
                    _logger.LogError(ex, "[payout-verify] Échec du tick");
                }

                try
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Traite tous les retraits UnderVerification échus. Public pour rejeu
        /// manuel (endpoint admin / job de réconciliation backstop).
        /// </summary>
        public async Task<int> RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var senepay = scope.ServiceProvider.GetRequiredService<ISenePayClient>();
            var settlement = scope.ServiceProvider.GetRequiredService<IPayoutSettlementService>();

            var now = DateTime.UtcNow;

            var due = await db.Withdrawals
                .Where(w => w.Status == WithdrawalStatus.UnderVerification
                            && (w.NextVerificationAt == null || w.NextVerificationAt <= now))
                .OrderBy(w => w.NextVerificationAt)
                .Take(BatchSize)
                .Select(w => new { w.Id, w.SchoolId, w.VerificationStartedAt })
                .ToListAsync(ct);

            if (due.Count == 0) return 0;

            _logger.LogInformation("[payout-verify] {Count} retrait(s) à vérifier", due.Count);

            var processed = 0;
            foreach (var item in due)
            {
                try
                {
                    await VerifyOneAsync(db, senepay, settlement, item.Id, item.SchoolId, item.VerificationStartedAt, ct);
                    processed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[payout-verify] Échec vérification Withdrawal {Id}", item.Id);
                }
            }

            return processed;
        }

        private async Task VerifyOneAsync(
            AppDbContext db, ISenePayClient senepay, IPayoutSettlementService settlement,
            int withdrawalId, int? schoolId, DateTime? startedAt, CancellationToken ct)
        {
            // external_id = Withdrawal.Id (cf. SchoolWalletController.Withdraw).
            DTOs.Senepay.SenePayPayoutStatusResponse? status;
            try
            {
                status = await senepay.GetPayoutStatusAsync(withdrawalId.ToString(), ct);
            }
            catch (SenePayApiException ex)
            {
                // Timeout / 5xx : indéterminé. On reste en vérification, back-off.
                _logger.LogWarning(ex,
                    "[payout-verify] GET status indéterminé pour Withdrawal {Id} — back-off", withdrawalId);
                await BumpBackoffAsync(db, withdrawalId, startedAt, settlement, ct);
                return;
            }

            // 404 : SenePay n'a aucun décaissement pour cet external_id → jamais
            // créé (l'external_id est idempotent : s'il existait, le GET le
            // trouverait). Aucun fonds sorti → restitution sûre.
            if (status == null)
            {
                _logger.LogInformation(
                    "[payout-verify] Withdrawal {Id} : 404 SenePay (jamais créé) → restitution", withdrawalId);
                await settlement.SettleFailedAsync(
                    withdrawalId, "Décaissement introuvable chez SenePay (jamais créé)", null, null, "poll", ct);
                return;
            }

            var s = status.Status?.ToLowerInvariant();
            switch (s)
            {
                case "completed":
                    await settlement.SettleCompletedAsync(
                        withdrawalId, status.DisbursementId,
                        (long)Math.Round(status.Fees?.Provider ?? 0, MidpointRounding.AwayFromZero),
                        (long)Math.Round(status.NetAmount, MidpointRounding.AwayFromZero),
                        status.CompletedAt, "poll", ct);
                    break;

                case "failed":
                case "cancelled":
                    await settlement.SettleFailedAsync(
                        withdrawalId,
                        status.ErrorMessage ?? status.ErrorCode ?? s,
                        status.DisbursementId, status.CompletedAt, "poll", ct);
                    break;

                default:
                    // pending / processing / submitted / pending_verification /
                    // pending_approval : toujours indéterminé → back-off.
                    await BumpBackoffAsync(db, withdrawalId, startedAt, settlement, ct);
                    break;
            }
        }

        /// <summary>
        /// Reprogramme le prochain poll avec back-off et incrémente le compteur,
        /// gardé par <c>Status == UnderVerification</c> (ExecuteUpdate : ne
        /// clobbe pas un retrait clôturé entre-temps par un webhook). Lève une
        /// alerte StuckUnderVerification au-delà de 48h (une seule fois).
        /// </summary>
        private async Task BumpBackoffAsync(
            AppDbContext db, int withdrawalId, DateTime? startedAt,
            IPayoutSettlementService settlement, CancellationToken ct)
        {
            var now = DateTime.UtcNow;

            // On lit le compteur courant pour calculer le back-off et le seuil.
            var current = await db.Withdrawals
                .Where(w => w.Id == withdrawalId && w.Status == WithdrawalStatus.UnderVerification)
                .Select(w => new { w.VerificationAttempts, w.SchoolId })
                .FirstOrDefaultAsync(ct);
            if (current == null) return; // déjà tranché

            var nextAttempts = current.VerificationAttempts + 1;
            var delay = BackoffFor(nextAttempts);

            await db.Withdrawals
                .Where(w => w.Id == withdrawalId && w.Status == WithdrawalStatus.UnderVerification)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(w => w.VerificationAttempts, nextAttempts)
                    .SetProperty(w => w.LastCheckedAt, now)
                    .SetProperty(w => w.NextVerificationAt, now.Add(delay)), ct);

            // Au-delà de 48h sans résolution → alerte (une seule fois : on
            // n'alerte que si aucune StuckUnderVerification n'existe déjà).
            if (startedAt != null && now - startedAt.Value > StuckThreshold)
            {
                var alreadyAlerted = await db.PayoutAlerts.AnyAsync(
                    a => a.WithdrawalId == withdrawalId && a.Type == PayoutAlertType.StuckUnderVerification, ct);
                if (!alreadyAlerted)
                {
                    await settlement.RaiseAlertAsync(
                        PayoutAlertType.StuckUnderVerification,
                        current.SchoolId, withdrawalId,
                        $"Décaissement #{withdrawalId} coincé en vérification depuis plus de 48h " +
                        $"({nextAttempts} tentatives). Réconciliation manuelle SenePay/AfribaPay requise.",
                        new { withdrawalId, startedAt, attempts = nextAttempts },
                        ct);
                }
            }
        }

        /// <summary>Back-off : 1→1min, 2→5min, 3→15min, ≥4→1h.</summary>
        private static TimeSpan BackoffFor(int attempts) => attempts switch
        {
            <= 1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromHours(1)
        };
    }
}
