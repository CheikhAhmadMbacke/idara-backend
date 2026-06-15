using Idara.API.Common.Extensions;
using Idara.API.Constants;
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
        private readonly ISubscriptionInvoicePdfService _pdf;
        private readonly IEmailService _email;
        private readonly Notifications.INotificationService _notif;
        private readonly ILogger<SubscriptionBillingService> _logger;

        public SubscriptionBillingService(
            AppDbContext db,
            ISubscriptionInvoicePdfService pdf,
            IEmailService email,
            Notifications.INotificationService notif,
            ILogger<SubscriptionBillingService> logger)
        {
            _db = db;
            _pdf = pdf;
            _email = email;
            _notif = notif;
            _logger = logger;
        }

        public async Task<SubscriptionBillingReport> RunOnceAsync(DateTime nowUtc, CancellationToken ct)
        {
            var report = new SubscriptionBillingReport { RunAtUtc = nowUtc };

            // Échus (Trial/Active dont NextBillingAt passé) OU déjà en impayé
            // (PendingPayment/ReadOnly/Suspended à re-tenter). Suspended est INCLUS
            // pour qu'un rechargement de wallet ramène l'école à Active (sinon une
            // école suspendue resterait piégée à vie malgré un paiement).
            var ids = await _db.Subscriptions
                .Where(s =>
                    ((s.Status == SubscriptionStatus.Trial || s.Status == SubscriptionStatus.Active)
                        && s.NextBillingAt <= nowUtc)
                    || s.Status == SubscriptionStatus.PendingPayment
                    || s.Status == SubscriptionStatus.ReadOnly
                    || s.Status == SubscriptionStatus.Suspended)
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
                    && (s.Status == SubscriptionStatus.PendingPayment
                        || s.Status == SubscriptionStatus.ReadOnly
                        || s.Status == SubscriptionStatus.Suspended), ct);
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
            var inArrears = sub.Status == SubscriptionStatus.PendingPayment
                || sub.Status == SubscriptionStatus.ReadOnly
                || sub.Status == SubscriptionStatus.Suspended;

            if (!dueNow && !inArrears)
            {
                await tx.RollbackAsync(ct);
                return BillingOutcome.NoAction; // déjà à jour / pas encore échu
            }

            // ----- Re-snapshot du prix/quota au RENOUVELLEMENT -----
            // Une modif de plan public par le SuperAdmin doit s'appliquer « au
            // prochain prélèvement » (décision produit + spec §6.1). On relit donc
            // le prix/quota du plan public courant à l'échéance et on les re-snapshote
            // dans l'abo AVANT de prélever. Jamais sur une période déjà payée.
            // Exclusions : deals custom (prix négocié, intouchable) et plan
            // désactivé (on garde le dernier prix snapshoté). Le garde-fou palier
            // ci-dessous peut ensuite remonter à un plan supérieur si l'effectif
            // dépasse le plafond.
            // Inclut les écoles EN IMPAYÉ (inArrears) : leur prochain prélèvement
            // EST ce rattrapage, donc une hausse de prix décidée entre-temps doit
            // s'y appliquer aussi (sinon la hausse ne les atteint jamais).
            if ((dueNow || inArrears) && sub.PlanId.HasValue)
            {
                var planForSnapshot = await _db.SubscriptionPlans
                    .FirstOrDefaultAsync(p => p.Id == sub.PlanId.Value, ct);
                if (planForSnapshot != null && !planForSnapshot.IsCustom && planForSnapshot.IsActive)
                {
                    var planAmount = sub.BillingCycle == BillingCycle.Annual
                        ? planForSnapshot.AnnualPriceFcfa
                        : planForSnapshot.MonthlyPriceFcfa;
                    if (planAmount != sub.AmountFcfa || planForSnapshot.NotificationQuota != sub.NotificationQuota)
                    {
                        _logger.LogInformation(
                            "[subscription-billing] École {SchoolId} re-snapshot prix {Old}→{New} / quota {OldQ}→{NewQ} (plan {Plan}).",
                            sub.SchoolId, sub.AmountFcfa, planAmount, sub.NotificationQuota,
                            planForSnapshot.NotificationQuota, planForSnapshot.Name);
                        sub.AmountFcfa = planAmount;
                        sub.NotificationQuota = planForSnapshot.NotificationQuota;
                        sub.UpdatedAt = nowUtc;
                    }
                }
            }

            // ----- Garde-fou palier : auto-ajustement AVANT le prélèvement -----
            // Si l'effectif réel de l'école dépasse le plafond de son plan, on la
            // remonte au plus petit plan public qui le couvre et on re-snapshote
            // le montant — sinon on facturerait en-dessous de l'effectif (perte).
            // On ne descend JAMAIS (l'école garde le choix de réduire via
            // change-plan) et on NE touche PAS aux deals custom (prix négocié).
            SubscriptionPlan? autoUpgradedTo = null;
            var studentCountForUpgrade = 0;
            {
                var currentPlan = sub.PlanId.HasValue
                    ? await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == sub.PlanId.Value, ct)
                    : null;
                if (currentPlan != null && !currentPlan.IsCustom && currentPlan.StudentMax.HasValue)
                {
                    var studentCount = await _db.Students
                        .CountAsync(s => s.SchoolId == sub.SchoolId && !s.IsDeleted, ct);
                    if (studentCount > currentPlan.StudentMax.Value)
                    {
                        var publicPlans = await _db.SubscriptionPlans
                            .Where(p => p.IsActive && !p.IsCustom)
                            .OrderBy(p => p.MonthlyPriceFcfa)
                            .ToListAsync(ct);
                        var correct = publicPlans.FirstOrDefault(
                            p => !p.StudentMax.HasValue || studentCount <= p.StudentMax.Value);
                        var correctAmount = correct == null
                            ? 0L
                            : (sub.BillingCycle == BillingCycle.Annual
                                ? correct.AnnualPriceFcfa
                                : correct.MonthlyPriceFcfa);
                        // Uniquement un vrai upgrade (prix supérieur au snapshot courant).
                        if (correct != null && correct.Id != sub.PlanId && correctAmount > sub.AmountFcfa)
                        {
                            sub.PlanId = correct.Id;
                            sub.AmountFcfa = correctAmount;
                            sub.NotificationQuota = correct.NotificationQuota;
                            sub.UpdatedAt = nowUtc;
                            autoUpgradedTo = correct;
                            studentCountForUpgrade = studentCount;
                            _logger.LogInformation(
                                "[subscription-billing] École {SchoolId} auto-ajustée {Old}→{New} (effectif {Count} > plafond {Max}) → montant {Amount}.",
                                sub.SchoolId, currentPlan.Name, correct.Name, studentCount,
                                currentPlan.StudentMax, correctAmount);
                        }
                    }
                }
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
                sub.SuspendedAt = null; // réactivation : on efface la trace de suspension
                sub.UpdatedAt = nowUtc;

                await _db.SaveChangesAsync(ct);
                invoice.WalletTransactionId = walletTx.Id;
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "[subscription-billing] École {SchoolId} prélevée {Amount} FCFA → Active (next {Next:yyyy-MM-dd})",
                    sub.SchoolId, amount, sub.NextBillingAt);

                // Facture PDF + email SchoolAdmin — best-effort, HORS transaction
                // (un échec PDF/SMTP ne doit jamais annuler un prélèvement encaissé).
                await EmitInvoiceDocsAsync(invoice, sub, ct);
                if (autoUpgradedTo != null)
                    await NotifyAutoUpgradeAsync(sub, autoUpgradedTo, studentCountForUpgrade, ct);
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
            if (autoUpgradedTo != null)
                await NotifyAutoUpgradeAsync(sub, autoUpgradedTo, studentCountForUpgrade, ct);
            return outcome;
        }

        /// <summary>
        /// Notifie (push, best-effort) le SchoolAdmin + personnel que l'abonnement
        /// a été auto-ajusté à un palier supérieur (effectif dépassant l'ancien
        /// plan). Ne lève jamais : un échec notif ne doit pas casser la facturation.
        /// </summary>
        private async Task NotifyAutoUpgradeAsync(
            Subscription sub, SubscriptionPlan newPlan, int studentCount, CancellationToken ct)
        {
            try
            {
                var admins = await _db.Users
                    .Where(u => u.SchoolId == sub.SchoolId && !u.IsDeleted
                        && (u.Role == UserRoles.SchoolAdmin || u.Role == UserRoles.SchoolStaff))
                    .Select(u => new { u.Id, u.PreferredLanguage })
                    .ToListAsync(ct);
                var msg = Notifications.NotificationTemplates.SubscriptionPlanUpgraded(
                    newPlan.Name, studentCount, sub.AmountFcfa);
                foreach (var a in admins)
                {
                    await _notif.SendPushOnlyAsync(new Notifications.PushOnlyRequest(
                        UserId: a.Id,
                        PreferredLanguage: a.PreferredLanguage ?? "fr",
                        Message: msg,
                        TemplateCode: "SUBSCRIPTION_AUTO_UPGRADE",
                        RelatedEntityId: sub.Id,
                        PushRoute: "/school/subscription"), ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[subscription-billing] Notif auto-upgrade échouée École {SchoolId} (non bloquant)",
                    sub.SchoolId);
            }
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

        /// <summary>
        /// Génère le PDF de facture + envoie l'email au SchoolAdmin. Best-effort
        /// absolu : chaque étape est isolée dans son try/catch et la méthode ne
        /// lève JAMAIS (un échec disque/SMTP ne doit pas impacter le prélèvement).
        /// </summary>
        private async Task EmitInvoiceDocsAsync(SubscriptionInvoice invoice, Subscription sub, CancellationToken ct)
        {
            try
            {
                var school = await _db.Schools.FirstOrDefaultAsync(s => s.Id == sub.SchoolId, ct);
                if (school == null) return;

                string? planName = sub.PlanId.HasValue
                    ? await _db.SubscriptionPlans.Where(p => p.Id == sub.PlanId.Value)
                        .Select(p => p.Name).FirstOrDefaultAsync(ct)
                    : null;

                // PDF
                try
                {
                    var path = await _pdf.GenerateAsync(invoice, school, planName);
                    await _db.SubscriptionInvoices.Where(i => i.Id == invoice.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.PdfPath, path), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[subscription-billing] PDF facture {Id} échoué — pas bloquant", invoice.Id);
                }

                // Email SchoolAdmin (le SEUL email côté école)
                try
                {
                    var adminEmail = await _db.Users
                        .Where(u => u.SchoolId == sub.SchoolId && !u.IsDeleted
                                    && u.Role == UserRoles.SchoolAdmin && u.Email != null)
                        .OrderBy(u => u.Id)
                        .Select(u => u.Email)
                        .FirstOrDefaultAsync(ct);
                    if (!string.IsNullOrWhiteSpace(adminEmail))
                    {
                        await _email.SendSubscriptionInvoiceEmailAsync(
                            adminEmail!, school.Name ?? "votre école",
                            invoice.AmountFcfa, invoice.PeriodStart, invoice.PeriodEnd);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[subscription-billing] Email facture {Id} échoué — pas bloquant", invoice.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[subscription-billing] Docs facture {Id} — échec global, ignoré", invoice.Id);
            }
        }

        private static DateTime AdvanceCycle(DateTime from, BillingCycle cycle) =>
            cycle == BillingCycle.Annual ? from.AddYears(1) : from.AddMonths(1);
    }
}
