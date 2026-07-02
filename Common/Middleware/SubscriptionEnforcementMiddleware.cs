using System.Text.Json;
using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Idara.API.Common.Middleware
{
    /// <summary>
    /// Applique la machine à états d'abonnement (Phase 4) : dès qu'un abonnement
    /// est impayé (PendingPayment / ReadOnly / Suspended), l'école passe en
    /// LECTURE SEULE — les écritures (POST/PUT/PATCH/DELETE) renvoient 402, mais
    /// les lectures (GET) restent TOUJOURS autorisées : l'école doit pouvoir se
    /// connecter et naviguer partout. NE FAIT RIEN tant que le SuperAdmin n'a pas
    /// activé <c>PlatformSettings.SubscriptionEnforcementEnabled</c> (OFF par
    /// défaut) → on peut déployer et observer la facturation avant de verrouiller.
    ///
    /// Exemptés : SuperAdmin, Guardian (les parents doivent pouvoir payer), et
    /// une whitelist de chemins (login, recharge wallet, webhooks, gestion de
    /// son propre abonnement, page de paiement publique). Dès le 1er échec de
    /// prélèvement (PendingPayment), l'école passe en LECTURE SEULE : ses seules
    /// actions possibles restent se connecter + recharger le wallet / payer
    /// l'abonnement. La fenêtre de grâce ne fait plus que retarder l'escalade
    /// vers ReadOnly puis Suspended (blocage total), pas le blocage des écritures.
    /// </summary>
    public class SubscriptionEnforcementMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SubscriptionEnforcementMiddleware> _logger;

        // Chemins toujours autorisés, même en ReadOnly/Suspended.
        private static readonly string[] WhitelistPrefixes =
        {
            "/api/auth",
            "/api/webhooks",
            "/api/school/wallet/topup",
            "/api/subscriptions/me",
            "/api/push",
            "/pay",
            "/uploads",
            "/swagger"
        };

        public SubscriptionEnforcementMiddleware(
            RequestDelegate next, IMemoryCache cache, ILogger<SubscriptionEnforcementMiddleware> logger)
        {
            _next = next;
            _cache = cache;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext ctx, AppDbContext db)
        {
            // Non authentifié → laisser passer (l'auth gère le 401).
            if (ctx.User?.Identity?.IsAuthenticated != true) { await _next(ctx); return; }

            var role = ctx.User.GetRole();
            // SuperAdmin gère la plateforme ; Guardian (parent) doit pouvoir payer ;
            // Donor (donateur global, sans école) n'est lié à aucun abonnement.
            // Un rôle vide n'est PAS exempté (fail-closed) : s'il porte un SchoolId
            // il sera évalué, sinon le check SchoolId==null l'exempte juste après.
            if (role == UserRoles.SuperAdmin || role == UserRoles.Guardian || role == UserRoles.Donor)
            {
                await _next(ctx); return;
            }

            var schoolId = ctx.User.GetSchoolId();
            if (schoolId == null) { await _next(ctx); return; }

            var path = ctx.Request.Path;
            if (IsWhitelisted(path)) { await _next(ctx); return; }

            // Flag global mis en cache 30s (change rarement) → on évite une requête
            // DB sur le chemin chaud à chaque requête, surtout quand le flag est OFF
            // (cas par défaut). GetOrCreateAsync recharge à l'expiration.
            var enforcementOn = await _cache.GetOrCreateAsync("sub_enforce_flag", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                var p = await db.GetPlatformSettingsAsync(ctx.RequestAborted);
                return p.SubscriptionEnforcementEnabled;
            });
            if (!enforcementOn) { await _next(ctx); return; }

            var sub = await db.Subscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId.Value, ctx.RequestAborted);
            if (sub == null) { await _next(ctx); return; }

            var isWrite = HttpMethods.IsPost(ctx.Request.Method)
                || HttpMethods.IsPut(ctx.Request.Method)
                || HttpMethods.IsPatch(ctx.Request.Method)
                || HttpMethods.IsDelete(ctx.Request.Method);

            // Décision produit : un abonnement impayé met l'école en LECTURE
            // SEULE — JAMAIS de blocage des lectures. Elle doit toujours pouvoir
            // se connecter et naviguer entre tous les écrans ; seules les
            // ÉCRITURES sont bloquées (hors recharge wallet, déjà en whitelist).
            // Les 3 états impayés bloquent donc uniquement les écritures, dès le
            // 1er échec de prélèvement (plus de fenêtre de grâce « accès complet »).
            // Suspended ne diffère de ReadOnly que par l'ancienneté de l'impayé
            // (escalade temporelle), pas par le périmètre du blocage.
            var blocked = sub.Status switch
            {
                SubscriptionStatus.PendingPayment => isWrite,
                SubscriptionStatus.ReadOnly => isWrite,
                SubscriptionStatus.Suspended => isWrite,
                _ => false // Trial / Active : accès complet.
            };

            if (!blocked) { await _next(ctx); return; }

            _logger.LogInformation(
                "[subscription-enforce] 402 École {SchoolId} (statut {Status}) sur {Method} {Path}",
                schoolId.Value, sub.Status, ctx.Request.Method, path);

            ctx.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            ctx.Response.ContentType = "application/json";
            var payload = new
            {
                success = false,
                message = sub.Status == SubscriptionStatus.Suspended
                    ? "Abonnement suspendu. Rechargez votre wallet pour réactiver l'accès."
                    : "Abonnement impayé : accès en lecture seule. Rechargez votre wallet pour débloquer.",
                data = new
                {
                    subscriptionStatus = (int)sub.Status,
                    subscriptionStatusName = sub.Status.ToString(),
                    nextBillingAt = sub.NextBillingAt,
                    gracePeriodEndsAt = sub.GracePeriodEndsAt,
                    readOnlyEndsAt = sub.ReadOnlyEndsAt,
                    amountDueFcfa = sub.AmountFcfa
                }
            };
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(payload), ctx.RequestAborted);
        }

        // Écritures d'onboarding NON exemptées malgré le préfixe /api/auth : une
        // école ReadOnly/Suspended ne doit PLUS pouvoir inviter du personnel,
        // re-soumettre un KYC, ni régénérer des codes d'accès (sinon le blocage
        // d'abonnement perd son levier — cf. review §E2).
        private static readonly string[] AuthWriteBlocked =
        {
            "/api/auth/invite-user",
            "/api/auth/submit-kyc"
        };

        private static bool IsWhitelisted(PathString path)
        {
            // Ces écritures d'auth restent soumises au blocage (priment sur la whitelist).
            foreach (var blocked in AuthWriteBlocked)
            {
                if (path.StartsWithSegments(blocked, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            if (path.Value != null
                && path.Value.Contains("regenerate-code", StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (var prefix in WhitelistPrefixes)
            {
                if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
