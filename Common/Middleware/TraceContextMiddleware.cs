using Idara.API.Common.Extensions;
using Idara.API.Common.Observability;

namespace Idara.API.Common.Middleware
{
    /// <summary>
    /// Établit le code de corrélation de la requête et le renvoie au client.
    ///
    /// <para><b>Où il se place et pourquoi.</b> Tout au début du pipeline, AVANT
    /// <see cref="GlobalExceptionMiddleware"/> : c'est ce dernier qui doit pouvoir
    /// annoncer le code à l'utilisateur quand tout casse. Une réponse d'erreur
    /// sans code, c'est le retour à « ça ne marche pas ».</para>
    ///
    /// <para><b>Le client propose, le serveur dispose.</b> Si l'application
    /// envoie un code au format attendu, on l'adopte : la même chaîne relie alors
    /// la chronologie côté téléphone et les lignes de journal côté serveur.
    /// Sinon (site web, <c>curl</c>, ancienne version de l'app, ou en-tête
    /// falsifié) on en génère un — la corrélation ne dépend donc jamais du bon
    /// vouloir du client.</para>
    ///
    /// <para>⚠️ Le code est repris dans l'en-tête <b>de réponse</b> ; pour que le
    /// JavaScript de la version web puisse le lire, il faut qu'il soit déclaré
    /// dans <c>WithExposedHeaders</c> de la politique CORS — sans quoi le
    /// navigateur le masque et l'écran d'erreur web n'affiche aucun code, alors
    /// que le mobile en affiche un.</para>
    /// </summary>
    public class TraceContextMiddleware
    {
        private readonly RequestDelegate _next;

        public TraceContextMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            var code = TraceCode.TryNormalize(context.Request.Headers[TraceCode.HeaderName])
                       ?? TraceCode.New();

            context.Items[HttpContextTraceExtensions.ItemKey] = code;

            // Aligne l'identifiant natif d'ASP.NET sur le nôtre : les journaux de
            // la plateforme (Kestrel, diagnostics) deviennent ainsi corrélables
            // avec les nôtres sans travail supplémentaire.
            context.TraceIdentifier = code;

            // Posé AVANT d'appeler la suite : une fois la réponse commencée, les
            // en-têtes ne sont plus modifiables (et une réponse en streaming ou
            // un 304 partirait sans code).
            context.Response.Headers[TraceCode.HeaderName] = code;

            await _next(context);
        }
    }
}
