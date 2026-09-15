using Idara.API.Data;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Chercher une personne — <b>un seul endroit</b>, pour les élèves comme
    /// pour les comptes.
    ///
    /// <para>La règle est écrite ici et nulle part ailleurs : c'est ce qui
    /// garantit que la liste des élèves, l'annuaire du personnel, la recherche
    /// de responsable et l'historique des transactions se comportent
    /// exactement pareil. Trois écrans avec trois règles de recherche, c'est
    /// trois fois l'occasion d'un « pourquoi je ne trouve pas cet enfant ? »
    /// sans réponse.</para>
    ///
    /// <para>Ce qui est interrogé : <c>SearchIndex</c>, le champ dérivé qui
    /// porte déjà le nom plié (sans accents), sa translittération si le nom est
    /// en caractères arabes, et son squelette consonantique — plus le matricule
    /// et les coordonnées, qu'on cherche aussi très souvent.</para>
    /// </summary>
    public static class PersonSearch
    {
        /// <summary>
        /// Élèves dont le nom, le matricule ou le nom d'un responsable
        /// correspond. Le terme vide laisse la requête intacte.
        /// </summary>
        public static IQueryable<Student> OuLeNomCorrespond(
            this IQueryable<Student> query, string? terme)
        {
            var (a, b, c) = TroisMotifs(terme);
            if (a == null) return query;

            // Trois motifs au plus, par construction de MotifsPourIndex — d'où
            // ce OR déplié plutôt qu'une boucle : EF doit pouvoir le traduire
            // en SQL, et un prédicat construit dynamiquement ne se traduit pas.
            return query.Where(s =>
                EF.Functions.ILike(s.SearchIndex ?? "", a)
                || (b != null && EF.Functions.ILike(s.SearchIndex ?? "", b))
                || (c != null && EF.Functions.ILike(s.SearchIndex ?? "", c))
                || EF.Functions.ILike(AppDbContext.Unaccent(s.StudentNumber ?? ""), a));
        }

        /// <summary>
        /// Comptes dont le nom, le téléphone ou l'e-mail correspond.
        ///
        /// <para>Le téléphone est cherché sur les CHIFFRES du terme : on tape
        /// « 77 630 » aussi bien que « 77630 » (§265).</para>
        /// </summary>
        public static IQueryable<User> OuLeNomCorrespond(
            this IQueryable<User> query, string? terme)
        {
            var (a, b, c) = TroisMotifs(terme);
            if (a == null) return query;

            var chiffres = new string((terme ?? "").Where(char.IsDigit).ToArray());
            var motifTel = chiffres.Length >= 3 ? $"%{chiffres}%" : null;

            return query.Where(u =>
                EF.Functions.ILike(u.SearchIndex ?? "", a)
                || (b != null && EF.Functions.ILike(u.SearchIndex ?? "", b))
                || (c != null && EF.Functions.ILike(u.SearchIndex ?? "", c))
                || EF.Functions.ILike(AppDbContext.Unaccent(u.Email ?? ""), a)
                // Un numéro n'a pas d'accents, et le plier ne change donc rien —
                // mais la règle « toute colonne comparée est pliée » vaut mieux
                // sans exception : une exception, c'est un cas qu'on oubliera de
                // vérifier, et check-search-parity.js n'aurait plus qu'une
                // liste blanche à maintenir.
                || (motifTel != null
                    && EF.Functions.ILike(AppDbContext.Unaccent(u.PhoneNumber ?? ""), motifTel)));
        }

        /// <summary>
        /// Les motifs de <see cref="SearchText.MotifsPourIndex"/>, ramenés à
        /// trois emplacements fixes pour rester traduisibles par EF.
        /// <c>A == null</c> signifie « pas de recherche ».
        /// </summary>
        public static (string? A, string? B, string? C) TroisMotifs(string? terme)
        {
            var motifs = SearchText.MotifsPourIndex(terme);
            return (
                motifs.Count > 0 ? motifs[0] : null,
                motifs.Count > 1 ? motifs[1] : null,
                motifs.Count > 2 ? motifs[2] : null);
        }

        /// <summary>
        /// Le motif à employer pour un texte LIBRE — une note de caisse, un
        /// motif de retrait, un titre d'événement, une catégorie.
        ///
        /// <para>Il n'y a pas d'index de recherche sur ces champs, et il n'en
        /// faut pas : la colonne est pliée en SQL par
        /// <c>AppDbContext.Unaccent(...)</c> et comparée à ce motif, déjà plié
        /// ici. C'est exactement la même règle d'or, appliquée au moteur plutôt
        /// qu'à une colonne — et c'est ce qui la rend vraie partout, y compris
        /// aux endroits auxquels personne n'a pensé.</para>
        /// </summary>
        public static string MotifTexteLibre(string? terme) => $"%{SearchText.Fold(terme)}%";
    }
}
