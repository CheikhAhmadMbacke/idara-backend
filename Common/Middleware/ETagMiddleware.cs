using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Idara.API.Common.Middleware
{
    /// <summary>
    /// Ajoute un <c>ETag</c> aux référentiels stables et répond <c>304 Not
    /// Modified</c> quand le client possède déjà la bonne version.
    ///
    /// <para><b>Pourquoi (lot 5 du chantier performance, 2026-07-28).</b> La
    /// liste des classes, des matières, des enseignants ou de l'emploi du temps
    /// ne change que quelques fois par an, mais elle est retéléchargée
    /// intégralement à chaque ouverture d'écran. Avec un ETag, le serveur
    /// répond « rien n'a changé » en quelques dizaines d'octets et le client
    /// réutilise son instantané local. Sur un forfait payé au mégaoctet, c'est
    /// autant de gagné à chaque navigation.</para>
    ///
    /// <para><b>Liste blanche stricte.</b> Seuls les chemins énumérés ici sont
    /// concernés. Rien de monétaire, rien de personnel volatil : un ETag mal
    /// placé ferait servir au client une version périmée d'un solde.</para>
    ///
    /// <para><b>Le corps est mis en mémoire tampon</b> le temps d'en calculer
    /// l'empreinte. C'est acceptable ici précisément parce que ces réponses sont
    /// petites et peu nombreuses — d'où, encore une fois, la liste blanche.</para>
    /// </summary>
    public class ETagMiddleware
    {
        private readonly RequestDelegate _next;

        /// Référentiels stables uniquement. Le motif doit couvrir le chemin
        /// COMPLET (préfixe /api inclus).
        private static readonly Regex Cacheable = new(
            @"^/api/(classes|subjects|teachers|class-assignments|academic-years|timetable)(/[\w-]+)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public ETagMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            if (!HttpMethods.IsGet(context.Request.Method) ||
                !Cacheable.IsMatch(context.Request.Path.Value ?? string.Empty))
            {
                await _next(context);
                return;
            }

            var originalBody = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;

            try
            {
                await _next(context);
            }
            catch
            {
                // Rendre le flux d'origine AVANT de laisser remonter : sinon le
                // middleware d'exception écrirait dans un tampon qu'on jette.
                context.Response.Body = originalBody;
                buffer.Position = 0;
                await buffer.CopyToAsync(originalBody);
                throw;
            }

            context.Response.Body = originalBody;
            var bytes = buffer.ToArray();

            if (context.Response.StatusCode != StatusCodes.Status200OK || bytes.Length == 0)
            {
                await originalBody.WriteAsync(bytes);
                return;
            }

            var etag = $"\"{Convert.ToHexString(SHA256.HashData(bytes))[..32].ToLowerInvariant()}\"";
            context.Response.Headers.ETag = etag;

            if (Matches(context.Request.Headers.IfNoneMatch, etag))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                context.Response.ContentLength = null;
                // Un 304 ne PORTE PAS de corps : écrire quoi que ce soit ici
                // ferait échouer la requête côté client.
                return;
            }

            context.Response.ContentLength = bytes.Length;
            await originalBody.WriteAsync(bytes);
        }

        /// <summary>
        /// Compare l'en-tête <c>If-None-Match</c> à l'ETag calculé.
        ///
        /// ⚠️ Le préfixe <c>W/</c> est ignoré des deux côtés, et ce n'est pas un
        /// détail : nginx AFFAIBLIT un ETag fort dès qu'il compresse la réponse
        /// (il le réécrit en <c>W/"…"</c>, puisqu'il a modifié le corps). Le
        /// client nous renvoie donc la forme affaiblie, et une comparaison
        /// littérale ne correspondrait JAMAIS — l'ETag ne servirait à rien tout
        /// en donnant l'illusion de fonctionner.
        /// </summary>
        private static bool Matches(string? ifNoneMatch, string etag)
        {
            if (string.IsNullOrWhiteSpace(ifNoneMatch)) return false;
            var expected = Strip(etag);
            foreach (var candidate in ifNoneMatch.Split(','))
            {
                var value = Strip(candidate);
                if (value == "*" || string.Equals(value, expected, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static string Strip(string value)
        {
            var v = value.Trim();
            if (v.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) v = v[2..];
            return v.Trim('"');
        }
    }
}
