using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Common.Extensions
{
    /// <summary>
    /// Helpers d'abonnement plateforme (Phase 4). Garantissent qu'une école a un
    /// abonnement (créé en Trial 30j), de façon idempotente et tolérante à la
    /// concurrence — même logique que <see cref="PaymentFoundationsExtensions"/>.
    /// </summary>
    public static class SubscriptionExtensions
    {
        public const string DefaultPlanCode = "daara";
        public const int TrialDays = 30;

        /// <summary>
        /// Crée l'abonnement Trial 30j de l'école s'il n'existe pas encore. Le
        /// plan par défaut est « daara » (le plus petit) — le SuperAdmin peut le
        /// changer ensuite. Prix + quota snapshotés depuis le plan. Idempotent :
        /// no-op si l'abo existe déjà ; tolère un conflit d'unicité concurrent.
        /// </summary>
        public static async Task EnsureSubscriptionAsync(
            this AppDbContext ctx, int schoolId, CancellationToken ct = default)
        {
            var exists = await ctx.Subscriptions.AnyAsync(s => s.SchoolId == schoolId, ct);
            if (exists) return;

            var plan = await ctx.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.Code == DefaultPlanCode && !p.IsCustom, ct);

            var now = DateTime.UtcNow;
            var trialEnd = now.AddDays(TrialDays);

            var sub = new Subscription
            {
                SchoolId = schoolId,
                PlanId = plan?.Id,
                BillingCycle = BillingCycle.Monthly,
                Status = SubscriptionStatus.Trial,
                AmountFcfa = plan?.MonthlyPriceFcfa ?? 5000,
                NotificationQuota = plan?.NotificationQuota ?? 150,
                NotificationUsedThisCycle = 0,
                TrialEndsAt = trialEnd,
                NextBillingAt = trialEnd,
                CreatedAt = now,
                UpdatedAt = now
            };

            ctx.Subscriptions.Add(sub);
            try
            {
                await ctx.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Conflit d'unicité (SchoolId) : un autre chemin a créé l'abo en
                // parallèle. On détache et on considère le travail fait.
                ctx.Entry(sub).State = EntityState.Detached;
            }
        }
    }
}
