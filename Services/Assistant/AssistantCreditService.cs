using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.Enums;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services.Assistant
{
    /// <summary>Où en est une école avec l'assistant.</summary>
    /// <param name="Available">L'assistant peut-il répondre à cette école maintenant.</param>
    /// <param name="BlockedReason">Motif court et stable quand il ne le peut pas.</param>
    /// <param name="RemainingCommands">Crédits restants (offerts + achetés − consommés).</param>
    /// <param name="PricePerCommandFcfa">Prix EFFECTIF d'une commande achetée (40 F en Pro payé).</param>
    /// <param name="PlanName">Plan dont l'inclus s'applique (null = aucun inclus en vigueur).</param>
    /// <param name="IncludedUnlimited">Assistant illimité ce cycle (Grand payé).</param>
    /// <param name="IncludedQuota">Commandes incluses ce cycle (Pro payé), 0 sinon.</param>
    /// <param name="IncludedUsed">Commandes incluses déjà consommées ce cycle.</param>
    /// <param name="IncludedResetsAt">Fin du cycle payé — l'inclus repart à zéro.</param>
    public record AssistantStatus(
        bool Available,
        string? BlockedReason,
        int RemainingCommands,
        int FreeCommands,
        long PricePerCommandFcfa,
        bool PurchaseEnabled,
        int MaxCommandsPerPurchase,
        string? PlanName = null,
        bool IncludedUnlimited = false,
        int IncludedQuota = 0,
        int IncludedUsed = 0,
        DateTime? IncludedResetsAt = null)
    {
        /// <summary>L'inclus du plan peut-il couvrir la prochaine commande.</summary>
        public bool IncludedAvailable => IncludedUnlimited || IncludedUsed < IncludedQuota;
    }

    /// <summary>Ce que le plan PAYÉ donne pour le cycle en cours.</summary>
    public record AssistantEntitlement(
        string PlanName, bool Unlimited, int IncludedQuota, long? CreditPriceFcfa,
        DateTime CycleStart, DateTime CycleEnd);

    public interface IAssistantCreditService
    {
        /// <summary>
        /// Décide si l'école (et cet utilisateur) peut lancer une commande.
        /// Lit le registre — aucun compteur stocké (§112/§191).
        /// </summary>
        Task<AssistantStatus> DescribeAsync(int schoolId, int? userId, bool apiConfigured, CancellationToken ct);
    }

    public class AssistantCreditService : IAssistantCreditService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<AssistantCreditService> _logger;

        public AssistantCreditService(AppDbContext db, ILogger<AssistantCreditService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// 🔴 <b>L'inclus suit le plan PAYÉ, pas le plan CHOISI.</b> Changer de plan
        /// prend effet tout de suite mais ne se paie qu'au prochain prélèvement :
        /// sans cette règle, une école passerait en Grand, consommerait
        /// l'illimité tout le mois et redescendrait avant d'avoir payé.
        ///
        /// <para>D'où trois conditions, toutes nécessaires :</para>
        /// <list type="number">
        /// <item>l'abonnement est <b>actif</b> — l'essai n'a droit qu'aux commandes
        ///   offertes, l'impayé à rien de plus ;</item>
        /// <item>une facture <b>payée</b> couvre aujourd'hui ;</item>
        /// <item>sa part abonnement (hors SMS refacturés) <b>atteint le prix du
        ///   plan actuel</b> — c'est ce qui prouve que CE plan a été payé, et pas
        ///   le plan inférieur d'avant le changement.</item>
        /// </list>
        /// </summary>
        public async Task<AssistantEntitlement?> EntitlementAsync(int schoolId, DateTime nowUtc, CancellationToken ct)
        {
            var sub = await _db.Subscriptions.AsNoTracking()
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);
            if (sub?.Plan == null || sub.Status != SubscriptionStatus.Active) return null;
            if (!sub.Plan.AssistantUnlimited && sub.Plan.AssistantIncludedCommands <= 0) return null;

            var paid = await _db.SubscriptionInvoices.AsNoTracking()
                .Where(i => i.SchoolId == schoolId
                            && i.Status == SubscriptionInvoiceStatus.Paid
                            && i.PeriodStart <= nowUtc && i.PeriodEnd > nowUtc)
                .OrderByDescending(i => i.PeriodStart)
                .Select(i => new { i.PeriodStart, i.PeriodEnd, Base = i.AmountFcfa - i.SmsRefactureFcfa })
                .FirstOrDefaultAsync(ct);
            if (paid == null || paid.Base < sub.AmountFcfa) return null;

            return new AssistantEntitlement(
                sub.Plan.Name, sub.Plan.AssistantUnlimited, sub.Plan.AssistantIncludedCommands,
                sub.Plan.AssistantCreditPriceFcfa, paid.PeriodStart, paid.PeriodEnd);
        }

        public async Task<AssistantStatus> DescribeAsync(
            int schoolId, int? userId, bool apiConfigured, CancellationToken ct)
        {
            var p = await _db.GetPlatformSettingsAsync(ct);
            var now = DateTime.UtcNow;

            var granted = await _db.AssistantCreditGrants
                .Where(g => g.SchoolId == schoolId)
                .SumAsync(g => (int?)g.Commands, ct) ?? 0;
            var used = await _db.AssistantTurns
                .Where(t => t.SchoolId == schoolId)
                .SumAsync(t => (int?)t.ChargedCommands, ct) ?? 0;
            var remaining = Math.Max(0, p.AssistantFreeCommands + granted - used);

            var ent = await EntitlementAsync(schoolId, now, ct);
            var includedUsed = ent == null ? 0 : await _db.AssistantTurns
                .Where(t => t.SchoolId == schoolId && t.CreatedAt >= ent.CycleStart && t.CreatedAt < ent.CycleEnd)
                .SumAsync(t => (int?)t.IncludedCommands, ct) ?? 0;

            AssistantStatus Make(bool ok, string? reason) => new(
                ok, reason, remaining, p.AssistantFreeCommands,
                ent?.CreditPriceFcfa ?? p.AssistantPricePerCommandFcfa,
                p.AssistantPurchaseEnabled && p.AssistantEnabled && apiConfigured,
                p.AssistantMaxCommandsPerPurchase,
                ent?.PlanName,
                ent?.Unlimited ?? false,
                ent == null || ent.Unlimited ? 0 : ent.IncludedQuota,
                includedUsed,
                ent?.CycleEnd);

            if (!p.AssistantEnabled || !apiConfigured) return Make(false, "disabled");

            // Une école dont le dossier n'est pas validé n'a rien à gérer : même
            // porte que la lecture de cahier.
            var kyc = await _db.Schools.Where(s => s.Id == schoolId)
                .Select(s => (KycStatus?)s.KycStatus).FirstOrDefaultAsync(ct);
            if (kyc != KycStatus.Validated) return Make(false, "kyc_not_validated");

            var probe = Make(true, null);
            if (!probe.IncludedAvailable && remaining <= 0) return Make(false, "no_credit");

            // Frein par utilisateur : un script qui tourne ne doit pas vider le
            // solde d'une école en une nuit.
            if (userId is int uid)
            {
                var hourAgo = now.AddHours(-1);
                var lastHour = await _db.AssistantTurns
                    .CountAsync(t => t.UserId == uid && t.CreatedAt >= hourAgo, ct);
                if (lastHour >= p.AssistantMaxCommandsPerUserPerHour) return Make(false, "rate_limited");
            }

            // « Usage raisonnable » : vaut pour TOUS les plans, et c'est ce qui
            // borne le pire cas de l'illimité Grand.
            var today = now.Date;
            var schoolToday = await _db.AssistantTurns
                .CountAsync(t => t.SchoolId == schoolId && t.CreatedAt >= today, ct);
            if (schoolToday >= p.AssistantDailySchoolCap) return Make(false, "daily_cap");

            // Plafond de la PLATEFORME, toutes écoles confondues : c'est lui qui
            // protège d'une boucle. Évalué avant chaque appel payant.
            var spent = await _db.AssistantTurns
                .Where(t => t.CreatedAt >= today)
                .SumAsync(t => (long?)t.CostCentimes, ct) ?? 0;
            if (spent >= p.AssistantDailyPlatformCapFcfa * 100L)
            {
                _logger.LogWarning(
                    "[assistant] Plafond journalier atteint : {Spent} centimes ≥ {Cap} FCFA (école {SchoolId})",
                    spent, p.AssistantDailyPlatformCapFcfa, schoolId);
                return Make(false, "platform_cap");
            }

            return probe;
        }
    }
}
