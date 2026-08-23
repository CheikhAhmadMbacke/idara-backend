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
        /// Noms connus du Coran, déjà normalisés (minuscules, sans accents ni
        /// harakat, sans espace ni ponctuation, article retiré).
        /// </summary>
        private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
        {
            // Français / translittérations latines
            "coran", "corane", "coranique",
            "quran", "qurane", "quraan", "qoran", "qouran", "koran", "kuran",
            "suivicoran", "suiviquran",
            // Mémorisation (tahfîz / hifz), employé tel quel par beaucoup de daara
            "tahfiz", "tahfid", "tahfidh", "tahfeez", "tahfeedh", "tahfith",
            "hifz", "hifd", "hifdh",
            // Arabe (après normalisation : آ → ا, harakat retirées, « ال » retiré)
            "قران", "تحفيظ", "حفظ",
        };

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
                    return compact[article.Length..];
            }
            return compact;
        }

        private static readonly string[] Articles = { "ال", "al", "el", "le", "la" };
    }
}
