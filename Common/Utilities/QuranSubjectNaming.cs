using System.Globalization;
using System.Text;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// 📖 Reconnaît qu'une matière d'école EST le Coran, d'après son nom.
    ///
    /// <para><b>Pourquoi cette classe existe.</b> Le type d'une matière
    /// (<c>SubjectKind</c>) commande la forme de la fiche du Cahier de suivi :
    /// « Coran » ouvre le relevé structuré (nouvelle leçon / révision récente /
    /// ancienne révision), tout le reste ouvre du texte libre. Or ce champ a un
    /// défaut — « Matière générale » — et rien, dans le formulaire, ne disait la
    /// conséquence. Un daara qui crée « Al quran » laisse le défaut et se
    /// retrouve avec du texte libre là où il attend sa fiche.</para>
    ///
    /// <para><b>La règle est VOLONTAIREMENT ÉTROITE : égalité, jamais
    /// « contient ».</b> Le nom normalisé doit être exactement l'un des noms
    /// connus du Coran. « Histoire du Coran » et « Éducation coranique » sont
    /// des matières d'enseignement classique qui se saisissent en texte libre :
    /// une règle « contient coran » les basculerait à tort sur le relevé de
    /// mémorisation, et personne ne comprendrait pourquoi.</para>
    ///
    /// <para>Publique et statique <b>à dessein</b> : une règle qu'on ne peut
    /// vérifier qu'en démarrant l'API contre une vraie base ne se vérifie
    /// jamais.</para>
    /// </summary>
    public static class QuranSubjectNaming
    {
        /// <summary>
        /// Le mot « Coran » translittéré : <b>consonne initiale</b> × <b>voyelle</b>.
        /// </summary>
        /// <remarks>
        /// ⚠️ Une liste plate d'orthographes ne peut pas gagner contre une
        /// translittération : mesuré le 2026-08-23, elle n'attrapait que
        /// <b>14 formes plausibles sur 25</b>. On génère donc la famille au lieu
        /// de l'énumérer — « quran », « kouran », « xuraan », « khourane »… en
        /// sortent toutes seules.
        ///
        /// <para>La voyelle est toujours <c>o / ou / u</c> : jamais <c>a</c>.
        /// C'est ce qui empêche « xar » (mouton, en wolof) et ses dérivés
        /// d'entrer dans la famille.</para>
        /// </remarks>
        private static readonly string[] QuranStems =
            { "cor", "qor", "kor", "xor", "khor",
              "cour", "qour", "kour", "xour", "khour",
              "cur", "qur", "kur", "xur", "khur" };

        /// <summary>Terminaisons observées, wolofisées comprises (« xuraanu »).</summary>
        private static readonly string[] QuranEndings =
            { "an", "aan", "ane", "aane", "anu", "aanu", "ana", "aana" };

        /// <summary>
        /// Déterminants wolof accolés en fin de nom : « Xuraan <b>bi</b> »,
        /// « Alxuraan <b>bu mag</b> ». Retirés avant comparaison.
        /// </summary>
        /// <remarks>
        /// Aucun risque de faux positif : ce qui reste est comparé à un ensemble
        /// fermé. « Arabi » devient « ara », qui n'y figure pas.
        /// </remarks>
        private static readonly string[] Determiners =
            { "bumag", "bimag", "bi", "ji", "gi", "mi", "si", "wi", "ki", "li", "yi" };

        /// <summary>
        /// Noms connus du Coran, déjà normalisés (minuscules, sans accents ni
        /// harakat, sans espace ni ponctuation, article retiré). Contient les
        /// formes générées ci-dessus <b>plus</b> celles qui ne suivent aucun
        /// motif.
        /// </summary>
        private static readonly HashSet<string> Known = BuildKnown();

        private static HashSet<string> BuildKnown()
        {
            var set = new HashSet<string>(StringComparer.Ordinal)
            {
                // Formes qui ne suivent pas le motif consonne+voyelle+r+…
                "coranique", "suivicoran", "suiviquran",
                // Mémorisation (tahfîz / hifz), employé tel quel par beaucoup de daara
                "tahfiz", "tahfid", "tahfidh", "tahfeez", "tahfeedh", "tahfith",
                "hifz", "hifd", "hifdh",
                // Arabe (après normalisation : آ → ا, harakat retirées, « ال » retiré)
                "قران", "تحفيظ", "حفظ",
            };
            foreach (var stem in QuranStems)
                foreach (var end in QuranEndings)
                    set.Add(stem + end);
            return set;
        }

        /// <summary>
        /// Vrai si l'un des deux noms de la matière (français ou arabe) désigne
        /// le Coran. On teste les deux : un daara peut nommer sa matière
        /// « Mémorisation » en français et « القرآن » en arabe.
        /// </summary>
        public static bool LooksLikeQuran(string? name, string? nameAr)
            => IsKnownName(name) || IsKnownName(nameAr);

        /// <summary>Vrai si ce nom seul désigne le Coran.</summary>
        public static bool IsKnownName(string? name)
        {
            var normalized = Normalize(name);
            return normalized.Length > 0 && Known.Contains(normalized);
        }

        /// <summary>
        /// Réduit un nom à sa forme comparable : minuscules, sans signes
        /// diacritiques (accents latins ET harakat arabes — la décomposition
        /// Unicode ramène au passage « آ » sur « ا »), sans rien d'autre que des
        /// lettres et des chiffres, et sans article de tête.
        /// </summary>
        public static string Normalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var decomposed = raw.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
            {
                // Les diacritiques (accents latins, harakat/shadda arabes) et
                // tout ce qui n'est ni lettre ni chiffre (espaces, apostrophes,
                // tirets, points) disparaissent.
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            }

            var compact = sb.ToString().Normalize(NormalizationForm.FormC);

            // Article de tête, une seule fois : « Al quran » → « quran »,
            // « Le Coran » → « coran », « القرآن » → « قران ». Aucun risque de
            // faux positif : on compare ensuite à une liste fermée (« Algèbre »
            // devient « gebre », qui n'y figure pas).
            foreach (var article in Articles)
            {
                if (compact.Length > article.Length && compact.StartsWith(article, StringComparison.Ordinal))
                {
                    compact = compact[article.Length..];
                    break;
                }
            }

            // Déterminant wolof accolé en fin : « Xuraan bi », « Alxuraan bu mag ».
            foreach (var det in Determiners)
            {
                if (compact.Length > det.Length + 2 && compact.EndsWith(det, StringComparison.Ordinal))
                    return compact[..^det.Length];
            }
            return compact;
        }

        private static readonly string[] Articles = { "ال", "al", "el", "le", "la" };
    }
}
