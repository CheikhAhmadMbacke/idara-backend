using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Common.Extensions
{
    /// <summary>
    /// Helpers d'abonnement plateforme (Phase 4). Garantissent qu'une école a un
    /// abonnement d'essai, de façon idempotente et tolérante à la concurrence —
    /// même logique que <see cref="PaymentFoundationsExtensions"/>.
    /// L'essai dure 30 jours AU MOINS, puis court jusqu'au jour de prélèvement
    /// commun (<see cref="SubscriptionSchedule"/>).
    /// </summary>
    public static class SubscriptionExtensions
    {
        public const string DefaultPlanCode = "daara";

        /// <summary>
        /// Durée MINIMALE de l'essai gratuit.
        /// </summary>
        /// <remarks>
        /// ⚠️ Conservé pour les appelants existants, mais la règle vit désormais
        /// dans <see cref="SubscriptionSchedule.MinimumTrialDays"/> : depuis le
        /// 2026-09-19, l'essai ne dure plus 30 jours PILE, il dure 30 jours au
        /// moins, puis se prolonge jusqu'au jour de prélèvement commun.
        /// </remarks>
        public const int TrialDays = SubscriptionSchedule.MinimumTrialDays;

        /// <summary>
        /// Crée l'abonnement d'essai de l'école s'il n'existe pas encore. Le
        /// plan par défaut est « daara » (le plus petit) — le SuperAdmin peut le
        /// changer ensuite. Prix + quota snapshotés depuis le plan. Idempotent :
        /// no-op si l'abo existe déjà ; tolère un conflit d'unicité concurrent.
        ///
        /// <para>📅 <b>L'essai se termine sur le jour de prélèvement commun</b>
        /// (le 8), et jamais avant 30 jours pleins : c'est la règle intangible
        /// posée par Cheikh le 2026-09-19. Une école validée le 18/09 est donc en
        /// essai jusqu'au 08/11 — 51 jours. La promesse des 30 jours est
        /// dépassée, jamais trahie, et il n'y a aucune facture au prorata à
        /// expliquer à un directeur.</para>
        /// </summary>
        public static async Task EnsureSubscriptionAsync(
            this AppDbContext ctx, int schoolId, CancellationToken ct = default)
        {
            var exists = await ctx.Subscriptions.AnyAsync(s => s.SchoolId == schoolId, ct);
            if (exists) return;

            var plan = await ctx.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.Code == DefaultPlanCode && !p.IsCustom, ct);

            var settings = await ctx.GetPlatformSettingsAsync(ct);
            var now = DateTime.UtcNow;
            var trialEnd = SubscriptionSchedule.TrialEnd(now, settings.SubscriptionBillingDay);

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
