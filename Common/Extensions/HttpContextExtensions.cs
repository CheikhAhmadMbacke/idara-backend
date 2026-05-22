using Microsoft.Net.Http.Headers;

namespace Idara.API.Common.Extensions
{
    public static class HttpContextExtensions
    {
        /// <summary>
        /// Détermine la langue préférée du client à partir du header
        /// <c>Accept-Language</c>. Renvoie <c>"ar"</c> si l'arabe arrive en
        /// premier (avec ou sans suffixe régional), sinon <c>"fr"</c>.
        /// Tolérant : aucun header → "fr".
        /// </summary>
        public static string GetPreferredLanguage(this HttpContext? ctx)
        {
            if (ctx == null) return "fr";

            var headerValue = ctx.Request.Headers[HeaderNames.AcceptLanguage].ToString();
            if (string.IsNullOrWhiteSpace(headerValue)) return "fr";

            // On prend le premier code de langue (avant la première virgule ou
            // point-virgule) et on regarde son préfixe.
            var first = headerValue.Split(',', ';').FirstOrDefault()?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(first)) return "fr";

            if (first.StartsWith("ar")) return "ar";
            return "fr";
        }
    }
}
