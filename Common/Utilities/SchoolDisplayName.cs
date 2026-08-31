namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Nom d'un daara dans ses deux écritures. Source UNIQUE, côté serveur, des
    /// règles d'affichage décidées le 2026-08-08 (proposition validée) :
    ///
    ///  • R2 — l'ordre suit la langue du lecteur. Un document PDF étant toujours
    ///    en français, le nom français y passe devant.
    ///  • R4 — une école qui n'a qu'un seul nom le voit occuper la place du
    ///    principal : jamais de trou, jamais de parenthèse vide, jamais de
    ///    « (non renseigné) ».
    ///
    /// ⚠️ Ne JAMAIS concaténer les deux noms sur une ligne (« Nom (الاسم) ») :
    /// c'est exactement le bricolage qu'on remplace, et deux écritures de sens
    /// opposés mises bout à bout déplacent la ponctuation de façon imprévisible.
    /// Les deux noms s'impriment sur DEUX lignes, chacune avec sa direction.
    /// </summary>
    public readonly record struct SchoolDisplayName
    {
        private SchoolDisplayName(string? fr, string? ar)
        {
            Fr = string.IsNullOrWhiteSpace(fr) ? null : fr.Trim();
            Ar = string.IsNullOrWhiteSpace(ar) ? null : ar.Trim();
        }

        /// <summary>Nom en français, ou null s'il n'est pas renseigné.</summary>
        public string? Fr { get; }

        /// <summary>Nom en arabe, ou null s'il n'est pas renseigné.</summary>
        public string? Ar { get; }

        public static SchoolDisplayName From(string? fr, string? ar) => new(fr, ar);

        public static SchoolDisplayName From(Models.School? school) =>
            new(school?.Name, school?.NameAr);

        /// <summary>
        /// Nom mis en avant sur un document français : le nom français, à défaut
        /// le nom arabe, à défaut <paramref name="fallback"/>.
        /// </summary>
        public string Primary(string fallback = "École") => Fr ?? Ar ?? fallback;

        /// <summary>
        /// Second nom, affiché sous le principal — null quand il n'y en a qu'un
        /// (l'appelant n'imprime alors simplement rien de plus).
        /// </summary>
        public string? Secondary => Fr != null ? Ar : null;

        /// <summary>true si le second nom est en arabe (donc à composer en RTL).</summary>
        public bool SecondaryIsArabic => Secondary != null;

        /// <summary>Les deux écritures sont renseignées.</summary>
        public bool HasBoth => Fr != null && Ar != null;
    }
}
