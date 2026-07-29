namespace Idara.API.Common.Extensions
{
    /// <summary>
    /// Accès au code de corrélation de la requête courante (cf.
    /// <see cref="Observability.TraceCode"/>). Source unique : ne jamais relire
    /// l'en-tête à la main ailleurs, sinon un appelant malveillant contournerait
    /// la validation de format.
    /// </summary>
    public static class HttpContextTraceExtensions
    {
        internal const string ItemKey = "__idara_trace__";

        /// <summary>
        /// Code de la requête courante, ou chaîne vide si le middleware de
        /// corrélation n'a pas encore tourné (cas des ressources statiques).
        /// </summary>
        public static string GetTraceCode(this HttpContext context) =>
            context.Items.TryGetValue(ItemKey, out var value) && value is string s ? s : string.Empty;
    }
}
