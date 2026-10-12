using Idara.API.Data;
using Idara.API.DTOs.Wave;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>
    /// Recopie le registre du portefeuille Wave dans <see cref="WaveLedgerEntry"/>,
    /// au démarrage puis toutes les heures.
    ///
    /// <para>🔑 <b>Lecture seule</b> : uniquement des <c>GET /v1/transactions</c>.
    /// Aucun franc ne peut bouger d'ici.</para>
    ///
    /// <para>Incrémental : on reprend à la VEILLE du dernier jour recopié (une
    /// journée n'est close qu'à minuit, et une ligne tardive de la veille ne doit
    /// pas se perdre), jusqu'à aujourd'hui. Premier passage : depuis la signature
    /// du contrat Wave — avant, le portefeuille n'existait pas pour Idara.</para>
    /// </summary>
    public class WaveLedgerSyncJob : BackgroundService
    {
        /// <summary>Signature du contrat Wave (§145) : rien n'est antérieur.</summary>
        public static readonly DateOnly BackfillFrom = new(2026, 8, 13);

        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
        private const int PageSize = 200;
        private const int MaxPagesPerDay = 50;   // garde-fou : 10 000 lignes/jour

        // La tâche horaire et un calcul de réserve peuvent recopier en même
        // temps : un seul à la fois, sinon le second bute sur l'index unique.
        private static readonly SemaphoreSlim Gate = new(1, 1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<WaveLedgerSyncJob> _logger;

        public WaveLedgerSyncJob(IServiceScopeFactory scopeFactory, ILogger<WaveLedgerSyncJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[wave-ledger] Démarré (intervalle {Hours} h)", Interval.TotalHours);

            // Laisse le démarrage (migrations, seed) se terminer avant le premier appel.
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[wave-ledger] Échec de la recopie — nouvel essai au prochain tick");
                }

                try { await Task.Delay(Interval, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }

        /// <summary>Public pour un rejeu depuis un endpoint admin. Renvoie le nombre de lignes ajoutées.</summary>
        public async Task<int> SyncAsync(CancellationToken ct)
        {
            await Gate.WaitAsync(ct);
            try { return await SyncCoreAsync(ct); }
            finally { Gate.Release(); }
        }

        private async Task<int> SyncCoreAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var wave = scope.ServiceProvider.GetRequiredService<IWaveClient>();

            var last = await db.WaveLedgerEntries
                .OrderByDescending(e => e.Timestamp)
                .Select(e => (DateTime?)e.Timestamp)
                .FirstOrDefaultAsync(ct);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var from = last is DateTime l
                ? DateOnly.FromDateTime(l).AddDays(-1)
                : BackfillFrom;
            if (from < BackfillFrom) from = BackfillFrom;

            var added = 0;
            for (var day = from; day <= today; day = day.AddDays(1))
                added += await SyncDayAsync(db, wave, day, ct);

            _logger.LogInformation(
                "[wave-ledger] Recopie {From:yyyy-MM-dd} → {To:yyyy-MM-dd} : {Added} ligne(s) ajoutée(s)",
                from, today, added);
            return added;
        }

        private async Task<int> SyncDayAsync(AppDbContext db, IWaveClient wave, DateOnly day, CancellationToken ct)
        {
            var lines = new List<WaveTransaction>();
            string? cursor = null;
            for (var page = 0; page < MaxPagesPerDay; page++)
            {
                var res = await wave.GetTransactionsAsync(day, cursor, PageSize, ct);
                lines.AddRange(res.Items);
                if (res.PageInfo?.HasNextPage != true) break;
                cursor = res.PageInfo.EndCursor;
                if (string.IsNullOrWhiteSpace(cursor)) break;
            }
            if (lines.Count == 0) return 0;

            var ids = lines
                .Select(t => t.TransactionId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList();
            var known = (await db.WaveLedgerEntries
                .Where(e => ids.Contains(e.TransactionId))
                .Select(e => e.TransactionId)
                .ToListAsync(ct)).ToHashSet();

            var now = DateTime.UtcNow;
            var added = 0;
            foreach (var t in lines)
            {
                if (string.IsNullOrWhiteSpace(t.TransactionId))
                {
                    _logger.LogWarning("[wave-ledger] Ligne sans transaction_id le {Day:yyyy-MM-dd} — ignorée", day);
                    continue;
                }
                if (!known.Add(t.TransactionId!)) continue;

                db.WaveLedgerEntries.Add(new WaveLedgerEntry
                {
                    TransactionId = t.TransactionId!,
                    Timestamp = t.Timestamp is DateTime ts
                        ? DateTime.SpecifyKind(ts.ToUniversalTime(), DateTimeKind.Utc)
                        : day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    TransactionType = t.TransactionType,
                    AmountFcfa = WaveClient.ParseAmount(t.Amount),
                    FeeFcfa = Math.Abs(WaveClient.ParseAmount(t.Fee)),
                    BalanceFcfa = WaveClient.ParseAmount(t.Balance),
                    IsReversal = t.IsReversal == true,
                    ClientReference = t.ClientReference,
                    PaymentReason = t.PaymentReason,
                    CheckoutSessionId = t.CheckoutSessionId,
                    CounterpartyName = t.CounterpartyName,
                    CounterpartyMobile = t.CounterpartyMobile,
                    SyncedAt = now
                });
                added++;
            }

            if (added > 0) await db.SaveChangesAsync(ct);
            return added;
        }
    }
}
