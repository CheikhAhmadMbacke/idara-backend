using System.Text.Json;

namespace Idara.API.Common.Middleware
{
    /// <summary>
    /// Applique le mode LECTURE SEULE du rôle <see cref="Idara.API.Constants.UserRoles.SchoolViewer"/>
    /// (observateur : propriétaire, superviseur, auditeur…) : il voit tout ce que voit le SchoolAdmin (grâce
    /// aux claims de rôle secondaires posés dans le JWT, cf. JwtService) mais
    /// AUCUNE écriture (POST/PUT/PATCH/DELETE) ne lui est permise → 403.
    ///
    /// C'est CE middleware — et non les `[Authorize(Roles=...)]` — qui garantit
    /// l'impossibilité d'écrire : on n'a donc pas à exclure SchoolViewer des ~88
    /// attributs d'autorisation. Seules quelques écritures « self-service »
    /// (gérer sa propre session / son mot de passe / ses tokens push) restent
    /// autorisées, pour qu'il puisse se connecter, déverrouiller l'espace
    /// paiement (verify-password) et recevoir des notifications.
    /// </summary>
    public class ReadOnlyRoleMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ReadOnlyRoleMiddleware> _logger;

        // Écritures self-service tolérées pour un compte lecture seule. Chemins
        // EXACTS (pas de préfixe large) : ne JAMAIS whitelister tout /api/push ni
        // tout /api/auth, qui contiennent des écritures non self-service
        // (broadcast, invite-user…). Toute nouvelle route mutante reste bloquée
        // par défaut tant qu'elle n'est pas ajoutée ici explicitement.
        private static readonly string[] WriteWhitelist =
        {
            "/api/auth/login",
            "/api/auth/logout",
            "/api/auth/refresh",
            "/api/auth/change-password",
            "/api/auth/verify-password",
            "/api/push/register",
            "/api/push/unregister",
            "/api/push/test"
        };

        public ReadOnlyRoleMiddleware(
            RequestDelegate next, ILogger<ReadOnlyRoleMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext ctx)
        {
            if (ctx.User?.Identity?.IsAuthenticated != true) { await _next(ctx); return; }

            // Détection par claim DÉDIÉ "readonly=true" (posé par JwtService pour
            // SchoolViewer) — indépendant de l'ordre des claims de rôle. Fail-closed :
            // si ce claim n'est pas là, ce n'est pas un compte lecture seule.
            if (!ctx.User.HasClaim("readonly", "true")) { await _next(ctx); return; }

            var isWrite = HttpMethods.IsPost(ctx.Request.Method)
                || HttpMethods.IsPut(ctx.Request.Method)
                || HttpMethods.IsPatch(ctx.Request.Method)
                || HttpMethods.IsDelete(ctx.Request.Method);
            if (!isWrite) { await _next(ctx); return; }

            var path = ctx.Request.Path;
            foreach (var allowed in WriteWhitelist)
            {
                if (path.StartsWithSegments(allowed, StringComparison.OrdinalIgnoreCase))
                {
                    await _next(ctx);
                    return;
                }
            }

            _logger.LogInformation(
                "[readonly-role] 403 lecture seule sur {Method} {Path}",
                ctx.Request.Method, path);

            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            ctx.Response.ContentType = "application/json";
            var payload = new
            {
                success = false,
                message = "Compte en lecture seule : modifications non autorisées."
            };
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(payload), ctx.RequestAborted);
        }
    }
}
