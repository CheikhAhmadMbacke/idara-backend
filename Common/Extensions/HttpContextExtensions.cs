using System.Security.Cryptography;
using System.Text;
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

        // ================================================================
        // Adresse de l'appelant, pour les garde-fous anti « SMS pumping »
        // ================================================================

        /// <summary>
        /// Adresse de l'appelant, ou <c>"unknown"</c>.
        ///
        /// <para>🔴 <b>Ne rend quelque chose d'utile que si
        /// <c>UseForwardedHeaders</c> est posé</b> (Program.cs, en tête du
        /// pipeline). Sans lui, derrière nginx, toutes les requêtes portent
        /// l'adresse du proxy — la même pour tout le monde — et un compteur par
        /// adresse est à la fois inopérant et dangereux.</para>
        /// </summary>
        /// <remarks>
        /// Les adresses IPv4 arrivent parfois sous leur forme « mappée IPv6 »
        /// (<c>::ffff:41.82.x.x</c>) selon la pile réseau. Sans normalisation, la
        /// même machine compterait pour deux appelants distincts et disposerait
        /// du double de son quota.
        /// </remarks>
        public static string GetClientIp(this HttpContext? ctx)
        {
            var ip = ctx?.Connection.RemoteIpAddress;
            if (ip == null) return "unknown";
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            return ip.ToString();
        }

        /// <summary>
        /// Empreinte stable de l'adresse, à ranger en base à la place de
        /// l'adresse elle-même.
        ///
        /// <para>🔒 Une adresse IP est une donnée personnelle, et Pyranil en est
        /// <b>sous-traitant</b> (§215). On n'a besoin que de <i>compter les
        /// appels d'un même appelant</i> — jamais de savoir qui il est, ni de
        /// pouvoir le retrouver. Une empreinte suffit à compter et ne se remonte
        /// pas.</para>
        ///
        /// <para>⚠️ Le sel vient de la configuration et <b>ne doit pas changer</b> :
        /// le modifier réinitialise tous les compteurs en cours, donc relâche les
        /// garde-fous le temps que les fenêtres glissantes se remplissent. Absent,
        /// on retombe sur un sel fixe — l'empreinte reste utile pour compter,
        /// seulement moins résistante à qui aurait la base ET la liste des
        /// adresses possibles.</para>
        /// </summary>
        public static string HashIp(string ip, string? salt)
        {
            var material = (salt ?? "idara-auth-throttle") + "|" + ip;
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
            // 32 caractères hexadécimaux : de quoi rendre une collision
            // impossible en pratique, sans stocker 64 caractères par ligne.
            return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
        }
    }
}
