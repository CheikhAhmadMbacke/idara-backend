using Idara.API.Enums;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Moyennes par DOMAINE d'un bulletin : « arabe et religieux » d'un côté,
    /// « français et général » de l'autre.
    ///
    /// C'est ainsi qu'une école franco-arabe présente ses résultats : les
    /// familles regardent d'abord si l'enfant suit dans SON cursus, et une
    /// moyenne générale unique mélange deux programmes distincts (un enfant
    /// excellent en Coran et faible en français est illisible autrement).
    ///
    /// Le socle existait déjà : <see cref="SubjectKind"/> distingue depuis
    /// toujours Coran / religieuse / générale. Il ne manquait que le calcul.
    /// </summary>
    public static class ReportCardDomains
    {
        /// <summary>
        /// Domaine « arabe et religieux » : le Coran ET les matières religieuses
        /// (fiqh, hadith, sira, langue arabe). Les regrouper est délibéré — dans
        /// une franco-arabe, elles forment UN cursus, souvent le même maître et
        /// la même demi-journée.
        /// </summary>
        public static bool IsArabicDomain(SubjectKind kind)
            => kind == SubjectKind.Coran || kind == SubjectKind.Religious;

        /// <summary>
        /// Moyenne pondérée par coefficient des lignes du domaine demandé.
        ///
        /// Retourne <c>null</c> — et non 0 — quand le domaine ne compte AUCUNE
        /// matière : un zéro s'afficherait comme une note catastrophique alors
        /// qu'il signifie « cette école n'enseigne pas ce domaine ». Les lignes
        /// sans type connu (bulletins générés avant ce champ) sont ignorées.
        /// </summary>
        public static double? Average(
            IEnumerable<(double Average, double Coefficient, SubjectKind? Kind)> lines,
            bool arabicDomain)
        {
            double num = 0, den = 0;
            foreach (var l in lines)
            {
                if (l.Kind is null) continue;
                if (IsArabicDomain(l.Kind.Value) != arabicDomain) continue;
                num += l.Average * l.Coefficient;
                den += l.Coefficient;
            }
            return den > 0 ? Math.Round(num / den, 2) : null;
        }

        /// <summary>
        /// Les deux moyennes ne s'affichent QUE si l'école enseigne réellement
        /// les deux domaines. Sur un daara — où tout est coranique — répéter la
        /// moyenne générale sous un second intitulé n'apprendrait rien et
        /// alourdirait le bulletin.
        /// </summary>
        public static bool ShowBothDomains(double? arabicAverage, double? generalAverage)
            => arabicAverage.HasValue && generalAverage.HasValue;
    }
}
