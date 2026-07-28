using System.Security.Cryptography;
using System.Text;
using Idara.API.Options;
using Microsoft.Extensions.Options;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Construit les noms de fichiers des PDF persistés dans <c>wwwroot/uploads/</c>
    /// (reçus, bulletins, factures d'abonnement).
    ///
    /// <para><b>Pourquoi ce service existe (2026-07-28).</b> Ces PDF étaient nommés
    /// de façon purement séquentielle — <c>receipt-{paymentId}.pdf</c>,
    /// <c>bulletin-{ecoleId}-{eleveId}-{periodeId}.pdf</c>,
    /// <c>sub-invoice-{id}.pdf</c>. Or nginx sert <c>/uploads/</c> en direct, en
    /// court-circuitant .NET (CLAUDE.md §32) : n'importe qui pouvait donc
    /// télécharger l'intégralité des reçus de toutes les écoles en incrémentant un
    /// entier. La fuite est fermée au niveau nginx (deny sur ces 3 dossiers), et ce
    /// service est la <b>défense en profondeur</b> : même si la règle nginx venait à
    /// sauter lors d'une future édition de config, les noms restent indevinables.</para>
    ///
    /// <para><b>Le nom reste déterministe</b>, et c'est volontaire : toute la
    /// mécanique existante repose dessus (un upsert écrase l'ancien fichier au lieu
    /// d'accumuler, et un endpoint sait recalculer le chemin attendu d'un PDF dont
    /// le chemin n'a jamais été persisté). Le suffixe est un HMAC-SHA256 de la clé
    /// logique, tronqué à 96 bits — impossible à deviner sans la clé serveur, mais
    /// identique d'un appel à l'autre.</para>
    ///
    /// <para><b>Clé.</b> Dérivée de <c>Jwt:Key</c> (déjà validée ≥ 32 caractères au
    /// démarrage) via un HMAC à étiquette fixe, pour ne jamais utiliser la clé de
    /// signature des jetons telle quelle. Si cette clé change un jour, les noms
    /// changent : les anciens fichiers deviennent orphelins (inaccessibles, bloqués
    /// par nginx) et les endpoints régénèrent le PDF au premier téléchargement —
    /// aucun 404 côté utilisateur.</para>
    /// </summary>
    public interface IPdfFileNamer
    {
        /// <summary>
        /// Retourne le nom de fichier complet, ex.
        /// <c>receipt-42-Xk3p_9QeR1sTuVwZ.pdf</c>.
        /// </summary>
        /// <param name="prefix">Famille de document (<c>receipt</c>, <c>bulletin</c>…).</param>
        /// <param name="logicalKey">Identifiant métier stable (ex. <c>42</c> ou <c>2-7-3</c>).</param>
        string Build(string prefix, string logicalKey);
    }

    public sealed class PdfFileNamer : IPdfFileNamer
    {
        /// Étiquette de dérivation : garantit que le sous-secret utilisé ici n'a
        /// aucun rapport exploitable avec la signature des JWT.
        private const string DerivationLabel = "idara-pdf-file-name-v1";

        /// 16 caractères Base64Url = 96 bits. Très au-delà de ce qu'une énumération
        /// peut couvrir, et reste un nom de fichier court et lisible.
        private const int SuffixLength = 16;

        private readonly byte[] _key;

        public PdfFileNamer(IOptions<JwtSettings> jwt)
        {
            var seed = jwt.Value.Key ?? string.Empty;
            using var kdf = new HMACSHA256(Encoding.UTF8.GetBytes(seed));
            _key = kdf.ComputeHash(Encoding.UTF8.GetBytes(DerivationLabel));
        }

        public string Build(string prefix, string logicalKey)
        {
            using var hmac = new HMACSHA256(_key);
            var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{prefix}:{logicalKey}"));

            // Base64Url : alphabet sûr à la fois pour un nom de fichier et pour une
            // URL (pas de '+', '/' ni '=' à échapper).
            var suffix = Convert.ToBase64String(mac)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=')[..SuffixLength];

            return $"{prefix}-{logicalKey}-{suffix}.pdf";
        }
    }
}
