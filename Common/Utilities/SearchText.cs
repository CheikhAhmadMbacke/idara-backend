using System.Globalization;
using System.Text;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Ce qui rend une recherche trouvable — <b>source unique</b> des deux
    /// règles d'or de la recherche dans Idara.
    ///
    /// <para><b>① Un accent ne doit JAMAIS empêcher de trouver.</b> « Mbacke »
    /// et « Mbacké » désignent le même enfant. Personne ne tape « é » sur un
    /// clavier de téléphone, et l'exiger revenait à cacher 54 élèves de la
    /// plateforme à quiconque écrit son nom simplement (mesuré en production le
    /// 2026-09-15 : chercher « cisse » renvoyait <b>zéro</b> résultat, « cissé »
    /// en renvoyait un).</para>
    ///
    /// <para><b>② Un nom écrit en caractères arabes doit se trouver en latin, et
    /// réciproquement.</b> Au Sénégal les noms wolof s'écrivent AUSSI en
    /// orthographe arabe — « Cheikh » et « شيخ » sont le même nom, et un maître
    /// arabophone qui saisit en arabe ne doit pas rendre l'élève introuvable au
    /// secrétariat. Un import photo d'un cahier en ajami en crée des dizaines
    /// d'un coup.</para>
    ///
    /// <para>⚠️ <b>Cette logique existe en double, côté Flutter</b>
    /// (<c>idara/lib/core/utils/search_text.dart</c>), parce que les deux côtés
    /// en ont besoin : le serveur pour les listes paginées, l'application pour
    /// les filtres locaux. Deux copies finissent toujours par diverger — c'est
    /// pourquoi <c>Idara.API/Tools/check-search-parity.js</c> compare les deux
    /// tables et échoue si l'une bouge sans l'autre.</para>
    /// </summary>
    public static class SearchText
    {
        // -----------------------------------------------------------------
        //  ① Le pliage : minuscules, sans accents, sans ponctuation
        // -----------------------------------------------------------------

        /// <summary>
        /// La forme sous laquelle un texte se cherche : minuscules, accents
        /// retirés, ponctuation ramenée à des espaces simples.
        ///
        /// <para>C'est l'équivalent EXACT de <c>unaccent(lower(x))</c> en base
        /// pour l'alphabet latin — ce qui est indispensable : le terme est plié
        /// ici, en C#, et comparé à du SQL plié par PostgreSQL. Si les deux
        /// divergeaient, une recherche ne trouverait rien sans que rien ne le
        /// signale.</para>
        /// </summary>
        public static string Fold(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // Décomposition Unicode : « é » devient « e » + accent aigu, et on
            // jette les accents. Couvre tout l'alphabet latin étendu d'un coup,
            // là où une table de correspondance en oublie toujours un.
            var decomposed = input.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);

            foreach (var ch in decomposed)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat == UnicodeCategory.NonSpacingMark) continue;   // accents, harakat
                sb.Append(ch);
            }

            var plie = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();

            // Ligatures : la décomposition ne les défait pas (œ n'est pas o + e).
            plie = plie.Replace("œ", "oe").Replace("æ", "ae").Replace("ß", "ss")
                       .Replace("ø", "o").Replace("đ", "d").Replace("ł", "l");

            // Ponctuation et espaces : un nom écrit « N'Diaye », « Ndiaye » ou
            // « N Diaye » se cherche pareil.
            //
            // ⚠️ L'APOSTROPHE se supprime, elle ne devient pas un espace — et
            // c'est tout sauf un détail ici : « N'Diaye » et « Ndiaye » sont le
            // même nom, très répandu, et la traiter comme les autres signes
            // donnait « n diaye » face à « ndiaye », donc aucun résultat.
            // Les autres signes (tiret, point, virgule) deviennent bien des
            // espaces : « el-hadji » vaut « el hadji ».
            var outp = new StringBuilder(plie.Length);
            bool espacePrecedent = false;
            foreach (var ch in plie)
            {
                if (ch is '\'' or '’' or '`' or '‘' or '´') continue;

                if (char.IsLetterOrDigit(ch))
                {
                    outp.Append(ch);
                    espacePrecedent = false;
                }
                else if (!espacePrecedent && outp.Length > 0)
                {
                    outp.Append(' ');
                    espacePrecedent = true;
                }
            }
            return outp.ToString().TrimEnd();
        }

        // -----------------------------------------------------------------
        //  ② L'ajami : de l'arabe vers le latin
        // -----------------------------------------------------------------

        /// <summary>
        /// Translittération arabe → latin <b>orientée noms sénégalais</b>, pas
        /// arabe classique.
        ///
        /// <para>Deux choix assumés, qui viennent de l'usage et non des normes
        /// de translittération :</para>
        /// <list type="bullet">
        ///   <item><c>ش</c> → <c>ch</c> et non <c>sh</c> : ici on écrit Cheikh,
        ///   Chérif, Chaanti.</item>
        ///   <item><c>ث</c> → <c>s</c> et non <c>th</c> : عثمان se prononce et
        ///   s'écrit <b>Ousmane</b>. La norme donnerait « Outhmane », que
        ///   personne ne tapera.</item>
        /// </list>
        /// </summary>
        public static readonly IReadOnlyDictionary<char, string> ArabeVersLatin =
            new Dictionary<char, string>
            {
                ['ا'] = "a", ['أ'] = "a", ['إ'] = "i", ['آ'] = "a", ['ٱ'] = "a",
                ['ب'] = "b",
                ['ت'] = "t",
                ['ث'] = "s",          // عثمان -> Ousmane (usage, pas la norme)
                ['ج'] = "j",
                ['ح'] = "h",
                ['خ'] = "kh",
                ['د'] = "d",
                ['ذ'] = "d",
                ['ر'] = "r",
                ['ز'] = "z",
                ['س'] = "s",
                ['ش'] = "ch",         // Cheikh, et non Sheikh
                ['ص'] = "s",
                ['ض'] = "d",
                ['ط'] = "t",
                ['ظ'] = "z",
                ['ع'] = "",           // muet en usage francophone
                ['غ'] = "g",          // دياغن -> Diagne
                ['ف'] = "f",
                ['ق'] = "k",
                ['ك'] = "k",
                ['ل'] = "l",
                ['م'] = "m",
                ['ن'] = "n",
                ['ه'] = "h",
                ['ة'] = "a",
                ['و'] = "w",
                ['ؤ'] = "w",
                ['ي'] = "y",
                ['ى'] = "a",
                ['ئ'] = "y",
                ['ء'] = "",
                ['ٓ'] = "",
                // Chiffres arabes orientaux : un cahier les emploie pour les
                // numéros comme pour les montants.
                ['٠'] = "0", ['١'] = "1", ['٢'] = "2", ['٣'] = "3", ['٤'] = "4",
                ['٥'] = "5", ['٦'] = "6", ['٧'] = "7", ['٨'] = "8", ['٩'] = "9",
                ['۰'] = "0", ['۱'] = "1", ['۲'] = "2", ['۳'] = "3", ['۴'] = "4",
                ['۵'] = "5", ['۶'] = "6", ['۷'] = "7", ['۸'] = "8", ['۹'] = "9",
            };

        /// <summary>Le texte contient-il au moins une lettre arabe ?</summary>
        public static bool ContientArabe(string? input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            foreach (var ch in input)
                if (ch >= 'ؠ' && ch <= 'ي') return true;   // lettres, hors harakat
            return false;
        }

        /// <summary>
        /// Rend une chaîne arabe en lettres latines. Le texte déjà latin
        /// ressort inchangé (plié), ce qui permet d'appeler cette méthode sans
        /// se demander dans quelle graphie on est.
        /// </summary>
        public static string Translitterer(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var sb = new StringBuilder(input.Length * 2);
            foreach (var ch in input)
            {
                if (ArabeVersLatin.TryGetValue(ch, out var latin)) sb.Append(latin);
                else if (ch >= 'ً' && ch <= 'ْ') continue;   // harakat : ignorées
                else if (ch == 'ـ') continue;                     // tatweel (étirement)
                else sb.Append(ch);
            }
            return Fold(sb.ToString());
        }

        // -----------------------------------------------------------------
        //  ③ Le squelette : ce qui fait qu'un nom en vaut un autre
        // -----------------------------------------------------------------

        // Représentations internes des digrammes, le temps du calcul : « ch » et
        // « kh » valent UNE consonne, et doivent survivre à la suppression des
        // voyelles comme à la réduction des doublons sans être pris pour deux.
        // Des MAJUSCULES font l'affaire : Fold() a déjà tout ramené en
        // minuscules, donc aucun nom ne peut en contenir — et contrairement à un
        // caractère de contrôle, le marqueur reste lisible dans le code.
        private const string CH = "C";   // « ch » / « sh » / ش
        private const string KH = "K";   // « kh » / خ

        /// <summary>
        /// Le squelette consonantique d'un nom, dans l'une ou l'autre graphie.
        ///
        /// <para>C'est ce qui fait se rencontrer « Cheikh » et « شيخ » : l'arabe
        /// est un alphabet consonantique, il n'écrit pas les voyelles courtes.
        /// On retire donc les voyelles des DEUX côtés et il reste le même
        /// squelette — <c>chkh</c>.</para>
        ///
        /// <para>Exemples vérifiés par les tests : شيخ / Cheikh → <c>chkh</c> ·
        /// امباكي / Mbacké → <c>mbk</c> · ديالو / Diallo → <c>dl</c> ·
        /// سيسي / Cissé → <c>s</c> · عثمان / Ousmane → <c>smn</c>.</para>
        ///
        /// <para>⚠️ Volontairement <b>grossier</b> : il rapproche plus qu'il ne
        /// distingue, et « Sow » comme « Sy » donnent <c>s</c>. C'est le bon
        /// arbitrage — sur une liste d'école, cinq résultats à parcourir valent
        /// mieux qu'un enfant introuvable. Il ne remplace jamais la recherche
        /// exacte : il s'y AJOUTE.</para>
        /// </summary>
        public static string Squelette(string? input)
        {
            var s = ContientArabe(input) ? Translitterer(input) : Fold(input);
            if (s.Length == 0) return string.Empty;

            // 1) Digrammes d'abord : « ch », « sh » et « kh » sont une seule
            // consonne, et « ck », « ph », « qu »… se ramènent à leur son.
            s = s.Replace("sch", CH).Replace("ch", CH).Replace("sh", CH)
                 .Replace("kh", KH)
                 .Replace("ck", "k").Replace("qu", "k").Replace("q", "k")
                 .Replace("ph", "f").Replace("th", "t").Replace("gu", "g")
                 .Replace("x", "ks");

            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                var ch = s[i];

                // 2) « c » se prononce s devant e/i/y, k ailleurs.
                if (ch == 'c')
                {
                    var suivant = i + 1 < s.Length ? s[i + 1] : ' ';
                    ch = suivant is 'e' or 'i' or 'y' ? 's' : 'k';
                }

                // 3) L'espace sépare les mots, et il RESTE : sans lui
                // « chkh mbk » devient « chkhmbk », et chercher « mbacke »
                // seul ne trouve plus rien.
                if (ch == ' ')
                {
                    if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                    continue;
                }

                // 4) Voyelles et semi-voyelles : supprimées. C'est le cœur du
                // procédé — l'ajami ne les écrit pas, le latin les écrit toutes.
                if (ch is 'a' or 'e' or 'i' or 'o' or 'u' or 'y' or 'w') continue;

                // 5) Consonne doublée = une seule (Diallo -> dl, سيسي -> s).
                // Les CHIFFRES en sont exclus : dans « 77 630 », les deux 7
                // comptent, et les réduire fabriquerait un faux numéro.
                if (sb.Length > 0 && sb[^1] == ch && !char.IsDigit(ch)) continue;

                sb.Append(ch);
            }

            return sb.ToString().Trim().Replace(CH, "ch").Replace(KH, "kh");
        }

        // -----------------------------------------------------------------
        //  ④ Ce qui est rangé en base, et ce qu'on y cherche
        // -----------------------------------------------------------------

        /// <summary>
        /// L'index de recherche d'une personne : tout ce sous quoi elle doit
        /// pouvoir être trouvée, en une seule chaîne.
        ///
        /// <para>Trois formes y cohabitent, séparées par des espaces : le nom
        /// <b>plié</b> (accents retirés), sa <b>translittération</b> latine s'il
        /// est en arabe, et son <b>squelette</b>. Une seule colonne, une seule
        /// comparaison <c>LIKE</c> — et donc aucune recherche à réécrire quand
        /// on ajoutera une forme.</para>
        /// </summary>
        public static string IndexPourPersonne(params string?[] parties)
        {
            var formes = new List<string>();
            var vus = new HashSet<string>(StringComparer.Ordinal);

            void Ajouter(string? v)
            {
                if (string.IsNullOrWhiteSpace(v)) return;
                if (vus.Add(v)) formes.Add(v);
            }

            // Les parties séparées (prénom, nom), puis leur assemblage : on
            // cherche aussi bien « Kâ » que « Serigne Modou Kâ ».
            //
            // Inutile de découper en mots : la recherche est un LIKE « %terme% »,
            // et « srgn md » contient déjà « srgn » comme « md ». Les ajouter
            // séparément ne faisait que doubler la taille de l'index.
            var entier = string.Join(' ', parties.Where(p => !string.IsNullOrWhiteSpace(p)));
            foreach (var partie in parties.Append(entier))
            {
                if (string.IsNullOrWhiteSpace(partie)) continue;
                Ajouter(Fold(partie));
                if (ContientArabe(partie)) Ajouter(Translitterer(partie));
                Ajouter(Squelette(partie));
            }

            // Un espace de tête et de queue, et ce n'est pas cosmétique : le
            // squelette se compare par MOT ENTIER (« % f % »), sinon « faye »
            // — squelette « f » — ramène « Fall » — squelette « fl » — parce
            // que f est une sous-chaîne de fl. Sans ces deux espaces, un mot en
            // début ou en fin d'index n'aurait pas de délimiteur à gauche ou à
            // droite et deviendrait introuvable.
            return " " + string.Join(' ', formes) + " ";
        }

        /// <summary>
        /// Les motifs <c>LIKE</c> avec lesquels interroger un index produit par
        /// <see cref="IndexPourPersonne"/>. Un seul d'entre eux suffit à
        /// considérer que la personne correspond.
        ///
        /// <para>Deux régimes, et c'est délibéré :</para>
        /// <list type="bullet">
        ///   <item>Le terme <b>plié</b> est cherché en <b>partiel</b>
        ///   (<c>%mba%</c>) : on tape les premières lettres d'un nom, on ne le
        ///   tape pas en entier.</item>
        ///   <item>Le <b>squelette</b> est cherché en <b>mot entier</b>
        ///   (<c>% f %</c>) : en partiel, il rapproche n'importe quoi.</item>
        /// </list>
        /// </summary>
        public static List<string> MotifsPourIndex(string? terme)
        {
            var motifs = new List<string>();
            var plie = Fold(terme);
            if (plie.Length == 0) return motifs;

            motifs.Add($"%{plie}%");

            if (ContientArabe(terme))
            {
                var translit = Translitterer(terme);
                if (translit.Length > 0 && translit != plie) motifs.Add($"%{translit}%");
            }

            var seuil = ContientArabe(terme) ? 2 : 3;
            if (plie.Replace(" ", "").Length < seuil) return motifs;

            var squelette = Squelette(terme);
            if (squelette.Length > 0) motifs.Add($"% {squelette} %");

            return motifs;
        }

        /// <summary>
        /// Filtrage EN MÉMOIRE, pour les listes qui ne passent pas par la base.
        /// Applique exactement les règles de <see cref="MotifsPourIndex"/> —
        /// deux règles différentes selon l'écran seraient pires que pas de
        /// règle du tout.
        /// </summary>
        public static bool Correspond(string? index, string? terme)
        {
            if (string.IsNullOrEmpty(index)) return false;
            foreach (var motif in MotifsPourIndex(terme))
            {
                // « %x% » -> sous-chaîne ; « % x % » -> mot entier (l'index
                // porte déjà ses espaces de délimitation).
                var noyau = motif.Trim('%');
                if (index.Contains(noyau, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Les formes sous lesquelles chercher ce que l'utilisateur a tapé :
        /// le terme plié, et son squelette s'il en diffère.
        ///
        /// <para>Toujours interroger avec le terme plié EN PREMIER : c'est lui
        /// qui donne les résultats exacts. Le squelette ne fait qu'élargir.</para>
        /// </summary>
        public static List<string> FormesDeRecherche(string? terme)
        {
            var formes = new List<string>();
            var plie = Fold(terme);
            if (plie.Length == 0) return formes;
            formes.Add(plie);

            if (ContientArabe(terme))
            {
                var translit = Translitterer(terme);
                if (translit.Length > 0 && translit != plie) formes.Add(translit);
            }

            // Le garde-fou porte sur ce que l'utilisateur a TAPÉ, pas sur la
            // longueur du squelette : « Sow » se réduit à « s », et refuser un
            // squelette d'une lettre rendait صو introuvable — alors que le
            // terme, lui, était parfaitement précis. En dessous de trois
            // caractères saisis, la recherche est de toute façon trop vague
            // pour qu'on l'élargisse encore.
            // Deux caractères suffisent en ARABE : l'écriture est
            // consonantique, « صو » (Sow) est un nom entier là où deux lettres
            // latines ne sont qu'un début de mot.
            var seuil = ContientArabe(terme) ? 2 : 3;
            if (plie.Replace(" ", "").Length < seuil) return formes;

            var squelette = Squelette(terme);
            if (squelette.Length > 0 && !formes.Contains(squelette)) formes.Add(squelette);

            return formes;
        }
    }
}
