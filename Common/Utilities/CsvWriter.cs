using System.Globalization;
using System.Text;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Petit générateur CSV (RFC 4180) sans dépendance externe. UTF-8 BOM pour
    /// qu'Excel reconnaisse les accents/arabe en double-clic, séparateur virgule,
    /// guillemets doublés à l'intérieur d'une cellule, retour ligne CRLF.
    /// </summary>
    public class CsvWriter
    {
        private readonly StringBuilder _sb = new();

        public CsvWriter(IEnumerable<string> header)
        {
            WriteRow(header);
        }

        public void WriteRow(IEnumerable<object?> cells)
        {
            var first = true;
            foreach (var cell in cells)
            {
                if (!first) _sb.Append(',');
                first = false;
                _sb.Append(Escape(cell));
            }
            _sb.Append("\r\n");
        }

        public byte[] ToBytes()
        {
            // BOM UTF-8 pour les compat Excel sous Windows.
            var preamble = Encoding.UTF8.GetPreamble();
            var content = Encoding.UTF8.GetBytes(_sb.ToString());
            var combined = new byte[preamble.Length + content.Length];
            Buffer.BlockCopy(preamble, 0, combined, 0, preamble.Length);
            Buffer.BlockCopy(content, 0, combined, preamble.Length, content.Length);
            return combined;
        }

        private static string Escape(object? value)
        {
            if (value == null) return string.Empty;

            string str = value switch
            {
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                double d => d.ToString("0.##", CultureInfo.InvariantCulture),
                float f => f.ToString("0.##", CultureInfo.InvariantCulture),
                bool b => b ? "1" : "0",
                _ => value.ToString() ?? string.Empty
            };

            // Quoting requis si la cellule contient une virgule, des guillemets,
            // ou un saut de ligne. Plus simple : on quote toujours les chaînes.
            var needsQuote = str.Contains(',') || str.Contains('"') ||
                             str.Contains('\n') || str.Contains('\r');
            if (!needsQuote) return str;

            return $"\"{str.Replace("\"", "\"\"")}\"";
        }
    }
}
