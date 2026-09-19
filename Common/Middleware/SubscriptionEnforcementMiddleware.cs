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
    /// Applique la machine à états d'abonnement : dès qu'un abonnement est
    /// impayé, l'école passe en LECTURE SEULE — les écritures renvoient 402,
    /// les lectures passent. Puis, SEPT JOURS plus tard (statut Suspended),
    /// <b>tout</b> est bloqué et l'application n'affiche plus qu'un mur de
    /// paiement.
    ///
    /// ⚠️ Ce second palier revient sur la décision §102 (« jamais de blocage des
    /// lectures »), assumé et daté : sans conséquence propre, l'escalade du 15
    /// ne changeait rien du tout. NE FAIT RIEN tant que le SuperAdmin n'a pas
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
            // 🔑 CE QUI FAIT RENTRER DE L'ARGENT RESTE OUVERT (Cheikh,
            // 2026-09-19) : « toute action de l'école peut être bloquée sauf
            // celles qui permettent de faire rentrer directement l'argent dans
            // son wallet, et donc à la plateforme d'être payée ».
            //
            // Sans ces deux lignes, on couperait à une école impayée le seul
            // robinet qui peut la remettre à flot — et on se priverait
            // nous-mêmes d'être payés. Le lien de paiement est d'autant plus
            // vital que 4 écoles sur 7 n'encaissent RIEN par la plateforme.
            //
            // ⚠️ Les préfixes sont les VRAIES routes des contrôleurs, vérifiées
            // une à une : le lien des familles vit sous « /api/fees/… » et non
            // « /api/payment-links », et une liste blanche qui ne correspond à
            // aucune route ne protège rien — en silence.
            "/api/fees/payment-links",
            "/api/donations",
            "/api/donation-campaigns",
            "/api/push",
            "/pay",
            // Page publique des tarifs : c'est précisément quand une école est
            // en impayé qu'elle a besoin de relire les formules.
            "/plans",
            "/api/public",
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

            // ============ DEUX PALIERS, DEUX CONSÉQUENCES ============
            //
            // ① ReadOnly (du 8 au 15) : les ÉCRITURES sont bloquées, les lectures
            //    passent. L'école continue de consulter ses élèves et ses
            //    paiements ; elle ne peut plus rien modifier.
            //
            // ② Suspended (à partir du 15) : TOUT est bloqué, lectures comprises.
            //    L'application n'affiche plus qu'un mur de paiement.
            //
            // ⚠️ Ce second palier REVIENT SUR LA DÉCISION §102 (« jamais de
            // blocage des lectures »), et c'est assumé, daté et voulu (Cheikh,
            // 2026-09-19) : sans conséquence propre, le 15 ne changeait
            // strictement rien — les trois états impayés bloquaient la même
            // chose, et l'escalade n'était qu'un jeu de dates.
            //
            // 🔑 Le directeur peut TOUJOURS se connecter (« /api/auth » reste en
            // liste blanche) : un refus de connexion ressemble à un compte
            // piraté et déclenche un appel au support, là où un mur qui annonce
            // le montant dû se comprend seul. Et son lien de paiement, lui, vit
            // hors de l'application : il s'ouvre sans compte.
            var blocked = sub.Status switch
            {
                SubscriptionStatus.PendingPayment => isWrite,
                SubscriptionStatus.ReadOnly => isWrite,
                SubscriptionStatus.Suspended => true,
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
                // Un refus dit CE QU'IL FAUT FAIRE, jamais seulement qu'il
                // refuse (§282). « Rechargez votre wallet » ne convenait plus :
                // 4 écoles sur 7 n'encaissent rien par la plateforme et n'ont
                // donc jamais de solde à recharger.
                message = sub.Status == SubscriptionStatus.Suspended
                    ? "Accès bloqué : votre abonnement est impayé. Réglez-le depuis le lien reçu par SMS pour rétablir l'accès."
                    : "Abonnement impayé : votre espace est en lecture seule. Réglez-le depuis le lien reçu par SMS pour débloquer.",
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
