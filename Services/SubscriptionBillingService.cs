using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    public enum BillingOutcome { Paid, Insufficient, Transitioned, NoAction, Error }

    public class SubscriptionBillingReport
    {
        public DateTime RunAtUtc { get; set; }
        public int Considered { get; set; }
        public int Charged { get; set; }
        public int Insufficient { get; set; }
        public int MovedToReadOnly { get; set; }
        public int MovedToSuspended { get; set; }
        public int Errors { get; set; }
    }

    public interface ISubscriptionBillingService
    {
        /// <summary>Traite UN abonnement (débit + transitions d'état) sous verrou wallet. Idempotent.</summary>
        Task<BillingOutcome> ProcessAsync(int subscriptionId, DateTime nowUtc, CancellationToken ct);

        /// <summary>Batch : traite tous les abonnements échus ou en impayé.</summary>
        Task<SubscriptionBillingReport> RunOnceAsync(DateTime nowUtc, CancellationToken ct);

        /// <summary>Re-tente le prélèvement de l'abo d'une école si elle est en impayé (appelé après un crédit wallet).</summary>
        Task<BillingOutcome> RetryForSchoolAsync(int schoolId, DateTime nowUtc, CancellationToken ct);
    }

    /// <summary>
    /// Moteur de facturation des abonnements plateforme + machine à états
    /// (Trial → Active → PendingPayment → ReadOnly → Suspended). TOUT mouvement
    /// de solde passe par <see cref="WalletLockExtensions.LockWalletAsync"/> sous
    /// transaction (cf. gotcha §69) : le cron et le retry-webhook ne peuvent pas
    /// double-débiter le même abo. Append-only côté wallet (WalletTransaction
    /// Debit signé négatif). Best-effort : ne lève jamais vers l'appelant batch.
    /// </summary>
    public class SubscriptionBillingService : ISubscriptionBillingService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<SubscriptionBillingService> _logger;

        public SubscriptionBillingService(AppDbContext db, ILogger<SubscriptionBillingService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<SubscriptionBillingReport> RunOnceAsync(DateTime nowUtc, CancellationToken ct)
        {
            var report = new SubscriptionBillingReport { RunAtUtc = nowUtc };

            // Échus (Trial/Active dont NextBillingAt passé) OU déjà en impayé
            // (PendingPayment/ReadOnly à re-tenter / faire avancer dans la SM).
            var ids = await _db.Subscriptions
                .Where(s =>
                    ((s.Status == SubscriptionStatus.Trial || s.Status == SubscriptionStatus.Active)
                        && s.NextBillingAt <= nowUtc)
                    || s.Status == SubscriptionStatus.PendingPayment
                    || s.Status == SubscriptionStatus.ReadOnly)
                .Select(s => s.Id)
                .ToListAsync(ct);

            report.Considered = ids.Count;

            foreach (var id in ids)
            {
                try
                {
                    var outcome = await ProcessAsync(id, nowUtc, ct);
                    switch (outcome)
                    {
                        case BillingOutcome.Paid: report.Charged++; break;
                        case BillingOutcome.Insufficient: report.Insufficient++; break;
                    }
                }
                catch (Exception ex)
                {
                    report.Errors++;
                    _logger.LogError(ex, "[subscription-billing] Échec traitement abo {Id}", id);
                }
            }

            _logger.LogInformation(
                "[subscription-billing] Terminé. Vus={Considered} Prélevés={Charged} Insuffisants={Insufficient} Erreurs={Errors}",
                report.Considered, report.Charged, report.Insufficient, report.Errors);
            return report;
        }

        public async Task<BillingOutcome> RetryForSchoolAsync(int schoolId, DateTime nowUtc, CancellationToken ct)
        {
            var sub = await _db.Subscriptions
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId
                    && (s.Status == SubscriptionStatus.PendingPayment || s.Status == SubscriptionStatus.ReadOnly), ct);
            if (sub == null) return BillingOutcome.NoAction;
            return await ProcessAsync(sub.Id, nowUtc, ct);
        }

        public async Task<BillingOutcome> ProcessAsync(int subscriptionId, DateTime nowUtc, CancellationToken ct)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var sub = await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
            if (sub == null) { await tx.RollbackAsync(ct); return BillingOutcome.NoAction; }

            // Verrou pessimiste wallet : sérialise cron vs retry-webhook concurrents.
            var wallet = await _db.LockWalletAsync(sub.SchoolId, ct);
            if (wallet == null)
            {
                await tx.RollbackAsync(ct);
                _logger.LogWarning("[subscription-billing] Wallet absent pour École {SchoolId}", sub.SchoolId);
                return BillingOutcome.Error;
            }

            // Recharge l'abo SOUS le verrou (un autre process a pu le faire avancer).
            await _db.Entry(sub).ReloadAsync(ct);

            var dueNow = (sub.Status == SubscriptionStatus.Trial || sub.Status == SubscriptionStatus.Active)
                && sub.NextBillingAt <= nowUtc;
            var inArrears = sub.Status == SubscriptionStatus.PendingPayment || sub.Status == SubscriptionStatus.ReadOnly;

            if (!dueNow && !inArrears)
            {
                await tx.RollbackAsync(ct);
                return BillingOutcome.NoAction; // déjà à jour / pas encore échu
            }

            BillingOutcome outcome;

            if (wallet.AvailableBalance >= sub.AmountFcfa)
            {
                // ----- Prélèvement réussi -----
                var amount = sub.AmountFcfa;
                wallet.AvailableBalance -= amount;
                wallet.UpdatedAt = nowUtc;

                var walletTx = new WalletTransaction
                {
                    SchoolId = sub.SchoolId,
                    Type = WalletTransactionType.Debit,
                    AmountFcfa = -amount,
                    BalanceAfter = wallet.AvailableBalance,
                    RelatedEntity = WalletRelatedEntity.Subscription,
                    RelatedId = sub.Id,
                    Note = "Prélèvement abonnement plateforme",
                    OccurredAt = nowUtc
                };
                _db.WalletTransactions.Add(walletTx);

                var periodStart = sub.NextBillingAt;
                var periodEnd = AdvanceCycle(periodStart, sub.BillingCycle).AddDays(-1);

                var invoice = await GetOrCreateInvoiceAsync(sub, periodStart, periodEnd, amount, nowUtc, ct);
                invoice.Status = SubscriptionInvoiceStatus.Paid;
                invoice.AmountFcfa = amount;
                invoice.PaidAt = nowUtc;

                // Avance le cycle + repasse Active + RAZ quota + nettoie les dates SM.
                sub.Status = SubscriptionStatus.Active;
                sub.ActivatedAt = nowUtc;
                sub.NextBillingAt = AdvanceCycle(periodStart, sub.BillingCycle);
                sub.NotificationUsedThisCycle = 0;
                sub.GracePeriodEndsAt = null;
                sub.ReadOnlyEndsAt = null;
                sub.UpdatedAt = nowUtc;

                await _db.SaveChangesAsync(ct);
                invoice.WalletTransactionId = walletTx.Id;
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "[subscription-billing] École {SchoolId} prélevée {Amount} FCFA → Active (next {Next:yyyy-MM-dd})",
                    sub.SchoolId, amount, sub.NextBillingAt);
                return BillingOutcome.Paid;
            }

            // ----- Solde insuffisant : facture en attente + avance dans la SM -----
            var dueStart = sub.NextBillingAt;
            var dueEnd = AdvanceCycle(dueStart, sub.BillingCycle).AddDays(-1);
            var pendingInvoice = await GetOrCreateInvoiceAsync(sub, dueStart, dueEnd, sub.AmountFcfa, nowUtc, ct);
            pendingInvoice.Status = SubscriptionInvoiceStatus.Pending;

            outcome = BillingOutcome.Insufficient;

            if (sub.Status == SubscriptionStatus.Trial || sub.Status == SubscriptionStatus.Active)
            {
                // 1er échec → entre en grâce (7 j).
                sub.Status = SubscriptionStatus.PendingPayment;
                sub.GracePeriodEndsAt = nowUtc.AddDays(7);
            }
            else if (sub.Status == SubscriptionStatus.PendingPayment
                     && sub.GracePeriodEndsAt is { } g && g <= nowUtc)
            {
                // Grâce expirée → ReadOnly (14 j).
                sub.Status = SubscriptionStatus.ReadOnly;
                sub.ReadOnlyEndsAt = nowUtc.AddDays(14);
                outcome = BillingOutcome.Transitioned;
            }
            else if (sub.Status == SubscriptionStatus.ReadOnly
                     && sub.ReadOnlyEndsAt is { } r && r <= nowUtc)
            {
                // ReadOnly expiré → Suspended.
                sub.Status = SubscriptionStatus.Suspended;
                sub.SuspendedAt = nowUtc;
                outcome = BillingOutcome.Transitioned;
            }

            sub.UpdatedAt = nowUtc;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return outcome;
        }

        /// <summary>
        /// Retrouve la facture de la période (hors annulée) ou en crée une neuve.
        /// L'UNIQUE filtré (SubscriptionId, PeriodStart) garantit l'unicité.
        /// </summary>
        private async Task<SubscriptionInvoice> GetOrCreateInvoiceAsync(
            Subscription sub, DateTime periodStart, DateTime periodEnd, long amount, DateTime nowUtc, CancellationToken ct)
        {
            var inv = await _db.SubscriptionInvoices.FirstOrDefaultAsync(
                i => i.SubscriptionId == sub.Id
                  && i.PeriodStart == periodStart
                  && i.Status != SubscriptionInvoiceStatus.Cancelled, ct);
            if (inv != null) return inv;

            inv = new SubscriptionInvoice
            {
                SubscriptionId = sub.Id,
                SchoolId = sub.SchoolId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                AmountFcfa = amount,
                Status = SubscriptionInvoiceStatus.Pending,
                IssuedAt = nowUtc,
                CreatedAt = nowUtc
            };
            _db.SubscriptionInvoices.Add(inv);
            return inv;
        }

        private static DateTime AdvanceCycle(DateTime from, BillingCycle cycle) =>
            cycle == BillingCycle.Annual ? from.AddYears(1) : from.AddMonths(1);
    }
}
