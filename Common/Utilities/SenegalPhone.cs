using System.Text.RegularExpressions;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Normalisation des numéros de téléphone sénégalais au format E.164
    /// (<c>+221XXXXXXXXX</c>), exigé par l'API SMS Africa's Talking.
    ///
    /// Accepte les saisies humaines courantes : espaces, points, tirets,
    /// préfixe <c>00221</c>, <c>+221</c>, ou un numéro national à 9 chiffres
    /// commençant par 7 (70/75/76/77/78). Rejette tout le reste.
    /// </summary>
    public static class SenegalPhone
    {
        // Mobiles SN : 9 chiffres commençant par 7X (70,75,76,77,78...).
        private static readonly Regex NationalMobile = new(@"^7\d{8}$", RegexOptions.Compiled);

        /// <summary>
        /// Retourne le numéro en E.164 (<c>+221XXXXXXXXX</c>) ou <c>null</c> si
        /// la saisie ne correspond pas à un mobile sénégalais reconnaissable.
        /// </summary>
        public static string? Normalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Ne garder que les chiffres et un éventuel '+' de tête.
            var trimmed = raw.Trim();
            var hasPlus = trimmed.StartsWith('+');
            var digits = Regex.Replace(trimmed, @"\D", "");

            if (string.IsNullOrEmpty(digits)) return null;

            // Forme internationale : 221 + 9 chiffres (avec ou sans + / 00).
            if (digits.StartsWith("00221")) digits = digits[2..];      // 00221... -> 221...
            if (hasPlus || digits.StartsWith("221"))
            {
                if (digits.StartsWith("221")) digits = digits[3..];
            }

            // À ce stade `digits` doit être le numéro national à 9 chiffres.
            return NationalMobile.IsMatch(digits) ? "+221" + digits : null;
        }

        /// <summary>Vrai si <paramref name="raw"/> est un mobile SN normalisable.</summary>
        public static bool IsValid(string? raw) => Normalize(raw) != null;

        /// <summary>
        /// Numéro lisible par un humain : <c>+221771234567</c> → <c>77 123 45 67</c>.
        ///
        /// <para>L'indicatif est implicite au Sénégal : il coûte de la largeur de
        /// colonne dans un export et gêne la lecture quand on veut simplement
        /// composer le numéro. Un numéro non reconnu est renvoyé tel quel plutôt
        /// que masqué — mieux vaut un format inattendu qu'une information perdue.</para>
        ///
        /// <para><b>Source unique de l'affichage</b> : cette logique existait en
        /// double (un helper privé dans le service d'export PDF, une copie dans le
        /// service d'alerte). Deux copies finissent toujours par diverger, dès
        /// qu'on corrige un cas particulier dans l'une seulement.</para>
        /// </summary>
        /// <param name="fallback">Rendu quand il n'y a pas de numéro (« - » dans un
        /// tableau PDF, chaîne vide pour omettre une ligne d'e-mail).</param>
        public static string ToDisplay(string? raw, string fallback = "")
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            // « 00221… » comme forme internationale : accepté par Normalize, donc
            // il peut se trouver dans un champ saisi à la main (téléphone du père,
            // d'un bénéficiaire) qui n'est pas passé par la normalisation.
            if (digits.StartsWith("00221")) digits = digits[2..];
            if (digits.StartsWith("221") && digits.Length == 12) digits = digits[3..];
            if (digits.Length != 9) return raw.Trim();
            return $"{digits[..2]} {digits[2..5]} {digits[5..7]} {digits[7..]}";
        }
    }
}
