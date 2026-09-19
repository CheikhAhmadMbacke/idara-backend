using Idara.API.Enums;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// 🏫 Donne son type à chaque école DÉJÀ en base : daara par défaut, et les
    /// exceptions que Cheikh a désignées une par une.
    ///
    /// <para><b>Pourquoi cette classe existe.</b> Le champ <see cref="SchoolType"/>
    /// est arrivé le 2026-08-31, après les écoles. Elles sont donc restées « non
    /// renseignées », et la règle d'alors était de ne pas leur en inventer un.
    /// Le 2026-09-19, Cheikh a tranché école par école : ce n'est plus une
    /// supposition, c'est une donnée qu'il détient — d'où cette reprise.</para>
    ///
    /// <para><b>Ce que le type commande.</b> Les niveaux de classe proposés
    /// (« CE1 » n'a aucun sens dans un daara, « Halaqa 3 » n'en a aucun dans une
    /// école classique) et, depuis cette reprise, la matière « Coran »
    /// pré-créée : dans un daara on apprend forcément le Coran, dans une école
    /// franco-arabe pas nécessairement — elle la crée elle-même si elle veut.
    /// La production le dit mieux que n'importe quel argument : « iqra Pape
    /// Djiby Diop », franco-arabe, tient <b>20 matières et pas une seule de
    /// Coran</b>.</para>
    ///
    /// <para>🔴 <b>La désignation se fait par IDENTIFIANT, pas par nom.</b> Le nom
    /// dicté n'est pas le nom en base : l'école citée « Iqra Pape <b>Djily</b>
    /// Diop » y figure « iqra Pape <b>Djiby</b> Diop », et deux écoles
    /// <b>distinctes</b> du même propriétaire portent presque le même nom
    /// (« … Cheikh Abdou Khadir Mbacke », inscrites à un jour d'intervalle) —
    /// l'une est un daara, l'autre une franco-arabe. Un appariement sur le nom
    /// se serait trompé, en silence. Le nom ne sert plus qu'à VÉRIFIER qu'on
    /// tient bien la bonne école (voir <see cref="Correspond"/>).</para>
    ///
    /// <para>Publique et pure à dessein : une règle qu'on ne peut vérifier qu'en
    /// démarrant l'API contre la vraie base ne se vérifie jamais (§133/§178).</para>
    /// </summary>
    public static class SchoolTypeBackfill
    {
        /// <summary>
        /// Le type de toute école déjà en base qui n'a pas été désignée
        /// autrement et qui n'a pas choisi le sien.
        /// </summary>
        public const SchoolType Defaut = SchoolType.Daara;

        /// <summary>
        /// Une école désignée nommément : son identifiant en production, son
        /// type, et les mots qui doivent TOUS se retrouver dans son nom pour
        /// confirmer qu'il s'agit bien d'elle.
        /// </summary>
        /// <param name="SchoolId">Identifiant en base de PRODUCTION.</param>
        /// <param name="NomAttendu">Le nom relevé en base — sert au journal.</param>
        /// <param name="Type">Ce que Cheikh a dit de cette école-là.</param>
        /// <param name="Jetons">
        /// Mots distinctifs du nom, déjà pliés (minuscules, sans accent). Choisis
        /// pour survivre à une correction d'orthographe : ce qui varie d'une
        /// graphie à l'autre n'en fait pas partie — d'où « khadim » et « touba »
        /// plutôt que « Chiekh », qui est une faute de frappe de l'école (§267).
        /// </param>
        public sealed record Signature(
            int SchoolId, string NomAttendu, SchoolType Type, params string[] Jetons);

        /// <summary>
        /// Les écoles désignées une par une le 2026-09-19, relevées en
        /// production le même jour. <b>Elles priment sur le type déjà posé</b> —
        /// c'est tout leur objet : Cheikh les a nommées, elles passent avant le
        /// formulaire. Toute école absente de cette liste garde son type si elle
        /// en a un, et prend <see cref="Defaut"/> sinon.
        /// </summary>
        /// <remarks>
        /// ⚠️ Ces identifiants viennent de la base de PRODUCTION. Sur une base de
        /// développement ils n'existent pas : la reprise ne les trouve pas et
        /// classe tout en daara, ce qui est le bon comportement en local. En
        /// revanche, si un identifiant existe sous un nom qui ne correspond PAS,
        /// la reprise s'arrête — la base n'est pas celle qu'on croit.
        /// </remarks>
        public static readonly IReadOnlyList<Signature> Designations = new[]
        {
            // Franco-arabe, et déjà typée comme telle par Cheikh avant la reprise.
            // Conservée ici parce que cette liste est la RÉFÉRENCE de ce qui a été
            // décidé, pas seulement la liste de ce qui restait à écrire.
            new Signature(15, "iqra Pape Djiby Diop", SchoolType.FrancoArabe,
                "iqra", "diop"),

            // ⚠️ Deux écoles DISTINCTES du même propriétaire, inscrites à un jour
            // d'intervalle et aux noms presque identiques. La première (#20,
            // « Centre Islamique Al Imam… ») est un daara — elle prend donc le
            // défaut, sans être nommée ici. La seconde est la franco-arabe : seul
            // « franco » les sépare, et c'est pourquoi ce jeton est présent.
            new Signature(21, "École Franco arabe centre islamique Imam Cheikh Abdou khadir Mbacke",
                SchoolType.FrancoArabe, "franco", "abdou", "khadir"),

            // Typée « Autre » par son directeur, alors que tout dit le daara :
            // Touba, Cheikh Ahmadou Al Khadim, et 5 matières de Coran sur 6.
            // « Autre » lui faisait proposer de mauvais niveaux de classe.
            new Signature(16, "Center Chiekh Ahmadu Al Khadim Touba Keur Sega",
                SchoolType.Daara, "khadim", "touba")
        };

        /// <summary>
        /// La forme sous laquelle un nom d'école se compare : les deux écritures
        /// mises bout à bout et pliées. Le nom arabe n'est pas translittéré — les
        /// jetons sont latins, et une translittération n'ajouterait ici que du
        /// risque.
        /// </summary>
        public static string Normaliser(string? name, string? nameAr)
        {
            var fr = SearchText.Fold(name);
            var ar = SearchText.Fold(nameAr);
            if (fr.Length == 0) return ar;
            if (ar.Length == 0) return fr;
            return fr + " " + ar;
        }

        /// <summary>
        /// Vrai si ce nom porte TOUS les jetons de la signature. Les jetons sont
        /// comparés comme des MOTS entiers : « iqra » ne se reconnaît pas par
        /// accident dans « Iqraa Academy ».
        /// </summary>
        public static bool Correspond(Signature signature, string? name, string? nameAr)
        {
            var mots = Normaliser(name, nameAr)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (mots.Length == 0) return false;

            foreach (var jeton in signature.Jetons)
                if (!mots.Contains(jeton, StringComparer.Ordinal)) return false;

            return true;
        }
    }
}
