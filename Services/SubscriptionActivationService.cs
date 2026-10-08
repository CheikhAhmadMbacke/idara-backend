using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>Ce que coûterait le passage immédiat à un plan, et ce qu'il couvrirait.</summary>
    public record ActivationQuote(
        int PlanId, string PlanName, long PriceFcfa,
        DateTime CoversFrom, DateTime CoversUntil, int DaysCovered,
        long WalletBalanceFcfa, bool CanPayFromWallet, string? BlockedReason, string? Message);

    public record ActivationResult(
        bool Ok, string? Error, string Status, int? PaymentId, string? RedirectUrl,
        SubscriptionInvoice? Invoice);

    public interface ISubscriptionActivationService
    {
        Task<ActivationQuote> QuoteAsync(int schoolId, int planId, CancellationToken ct);
        Task<ActivationResult> ActivateAsync(int schoolId, int planId, string? payerName, CancellationToken ct);
    }

    /// <summary>
    /// 💳 <b>Payer un plan MAINTENANT</b> — finir son essai plus tôt, ou monter
    /// de plan en cours de mois (décision de Cheikh, 2026-10-07, §300).
    ///
    /// <para><b>La règle, et elle seule :</b> on paie le CYCLE EN COURS au prix
    /// plein — d'aujourd'hui au prochain 8 —, sans prorata et sans rien offrir.
    /// Le plan (et l'assistant inclus) s'ouvre tout de suite ; le prélèvement
    /// suivant reste le 8. L'écran annonce les jours couverts AVANT que l'école
    /// paie : à elle de juger s'il vaut mieux attendre le 8.</para>
    ///
    /// <para>🔴 <b>Rien ne change tant que ce n'est pas payé.</b> Aucune facture
    /// en attente n'est créée avant le paiement : un Wave abandonné ne doit
    /// jamais faire passer l'école pour impayée. La facture naît DÉJÀ PAYÉE, par
    /// <see cref="Apply"/>, la même règle pour le solde et pour Wave.</para>
    /// </summary>
    public class SubscriptionActivationService : ISubscriptionActivationService
    {
        private readonly AppDbContext _db;
        private readonly IWavePayinService _wave;
        private readonly ILogger<SubscriptionActivationService> _logger;

        public SubscriptionActivationService(
            AppDbContext db, IWavePayinService wave, ILogger<SubscriptionActivationService> logger)
        {
            _db = db;
            _wave = wave;
            _logger = logger;
        }

        /// <summary>Le prochain 8 STRICTEMENT après aujourd'hui : la fin du cycle en cours.</summary>
        public static DateTime CycleEnd(DateTime nowUtc, int billingDay) =>
            SubscriptionSchedule.FirstAnchorOnOrAfter(nowUtc.Date.AddDays(1), billingDay);

        public async Task<ActivationQuote> QuoteAsync(int schoolId, int planId, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var settings = await _db.GetPlatformSettingsAsync(ct);
            var end = CycleEnd(now, settings.SubscriptionBillingDay);
            var wallet = await _db.SchoolWallets.AsNoTracking()
                .Where(w => w.SchoolId == schoolId).Select(w => (long?)w.AvailableBalance).FirstOrDefaultAsync(ct) ?? 0;

            var (plan, blocked, msg) = await CheckAsync(schoolId, planId, ct);
            var price = plan?.MonthlyPriceFcfa ?? 0;
            return new ActivationQuote(
                planId, plan?.Name ?? "", price, now, end, (int)Math.Ceiling((end - now.Date).TotalDays),
                wallet, blocked == null && wallet >= price, blocked, msg);
        }

        private async Task<(SubscriptionPlan? Plan, string? Blocked, string? Message)> CheckAsync(
            int schoolId, int planId, CancellationToken ct)
        {
            await _db.EnsureSubscriptionAsync(schoolId, ct);
            var sub = await _db.Subscriptions.AsNoTracking().FirstAsync(s => s.SchoolId == schoolId, ct);
            var plan = await _db.SubscriptionPlans.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == planId && p.IsActive && !p.IsCustom, ct);
            if (plan == null) return (null, "plan_unavailable", "Ce plan n'est pas disponible.");

            if (sub.Status is not (SubscriptionStatus.Trial or SubscriptionStatus.Active))
                return (plan, "arrears",
                    "Votre abonnement a une facture en attente : réglez-la d'abord, depuis le lien de paiement de votre abonnement.");

            // Monter seulement. Descendre se fait par « Changer de plan », au
            // prochain prélèvement, sans remboursement.
            if (sub.Status == SubscriptionStatus.Active && plan.MonthlyPriceFcfa <= sub.AmountFcfa)
                return (plan, "not_an_upgrade",
                    "Votre plan actuel est déjà payé pour ce mois. Pour un plan moins cher, utilisez « Changer de plan » : il s'appliquera au prochain prélèvement.");

            var students = await _db.Students.Where(s => s.SchoolId == schoolId).Enrolled().CountAsync(ct);
            if (plan.StudentMax is int max && students > max)
                return (plan, "too_many_students",
                    $"Le plan « {plan.Name} » est limité à {max} élèves, or votre école en compte {students}.");

            return (plan, null, null);
        }

        public async Task<ActivationResult> ActivateAsync(
            int schoolId, int planId, string? payerName, CancellationToken ct)
        {
            var (plan, blocked, msg) = await CheckAsync(schoolId, planId, ct);
            if (blocked != null || plan == null) return new(false, msg, "refused", null, null, null);
            var price = plan.MonthlyPriceFcfa;
            var settings = await _db.GetPlatformSettingsAsync(ct);

            // 1) Le SOLDE suffit : un geste, sous verrou (§69).
            await using (var tx = await _db.Database.BeginTransactionAsync(ct))
            {
                var wallet = await _db.LockWalletAsync(schoolId, ct);
                if (wallet != null && wallet.AvailableBalance >= price)
                {
                    var now = DateTime.UtcNow;
                    var draw = wallet.DonationDrawFor(price, WithdrawalSource.Total);
                    wallet.AvailableBalance -= price;
                    wallet.DonationBalanceFcfa -= draw;
                    wallet.UpdatedAt = now;
                    var walletTx = new WalletTransaction
                    {
                        SchoolId = schoolId,
                        Type = WalletTransactionType.Debit,
                        Source = WalletSource.Subscription,
                        AmountFcfa = -price,
                        BalanceAfter = wallet.AvailableBalance,
                        RelatedEntity = WalletRelatedEntity.Subscription,
                        Note = $"Passage immédiat au plan {plan.Name}",
                        OccurredAt = now,
                    };
                    _db.WalletTransactions.Add(walletTx);
                    var sub = await _db.Subscriptions.FirstAsync(s => s.SchoolId == schoolId, ct);
                    walletTx.RelatedId = sub.Id;
                    await _db.SaveChangesAsync(ct);

                    var invoice = Apply(_db, sub, plan, price, now, settings.SubscriptionBillingDay);
                    invoice.WalletTransactionId = walletTx.Id;
                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);

                    _logger.LogInformation(
                        "[activate-now] École {SchoolId} : plan {Plan} payé {Price} FCFA par le solde, couvert jusqu'au {Until:yyyy-MM-dd}",
                        schoolId, plan.Name, price, sub.NextBillingAt);
                    return new(true, null, "Paid", null, null, invoice);
                }
                await tx.RollbackAsync(ct);
            }

            // 2) Sinon, WAVE. Le plan visé voyage sur le paiement ; rien ne
            // change avant le webhook. Même construction que le lien public
            // d'abonnement (FeesPayer = School, TargetAmount = 0 : la plateforme
            // absorbe la commission, §145 ; finance : PaymentPurposes.PaysPlatform).
            var payment = new Payment
            {
                SchoolId = schoolId,
                Purpose = PaymentPurpose.Subscription,
                SubscriptionPlanId = plan.Id,
                AmountFcfa = price,
                TargetAmountFcfa = 0,
                FeesFcfa = 0,
                NetCreditedFcfa = 0,
                Operator = PaymentOperator.Wave,
                FeesPayer = FeesPayer.School,
                Status = PaymentStatus.Pending,
                InitiatedAt = DateTime.UtcNow,
                PublicResultToken = Guid.NewGuid().ToString("N"),
            };
            _db.Payments.Add(payment);
            await _db.SaveChangesAsync(ct);

            var outcome = await _wave.StartAsync(payment, payerName, ct);
            if (!outcome.Ok || string.IsNullOrWhiteSpace(outcome.RedirectUrl))
            {
                _logger.LogWarning("[activate-now] Session Wave refusée Payment {Id} : {Msg}", payment.Id, outcome.ErrorMessage);
                return new(false, outcome.ErrorMessage ?? "Le paiement est temporairement indisponible.", "error", payment.Id, null, null);
            }
            return new(true, null, "Pending", payment.Id, outcome.RedirectUrl, null);
        }

        /// <summary>
        /// 🔑 La SEULE règle d'application, partagée par le solde et par le
        /// règlement Wave (<c>PayinSettlementService</c>). Crée la facture DÉJÀ
        /// PAYÉE du cycle en cours et bascule le plan. À appeler dans la
        /// transaction de l'appelant.
        /// </summary>
        public static SubscriptionInvoice Apply(
            AppDbContext db, Subscription sub, SubscriptionPlan plan, long amountPaid, DateTime nowUtc, int billingDay)
        {
            var end = CycleEnd(nowUtc, billingDay);

            sub.PlanId = plan.Id;
            sub.BillingCycle = BillingCycle.Monthly;
            sub.AmountFcfa = plan.MonthlyPriceFcfa;
            sub.NotificationQuota = plan.NotificationQuota;
            sub.NotificationUsedThisCycle = 0;
            sub.Status = SubscriptionStatus.Active;
            sub.ActivatedAt = nowUtc;
            // L'essai s'arrête AUJOURD'HUI : ses jours restants ne s'ajoutent pas
            // (décision de Cheikh — rien d'offert au-delà de l'essai).
            if (sub.TrialEndsAt > nowUtc) sub.TrialEndsAt = nowUtc;
            sub.NextBillingAt = end;
            sub.GracePeriodEndsAt = null;
            sub.ReadOnlyEndsAt = null;
            sub.SuspendedAt = null;
            sub.UpdatedAt = nowUtc;

            // PeriodStart = l'INSTANT du paiement, pas la date : deux passages le
            // même jour (essai → Pro → Grand) ne heurtent pas l'unicité
            // (SubscriptionId, PeriodStart). PeriodEnd = veille du 8, comme
            // toutes les factures d'abonnement.
            var invoice = new SubscriptionInvoice
            {
                SubscriptionId = sub.Id,
                SchoolId = sub.SchoolId,
                PeriodStart = nowUtc,
                PeriodEnd = end.AddDays(-1),
                AmountFcfa = amountPaid,
                Status = SubscriptionInvoiceStatus.Paid,
                IssuedAt = nowUtc,
                PaidAt = nowUtc,
                CreatedAt = nowUtc,
            };
            db.SubscriptionInvoices.Add(invoice);
            return invoice;
        }
    }
}
