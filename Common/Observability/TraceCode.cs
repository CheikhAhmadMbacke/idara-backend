using System.Security.Cryptography;

namespace Idara.API.Common.Observability
{
    /// <summary>
    /// Code d'incident lisible à voix haute, du type <c>IDR-7K2MQ4</c>.
    ///
    /// <para><b>Pourquoi un code et pas un GUID.</b> C'est LA pièce qui fait
    /// disparaître le cycle « décris-moi le bug ». Un directeur de daara voit ce
    /// code sur son écran d'erreur, l'envoie par WhatsApp, et on retrouve d'un
    /// coup : l'utilisateur, son école, son écran, la requête HTTP, la trace
    /// serveur. Un identifiant de 36 caractères ne serait ni lu au téléphone ni
    /// recopié sans faute.</para>
    ///
    /// <para><b>Alphabet.</b> 30 caractères, sans <c>0 1 I L O U</c> : ce sont
    /// exactement les confusions qui se produisent quand on épelle un code
    /// (zéro/O, un/I/L) ou qu'on le dicte en français. 30⁶ ≈ 729 millions de
    /// combinaisons — à ~430 requêtes/jour et 30 jours de rétention, la
    /// probabilité de collision est négligeable.</para>
    ///
    /// <para><b>Un seul identifiant pour deux usages.</b> Le même code sert
    /// d'identifiant de corrélation d'une requête (en-tête
    /// <c>X-Idara-Trace</c>) et de code d'incident affiché. Deux notions
    /// distinctes auraient obligé l'utilisateur à savoir laquelle nous
    /// communiquer.</para>
    /// </summary>
    public static class TraceCode
    {
        public const string HeaderName = "X-Idara-Trace";

        private const string Prefix = "IDR-";
        private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";
        private const int BodyLength = 6;

        /// <summary>Longueur totale attendue (<c>IDR-</c> + 6 caractères).</summary>
        public const int TotalLength = 4 + BodyLength;

        /// <summary>Génère un nouveau code.</summary>
        public static string New()
        {
            var chars = new char[TotalLength];
            Prefix.CopyTo(0, chars, 0, Prefix.Length);
            for (var i = 0; i < BodyLength; i++)
            {
                chars[Prefix.Length + i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }
            return new string(chars);
        }

        /// <summary>
        /// Normalise ce que fournit un client (ou ce qu'un humain a recopié) et
        /// refuse tout ce qui ne rentre pas strictement dans le format.
        ///
        /// <para><b>Ce contrôle n'est pas cosmétique</b> : le code arrive par un
        /// en-tête HTTP contrôlé par l'appelant, et il est ensuite recopié dans
        /// les fichiers de journal, dans une réponse HTTP et dans une requête SQL.
        /// Sans validation, un appelant pourrait y glisser des retours à la ligne
        /// et fabriquer de fausses lignes de journal (log injection), ou faire
        /// grossir chaque ligne écrite.</para>
        /// </summary>
        public static string? TryNormalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var value = raw.Trim().ToUpperInvariant();
            // Tolère un code recopié sans le tiret ni le préfixe (« 7K2MQ4 »),
            // parce que c'est ce qu'un directeur enverra une fois sur deux.
            if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            {
                value = value.Replace("-", string.Empty);
                if (value.StartsWith("IDR", StringComparison.Ordinal)) value = value[3..];
                value = Prefix + value;
            }

            if (value.Length != TotalLength) return null;
            for (var i = Prefix.Length; i < value.Length; i++)
            {
                if (!Alphabet.Contains(value[i])) return null;
            }
            return value;
        }
    }
}
