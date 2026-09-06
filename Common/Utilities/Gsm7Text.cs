using System.Globalization;
using System.Text;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Ramène un texte dans l'alphabet GSM 03.38, celui qui permet à un SMS de
    /// tenir 160 caractères par segment au lieu de 70.
    ///
    /// <para><b>Pourquoi c'est une question d'argent et pas de typographie.</b>
    /// Un SEUL caractère hors alphabet fait basculer le message ENTIER en UCS-2,
    /// où le segment ne vaut plus que 67 caractères. Mesuré sur le message de la
    /// campagne : 2 segments (7 F) deviennent 5 segments (17,50 F) — <b>2,5 fois
    /// le prix pour un tréma</b>. Et le piège est parfaitement invisible : le
    /// message s'affiche normalement, seule la facture le dit.</para>
    ///
    /// <para><b>Les caractères qui piègent sont ceux qu'on écrit tous les
    /// jours.</b> L'alphabet GSM-7 contient à é è ù ì ò ñ ä ö ü Ç, mais PAS
    /// <c>ë</c> (wolof : bëss, tëdd), PAS <c>ï</c> (Maïmouna, Aïssatou), PAS
    /// <c>ó</c> ni <c>ŋ</c> (wolof), PAS <c>ç</c> minuscule. Autrement dit : un
    /// prénom sénégalais courant suffit à multiplier par 2,5 le coût du SMS de
    /// cette famille, tous les mois.</para>
    ///
    /// <para>La substitution est celle que tout le monde fait déjà en écrivant un
    /// SMS au Sénégal — « Maimouna », « bess ». Elle ne s'applique qu'aux parties
    /// VARIABLES d'un message (nom d'école, nom de responsable) : les textes
    /// rédigés, eux, sont écrits directement dans l'alphabet sûr.</para>
    /// </summary>
    public static class Gsm7Text
    {
        // Caractères hors GSM-7 rencontrés en pratique, et leur équivalent sûr.
        // Les remplacements qui EXISTENT dans l'alphabet (é, à, ñ, ü…) ne sont
        // volontairement pas touchés : ils ne coûtent rien.
        private static readonly Dictionary<char, string> Direct = new()
        {
            ['ë'] = "e", ['Ë'] = "E",
            ['ï'] = "i", ['Ï'] = "I",
            ['î'] = "i", ['Î'] = "I",
            ['ô'] = "o", ['Ô'] = "O",
            ['ó'] = "o", ['Ó'] = "O",
            ['û'] = "u", ['Û'] = "U",
            ['â'] = "a", ['Â'] = "A",
            ['ê'] = "e", ['Ê'] = "E",
            ['ç'] = "c", ['ć'] = "c",
            ['ŋ'] = "ng", ['Ŋ'] = "NG",
            ['ã'] = "a", ['õ'] = "o",
            ['œ'] = "oe", ['Œ'] = "OE",
            ['’'] = "'", ['‘'] = "'",
            ['“'] = "\"", ['”'] = "\"",
            ['–'] = "-", ['—'] = "-",
            ['…'] = "...",
            [' '] = " ", // espace insécable
        };

        /// <summary>
        /// Rend le texte sûr pour un envoi en GSM-7. Tout caractère resté hors
        /// alphabet après substitution est décomposé (accent retiré) ; s'il ne
        /// se décompose pas, il est supprimé plutôt que laissé — un caractère
        /// exotique isolé ne vaut pas 2,5 fois le prix du message.
        /// </summary>
        public static string Sanitize(string? input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            var sb = new StringBuilder(input.Length);
            foreach (var c in input)
            {
                if (Direct.TryGetValue(c, out var repl)) { sb.Append(repl); continue; }
                if (SmsSegmentCalculator.IsGsm7Char(c)) { sb.Append(c); continue; }

                // Dernier recours : retirer le diacritique (é reste é car il est
                // déjà dans l'alphabet ; ẞ, ā, ő… retombent sur leur lettre).
                var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
                var kept = false;
                foreach (var d in decomposed)
                {
                    if (CharUnicodeInfo.GetUnicodeCategory(d) == UnicodeCategory.NonSpacingMark) continue;
                    if (!SmsSegmentCalculator.IsGsm7Char(d)) continue;
                    sb.Append(d);
                    kept = true;
                }
                if (!kept && char.IsWhiteSpace(c)) sb.Append(' ');
            }
            return sb.ToString();
        }
    }
}
