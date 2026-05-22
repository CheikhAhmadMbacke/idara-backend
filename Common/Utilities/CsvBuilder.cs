using System.Globalization;
using System.Text;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Petit builder CSV minimaliste mais correct (échappement RFC 4180,
    /// BOM UTF-8 pour qu'Excel détecte l'encodage).
    /// </summary>
    public class CsvBuilder
    {
        private readonly StringBuilder _sb = new();
        private readonly char _separator;

        // Excel français/arabe attend ; comme séparateur. Configurable au cas où.
        public CsvBuilder(char separator = ';')
        {
            _separator = separator;
        }

        public CsvBuilder AppendRow(params object?[] cells)
        {
            for (var i = 0; i < cells.Length; i++)
            {
                if (i > 0) _sb.Append(_separator);
                _sb.Append(Escape(cells[i]));
            }
            _sb.Append('\n');
            return this;
        }

        /// <summary>
        /// Retourne le CSV avec BOM UTF-8 — sans cela, Excel ouvre les fichiers
        /// contenant des accents/arabe en cassant l'encodage.
        /// </summary>
        public byte[] ToBytes()
        {
            var bom = Encoding.UTF8.GetPreamble();
            var content = Encoding.UTF8.GetBytes(_sb.ToString());
            var output = new byte[bom.Length + content.Length];
            Buffer.BlockCopy(bom, 0, output, 0, bom.Length);
            Buffer.BlockCopy(content, 0, output, bom.Length, content.Length);
            return output;
        }

        private string Escape(object? value)
        {
            if (value == null) return string.Empty;

            string raw = value switch
            {
                DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                double d => d.ToString("0.##", CultureInfo.InvariantCulture),
                float f => f.ToString("0.##", CultureInfo.InvariantCulture),
                bool b => b ? "1" : "0",
                _ => value.ToString() ?? string.Empty,
            };

            // Échappement RFC 4180 : si la cellule contient le séparateur, des
            // guillemets ou un saut de ligne, on encadre de "" et on double les ".
            var needsQuotes = raw.Contains(_separator) || raw.Contains('"') ||
                              raw.Contains('\n') || raw.Contains('\r');
            if (!needsQuotes) return raw;
            return $"\"{raw.Replace("\"", "\"\"")}\"";
        }
    }
}
