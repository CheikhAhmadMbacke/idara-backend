using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Wave;
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
            var wave = scope.ServiceProvider.GetRequiredService<IWaveClient>();
            var settlement = scope.ServiceProvider.GetRequiredService<IPayoutSettlementService>();

            var now = DateTime.UtcNow;

            var due = await db.Withdrawals
                .Where(w => w.Status == WithdrawalStatus.UnderVerification
                            && (w.NextVerificationAt == null || w.NextVerificationAt <= now)
                            // Uniquement les décaissements partis chez Wave.
                            && w.Provider == PaymentProviders.Wave)
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
                    await VerifyOneAsync(db, wave, settlement, item.Id, item.SchoolId, item.VerificationStartedAt, ct);
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
            AppDbContext db, IWaveClient wave, IPayoutSettlementService settlement,
            int withdrawalId, int? schoolId, DateTime? startedAt, CancellationToken ct)
        {
            // 🔴 Chez Wave, AUCUN webhook n'annonce l'issue d'un décaissement.
            // Ce qui suit n'est donc plus un filet de sécurité : c'est le seul
            // chemin par lequel un retrait se referme.
            //
            // On interroge par l'identifiant Wave quand on l'a. Sinon — l'appel
            // a expiré avant de nous le rendre — on cherche par NOTRE référence :
            // c'est le seul moyen de savoir si l'argent est parti quand même.
            var stored = await db.Withdrawals
                .Where(w => w.Id == withdrawalId)
                .Select(w => w.ProviderDisbursementId)
                .FirstOrDefaultAsync(ct);

            WavePayout? payout;
            try
            {
                payout = string.IsNullOrWhiteSpace(stored)
                    ? await wave.FindPayoutByClientReferenceAsync(withdrawalId.ToString(), ct)
                    : await wave.GetPayoutAsync(stored, ct);
            }
            catch (WaveApiException ex)
            {
                // Délai / 5xx : indéterminé. On reste en vérification, back-off.
                _logger.LogWarning(ex,
                    "[payout-verify] Lecture indéterminée pour Withdrawal {Id} — back-off", withdrawalId);
                await BumpBackoffAsync(db, withdrawalId, startedAt, settlement, ct);
                return;
            }

            // Introuvable des DEUX façons : ni par identifiant, ni par notre
            // référence. Wave n'a donc jamais rien créé — aucun franc n'est
            // sorti, la restitution est sûre.
            if (payout is null)
            {
                _logger.LogInformation(
                    "[payout-verify] Withdrawal {Id} : inconnu de Wave (jamais créé) → restitution", withdrawalId);
                await settlement.SettleFailedAsync(
                    withdrawalId, "Décaissement introuvable chez Wave (jamais créé)", null, null, "poll", ct);
                return;
            }

            switch (payout.Status?.ToLowerInvariant())
            {
                case "succeeded":
                    await settlement.SettleCompletedAsync(
                        withdrawalId, payout.Id,
                        WaveClient.ParseAmount(payout.Fee),
                        WaveClient.ParseAmount(payout.ReceiveAmount),
                        payout.Timestamp, "poll", ct);
                    break;

                case "failed":
                    await settlement.SettleFailedAsync(
                        withdrawalId,
                        payout.PayoutError?.Code ?? payout.PayoutError?.Message ?? "failed",
                        payout.Id, payout.Timestamp, "poll", ct);
                    break;

                case "reversed":
                    // L'argent est parti puis revenu (réversion sous 3 jours).
                    // Du point de vue de l'école, le retrait n'a pas eu lieu :
                    // on restitue, en le DISANT — un solde qui remonte sans
                    // explication est plus inquiétant qu'un échec annoncé.
                    _logger.LogWarning(
                        "[payout-verify] Withdrawal {Id} RÉVERSÉ par Wave ({PayoutId})", withdrawalId, payout.Id);
                    await settlement.SettleFailedAsync(
                        withdrawalId, "Décaissement annulé (réversion Wave)",
                        payout.Id, payout.Timestamp, "poll", ct);
                    break;

                default:
                    // `processing` : toujours indéterminé → back-off.
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
