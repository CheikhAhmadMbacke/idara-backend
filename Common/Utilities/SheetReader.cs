using System.Text;
using ClosedXML.Excel;

namespace Idara.API.Common.Utilities
{
    /// <summary>Une feuille de calcul ramenée à ce qui nous intéresse : des lignes de texte.</summary>
    public sealed class SheetTable
    {
        /// <summary>En-têtes, dans l'ordre du fichier.</summary>
        public List<string> Headers { get; init; } = new();

        /// <summary>Lignes de données. Chaque liste a la longueur de <see cref="Headers"/>.</summary>
        public List<List<string>> Rows { get; init; } = new();

        /// <summary>
        /// Numéro de la ligne DANS LE FICHIER pour chaque ligne de données (1
        /// pour l'en-tête). C'est ce numéro qu'on montre à l'école : « ligne 34
        /// » doit désigner ce qu'elle voit dans Excel, pas notre index interne.
        /// </summary>
        public List<int> RowNumbers { get; init; } = new();
    }

    /// <summary>
    /// Lit un tableau envoyé par une école, en .xlsx ou en .csv.
    ///
    /// ⚠️ Les DEUX formats sont indispensables : on fournit le modèle en .xlsx,
    /// et un directeur qui l'ouvre puis fait « Enregistrer » nous renvoie du
    /// .xlsx — mais certains exportent en .csv depuis un autre logiciel. Ne
    /// lire qu'un seul format, c'est refuser le fichier de la moitié des
    /// écoles, à l'étape précise où elles nous font confiance.
    /// </summary>
    public static class SheetReader
    {
        public const int MaxRows = 5000;

        /// <summary>Le contenu commence-t-il par la signature d'un fichier ZIP (donc .xlsx) ?</summary>
        public static bool LooksLikeXlsx(byte[] bytes)
            => bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B
               && (bytes[2] == 0x03 || bytes[2] == 0x05 || bytes[2] == 0x07);

        /// <summary>
        /// Lit le tableau, quel que soit le format. On ne se fie PAS à
        /// l'extension du nom de fichier : elle ment souvent (un .csv renommé,
        /// un .xls qui est en réalité du .xlsx). On regarde le contenu.
        /// </summary>
        public static SheetTable Read(byte[] bytes)
            => LooksLikeXlsx(bytes) ? ReadXlsx(bytes) : ReadCsv(DecodeText(bytes));

        /// <summary>
        /// Décode en texte. Le BOM UTF-8 est retiré s'il est là ; sinon on
        /// tente l'UTF-8 strict et on retombe sur Windows-1252, l'encodage que
        /// produit encore Excel en français quand on enregistre en CSV — sans
        /// ce repli, tous les accents et l'arabe deviennent illisibles.
        /// </summary>
        public static string DecodeText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            try
            {
                return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }

        private static SheetTable ReadXlsx(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);

            // La PREMIÈRE feuille fait foi. Le modèle met les données en
            // premier et l'aide ensuite, précisément pour que l'école n'ait pas
            // à choisir.
            var ws = wb.Worksheets.FirstOrDefault()
                     ?? throw new InvalidDataException("Le fichier ne contient aucune feuille.");

            var used = ws.RangeUsed();
            if (used == null) return new SheetTable();

            var table = new SheetTable();
            var firstRow = used.FirstRow().RowNumber();
            var lastRow = used.LastRow().RowNumber();
            var firstCol = used.FirstColumn().ColumnNumber();
            var lastCol = used.LastColumn().ColumnNumber();

            for (int c = firstCol; c <= lastCol; c++)
                table.Headers.Add(ws.Cell(firstRow, c).GetFormattedString().Trim());

            for (int r = firstRow + 1; r <= lastRow && table.Rows.Count < MaxRows; r++)
            {
                var row = new List<string>();
                bool any = false;
                for (int c = firstCol; c <= lastCol; c++)
                {
                    // GetFormattedString : une date ou un nombre doivent arriver
                    // tels que l'école les VOIT, pas en série Excel (45234).
                    var v = ws.Cell(r, c).GetFormattedString().Trim();
                    if (v.Length > 0) any = true;
                    row.Add(v);
                }
                // Une ligne entièrement vide au milieu du fichier est un
                // séparateur visuel, pas une erreur : on la saute en silence.
                if (!any) continue;
                table.Rows.Add(row);
                table.RowNumbers.Add(r);
            }
            return table;
        }

        /// <summary>
        /// Lit un CSV. Le séparateur est DÉTECTÉ (point-virgule, virgule ou
        /// tabulation) : Excel en français écrit des points-virgules, Excel en
        /// anglais des virgules, et imposer l'un des deux ferait échouer la
        /// moitié des envois pour une raison que personne ne comprendrait.
        /// </summary>
        public static SheetTable ReadCsv(string text)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            // Ligne « sep=; » qu'Excel ajoute parfois en tête.
            if (text.StartsWith("sep=", StringComparison.OrdinalIgnoreCase))
            {
                var nl = text.IndexOf('\n');
                text = nl >= 0 ? text[(nl + 1)..] : string.Empty;
            }
            if (text.Trim().Length == 0) return new SheetTable();

            var sep = DetectSeparator(text);
            var table = new SheetTable();
            var rows = ParseCsv(text, sep);
            if (rows.Count == 0) return table;

            table.Headers.AddRange(rows[0].Select(h => h.Trim()));
            for (int i = 1; i < rows.Count && table.Rows.Count < MaxRows; i++)
            {
                var r = rows[i];
                if (r.All(string.IsNullOrWhiteSpace)) continue;
                while (r.Count < table.Headers.Count) r.Add(string.Empty);
                table.Rows.Add(r.Select(v => v.Trim()).ToList());
                table.RowNumbers.Add(i + 1);
            }
            return table;
        }

        private static char DetectSeparator(string text)
        {
            // On regarde la PREMIÈRE ligne seulement : c'est l'en-tête, il n'y a
            // pas encore de texte libre pour fausser le compte.
            var line = text.Split('\n')[0];
            var counts = new[] { ';', ',', '\t' }
                .Select(c => (sep: c, n: line.Count(x => x == c)))
                .OrderByDescending(x => x.n)
                .ToList();
            return counts[0].n > 0 ? counts[0].sep : ';';
        }

        /// <summary>
        /// Analyse CSV avec guillemets : une valeur peut contenir le séparateur
        /// et des retours à la ligne (une adresse, une remarque). Un simple
        /// Split casserait ces lignes en silence.
        /// </summary>
        private static List<List<string>> ParseCsv(string text, char sep)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cur = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cur.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else cur.Append(ch);
                    continue;
                }
                if (ch == '"') { inQuotes = true; continue; }
                if (ch == sep) { row.Add(cur.ToString()); cur.Clear(); continue; }
                if (ch == '\n')
                {
                    row.Add(cur.ToString()); cur.Clear();
                    rows.Add(row); row = new List<string>();
                    continue;
                }
                cur.Append(ch);
            }
            if (cur.Length > 0 || row.Count > 0)
            {
                row.Add(cur.ToString());
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Retrouve l'index d'une colonne d'après plusieurs intitulés possibles.
        ///
        /// La comparaison ignore la casse, les accents, les espaces et la
        /// ponctuation : une école qui écrit « Prénom », « prenom » ou
        /// « Prénom de l'élève » doit être comprise. Refuser un fichier pour un
        /// accent serait exactement le genre de mur qui fait abandonner.
        /// </summary>
        public static int IndexOf(List<string> headers, params string[] candidates)
        {
            var norm = headers.Select(Normalize).ToList();
            foreach (var c in candidates)
            {
                var n = Normalize(c);
                var i = norm.IndexOf(n);
                if (i >= 0) return i;
            }
            // Repli : un en-tête qui CONTIENT le libellé attendu
            // (« prenom de l'eleve » pour « prenom »).
            foreach (var c in candidates)
            {
                var n = Normalize(c);
                if (n.Length < 4) continue;
                var i = norm.FindIndex(h => h.Contains(n, StringComparison.Ordinal));
                if (i >= 0) return i;
            }
            return -1;
        }

        public static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s.ToLowerInvariant().Trim())
            {
                var c = ch switch
                {
                    'à' or 'á' or 'â' or 'ã' or 'ä' or 'å' => 'a',
                    'è' or 'é' or 'ê' or 'ë' => 'e',
                    'ì' or 'í' or 'î' or 'ï' => 'i',
                    'ò' or 'ó' or 'ô' or 'õ' or 'ö' => 'o',
                    'ù' or 'ú' or 'û' or 'ü' => 'u',
                    'ç' => 'c',
                    'ñ' => 'n',
                    _ => ch,
                };
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Valeur d'une colonne, ou chaîne vide si la colonne est absente.</summary>
        public static string Cell(List<string> row, int index)
            => index >= 0 && index < row.Count ? row[index] : string.Empty;
    }
}
