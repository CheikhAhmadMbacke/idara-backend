using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.Enums;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services.Vision
{
    /// <inheritdoc cref="IOcrBudgetGuard"/>
    public class OcrBudgetGuard : IOcrBudgetGuard
    {
        private readonly AppDbContext _db;
        private readonly ILogger<OcrBudgetGuard> _logger;

        public OcrBudgetGuard(AppDbContext db, ILogger<OcrBudgetGuard> logger)
        {
            _db = db;
            _logger = logger;
        }

        public Task<OcrGuardDecision> DescribeAsync(int schoolId, CancellationToken ct = default)
            => EvaluateAsync(new OcrGuardContext(schoolId, 0), ct);

        public async Task<OcrGuardDecision> EvaluateAsync(OcrGuardContext ctx, CancellationToken ct = default)
        {
            try
            {
                return await EvaluateCoreAsync(ctx, ct);
            }
            catch (Exception ex)
            {
                // Échec FERMÉ, délibérément (cf. la doc de l'interface) : ne pas
                // savoir et laisser passer, c'est dépenser à l'aveugle.
                _logger.LogError(ex,
                    "[ocr-guard] Évaluation impossible pour l'école {SchoolId} : refus par défaut", ctx.SchoolId);
                return new OcrGuardDecision(false, "guard_error",
                    "La lecture est momentanément indisponible. Réessayez dans un instant.", 0, 0);
            }
        }

        private async Task<OcrGuardDecision> EvaluateCoreAsync(OcrGuardContext ctx, CancellationToken ct)
        {
            var p = await _db.GetPlatformSettingsAsync(ct);

            // --- La porte : une école dont le dossier a été validé par un
            // humain. C'est ce qui empêche une inscription bidon de dépenser un
            // franc, et ça ne coûte rien : le contrôle existe déjà.
            var kyc = await _db.Schools
                .Where(s => s.Id == ctx.SchoolId)
                .Select(s => (KycStatus?)s.KycStatus)
                .FirstOrDefaultAsync(ct);
            if (kyc == null)
                return Deny("school_unknown", "École introuvable.", 0, 0);
            if (kyc != KycStatus.Validated)
                return Deny("kyc_not_validated",
                    "La lecture d'un cahier sera disponible dès que votre dossier sera validé.", 0, 0);

            // --- Quota de l'école : base + accordées − consommées. DÉRIVÉ.
            var granted = await _db.OcrPageGrants
                .Where(g => g.SchoolId == ctx.SchoolId)
                .SumAsync(g => (int?)g.Pages, ct) ?? 0;

            var used = await _db.OcrJobs
                .Where(j => j.SchoolId == ctx.SchoolId)
                .SumAsync(j => (int?)j.ChargedPages, ct) ?? 0;

            var allowance = p.OcrBaseAllowancePages + granted;
            var remaining = Math.Max(0, allowance - used);

            if (!p.OcrEnabled)
                return Deny("disabled",
                    "La lecture d'un cahier est momentanément indisponible.", remaining, allowance);

            // DescribeAsync : on ne juge rien, on rend l'état.
            if (ctx.Pages <= 0)
                return new OcrGuardDecision(remaining > 0, null, null, remaining, allowance);

            if (ctx.Pages > p.OcrMaxPagesPerRequest)
                return Deny("too_many_pages",
                    $"Envoyez au maximum {p.OcrMaxPagesPerRequest} photos à la fois.",
                    remaining, allowance);

            if (remaining < ctx.Pages)
                return Deny("school_quota",
                    remaining == 0
                        ? $"Vous avez utilisé vos {allowance} pages de lecture."
                        : $"Il ne vous reste que {remaining} page(s) de lecture sur {allowance}.",
                    remaining, allowance);

            // --- Échecs consécutifs : un appel qui échoue ne consomme pas le
            // quota (l'école n'a rien reçu), donc rien ne l'arrêterait sans ça.
            var recent = await _db.OcrJobs
                .Where(j => j.SchoolId == ctx.SchoolId && j.BlockedReason == null)
                .OrderByDescending(j => j.Id)
                .Take(p.OcrMaxConsecutiveFailures)
                .Select(j => j.Success)
                .ToListAsync(ct);
            if (recent.Count >= p.OcrMaxConsecutiveFailures && recent.All(s => !s))
                return Deny("consecutive_failures",
                    "Plusieurs lectures ont échoué de suite. Contactez-nous — nous regardons ce qui bloque.",
                    remaining, allowance);

            // --- Le plafond PLATEFORME, en francs et par jour. C'est lui qui
            // protège d'un bug ou d'une boucle ; le quota par école ne le fait
            // pas (dix écoles à 30 pages, c'est déjà 15 000 F).
            var since = DateTime.UtcNow.Date;
            var spentCentimes = await _db.OcrJobs
                .Where(j => j.CreatedAt >= since)
                .SumAsync(j => (long?)j.CostCentimes, ct) ?? 0L;
            if (spentCentimes >= p.OcrDailyPlatformCapFcfa * 100L)
            {
                _logger.LogWarning(
                    "[ocr-guard] Plafond quotidien plateforme atteint : {Spent} centimes ≥ {Cap} FCFA "
                    + "(école {SchoolId} refusée)",
                    spentCentimes, p.OcrDailyPlatformCapFcfa, ctx.SchoolId);
                return Deny("platform_daily_cap",
                    "La lecture de cahiers est saturée pour aujourd'hui. Réessayez demain, "
                    + "ou utilisez le fichier Excel en attendant.",
                    remaining, allowance);
            }

            return new OcrGuardDecision(true, null, null, remaining, allowance);
        }

        private static OcrGuardDecision Deny(string reason, string message, int remaining, int allowance)
            => new(false, reason, message, remaining, allowance);
    }
}
