using System.Text.RegularExpressions;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// 🌍 Normalisation d'un numéro de téléphone <b>de n'importe quel pays</b>
    /// vers l'E.164 (<c>+CCXXXXXXXXX</c>), format de stockage et d'identité.
    /// </summary>
    /// <remarks>
    /// <para><b>Pourquoi cette classe existe, à côté de
    /// <see cref="SenegalPhone"/>.</b> Jusqu'au 2026-09-12, tout numéro entrant
    /// passait par <see cref="SenegalPhone.Normalize"/>, qui <b>refuse</b> ce
    /// qui n'est pas un mobile sénégalais. Un parent installé en France ou en
    /// Italie — la première et la deuxième destination de l'émigration
    /// sénégalaise — ne pouvait donc pas avoir de compte : son numéro était
    /// rejeté à la création comme à la connexion.</para>
    ///
    /// <para>🔴 <b>Les deux classes restent, et le choix n'est pas cosmétique.</b>
    /// L'identité accepte le monde entier ; le <b>paiement</b>, non : Wave et
    /// Orange Money n'opèrent qu'au Sénégal, un décaissement vers un numéro
    /// étranger serait de l'argent envoyé nulle part. Tout ce qui touche à un
    /// encaissement ou à un virement continue donc d'exiger
    /// <see cref="SenegalPhone"/>.</para>
    ///
    /// <para>⚠️ <b>Un SMS, lui, ne sort pas du Sénégal</b> — et c'est délibéré
    /// (<c>SmsBudgetGuard</c>) : un SMS international coûte onze fois le prix
    /// local et c'est le carburant de la fraude au « SMS pumping ». Un
    /// responsable joignable seulement à l'étranger a donc un compte qui
    /// fonctionne, mais ses identifiants se communiquent de la main à la main
    /// (le modal les affiche, §94) plutôt que par SMS.</para>
    ///
    /// <para>🔁 <b>Jumeau côté application</b> : <c>core/utils/phone_number.dart</c>
    /// applique exactement les mêmes règles, dans le même ordre. Les deux se
    /// corrigent ensemble — une divergence ferait exister le même parent sous
    /// deux numéros.</para>
    /// </remarks>
    public static class Phone
    {
        /// <summary>Indicatif appliqué à un numéro saisi sans indication de pays.</summary>
        public const string DefaultDialCode = "221";

        /// <summary>
        /// Longueur minimale d'un E.164 plausible (indicatif compris). En dessous,
        /// ce n'est pas un numéro : c'est une saisie interrompue.
        /// </summary>
        private const int MinDigits = 8;

        /// <summary>Maximum imposé par la norme E.164.</summary>
        private const int MaxDigits = 15;

        private static readonly Regex NonDigits = new(@"\D", RegexOptions.Compiled);

        /// <summary>
        /// Retourne le numéro en E.164, ou <c>null</c> si la saisie ne peut pas
        /// en produire un.
        /// </summary>
        /// <param name="raw">Saisie humaine, numéro du carnet, ou valeur relue en base.</param>
        /// <param name="defaultDialCode">
        /// Indicatif du pays supposé quand <paramref name="raw"/> n'en porte
        /// aucun. Le Sénégal par défaut : c'est le cas de l'écrasante majorité.
        /// </param>
        public static string? Normalize(string? raw, string? defaultDialCode = null)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var dial = string.IsNullOrWhiteSpace(defaultDialCode)
                ? DefaultDialCode
                : NonDigits.Replace(defaultDialCode!, "");
            if (dial.Length == 0) dial = DefaultDialCode;

            var trimmed = raw.Trim();
            var hasPlus = trimmed.StartsWith('+');
            var digits = NonDigits.Replace(trimmed, "");
            if (digits.Length == 0) return null;

            // 1) Forme internationale explicite : « + » ou « 00 » en tête.
            if (digits.StartsWith("00"))
            {
                digits = digits[2..];
                return Finish(digits);
            }
            if (hasPlus) return Finish(digits);

            // 2) International implicite : le carnet a gardé l'indicatif sans le
            //    « + ». On ne l'applique qu'à l'indicatif ATTENDU — sinon « 33… »,
            //    début parfaitement normal d'un numéro national ailleurs, serait
            //    lu comme la France.
            if (digits.Length > dial.Length + 5 && digits.StartsWith(dial))
                return Finish(digits);

            // 3) Le « 0 » de tête est un préfixe interurbain (France, Maroc,
            //    Italie…). Aucun numéro sénégalais ne commence par 0 : la règle
            //    est sans risque ici.
            digits = digits.TrimStart('0');
            if (digits.Length == 0) return null;

            // 4) Numéro national du pays supposé.
            return Finish(dial + digits);
        }

        private static string? Finish(string digits) =>
            digits.Length is >= MinDigits and <= MaxDigits ? "+" + digits : null;

        /// <summary>Vrai si <paramref name="raw"/> produit un E.164 plausible.</summary>
        public static bool IsValid(string? raw, string? defaultDialCode = null) =>
            Normalize(raw, defaultDialCode) != null;

        /// <summary>
        /// Vrai si le numéro normalisé est un mobile <b>sénégalais</b> — la seule
        /// destination qu'un paiement ou un SMS accepte.
        /// </summary>
        public static bool IsSenegalese(string? raw) =>
            SenegalPhone.Normalize(raw) != null;
    }
}
