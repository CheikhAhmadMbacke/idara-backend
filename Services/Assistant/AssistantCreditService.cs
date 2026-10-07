using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.Enums;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services.Assistant
{
    /// <summary>Où en est une école avec l'assistant.</summary>
    /// <param name="Available">L'assistant peut-il répondre à cette école maintenant.</param>
    /// <param name="BlockedReason">Motif court et stable quand il ne le peut pas.</param>
    /// <param name="RemainingCommands">Commandes restantes (offertes + achetées − consommées).</param>
    public record AssistantStatus(
        bool Available,
        string? BlockedReason,
        int RemainingCommands,
        int FreeCommands,
        long PricePerCommandFcfa,
        bool PurchaseEnabled,
        int MaxCommandsPerPurchase);

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

        public async Task<AssistantStatus> DescribeAsync(
            int schoolId, int? userId, bool apiConfigured, CancellationToken ct)
        {
            var p = await _db.GetPlatformSettingsAsync(ct);

            var granted = await _db.AssistantCreditGrants
                .Where(g => g.SchoolId == schoolId)
                .SumAsync(g => (int?)g.Commands, ct) ?? 0;
            var used = await _db.AssistantTurns
                .Where(t => t.SchoolId == schoolId)
                .SumAsync(t => (int?)t.ChargedCommands, ct) ?? 0;
            var remaining = Math.Max(0, p.AssistantFreeCommands + granted - used);

            AssistantStatus Make(bool ok, string? reason) => new(
                ok, reason, remaining, p.AssistantFreeCommands, p.AssistantPricePerCommandFcfa,
                p.AssistantPurchaseEnabled && p.AssistantEnabled && apiConfigured,
                p.AssistantMaxCommandsPerPurchase);

            if (!p.AssistantEnabled || !apiConfigured) return Make(false, "disabled");

            // Une école dont le dossier n'est pas validé n'a rien à gérer : même
            // porte que la lecture de cahier.
            var kyc = await _db.Schools.Where(s => s.Id == schoolId)
                .Select(s => (KycStatus?)s.KycStatus).FirstOrDefaultAsync(ct);
            if (kyc != KycStatus.Validated) return Make(false, "kyc_not_validated");

            if (remaining <= 0) return Make(false, "no_credit");

            // Frein par utilisateur : un script qui tourne ne doit pas vider le
            // solde d'une école en une nuit.
            if (userId is int uid)
            {
                var hourAgo = DateTime.UtcNow.AddHours(-1);
                var lastHour = await _db.AssistantTurns
                    .CountAsync(t => t.UserId == uid && t.CreatedAt >= hourAgo, ct);
                if (lastHour >= p.AssistantMaxCommandsPerUserPerHour) return Make(false, "rate_limited");
            }

            // Plafond de la PLATEFORME, toutes écoles confondues : c'est lui qui
            // protège d'une boucle. Évalué avant chaque appel payant.
            var today = DateTime.UtcNow.Date;
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

            return Make(true, null);
        }
    }
}
