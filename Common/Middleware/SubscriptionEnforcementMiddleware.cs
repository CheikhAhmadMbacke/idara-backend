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
    /// Applique la machine à états d'abonnement (Phase 4) : bloque les écoles
    /// dont l'abonnement est <see cref="SubscriptionStatus.ReadOnly"/> (écritures
    /// interdites) ou <see cref="SubscriptionStatus.Suspended"/> (tout interdit),
    /// avec un 402 Payment Required. NE FAIT RIEN tant que le SuperAdmin n'a pas
    /// activé <c>PlatformSettings.SubscriptionEnforcementEnabled</c> (OFF par
    /// défaut) → on peut déployer et observer la facturation avant de verrouiller.
    ///
    /// Exemptés : SuperAdmin, Guardian (les parents doivent pouvoir payer), et
    /// une whitelist de chemins (login, recharge wallet, webhooks, gestion de
    /// son propre abonnement, page de paiement publique). En grâce
    /// (PendingPayment) l'école n'est PAS bloquée — seulement avertie.
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
            // SuperAdmin gère la plateforme ; Guardian (parent) doit pouvoir payer.
            // Un rôle vide n'est PAS exempté (fail-closed) : s'il porte un SchoolId
            // il sera évalué, sinon le check SchoolId==null l'exempte juste après.
            if (role == UserRoles.SuperAdmin || role == UserRoles.Guardian)
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

            var blocked = sub.Status switch
            {
                // ReadOnly : lecture OK, écritures bloquées.
                SubscriptionStatus.ReadOnly => isWrite,
                // Suspended : tout bloqué (hors whitelist déjà filtrée).
                SubscriptionStatus.Suspended => true,
                _ => false // Trial / Active / PendingPayment (grâce) : on laisse passer.
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
