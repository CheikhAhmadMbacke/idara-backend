namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Décodage + validation taille / MIME (magic-bytes) d'un payload base64.
    /// Mutualisé entre StudentService (photo/documents élève) et AuthController (KYC).
    /// </summary>
    public static class FileUploadValidator
    {
        public record Decoded(byte[] Bytes, string Extension, string ContentType);

        public static Decoded? DecodeAndValidate(
            string? base64Input,
            int maxSizeMb,
            string[] allowedMimeTypes,
            string? declaredContentType = null,
            string? originalFileName = null)
        {
            if (string.IsNullOrWhiteSpace(base64Input)) return null;

            string? mimeFromHeader = null;
            string base64Data;

            // Format possible : "data:image/png;base64,iVBOR..." OU base64 brut.
            if (base64Input.StartsWith("data:", System.StringComparison.OrdinalIgnoreCase))
            {
                var commaIdx = base64Input.IndexOf(',');
                if (commaIdx <= 5) return null;
                var header = base64Input.Substring(5, commaIdx - 5);
                var semi = header.IndexOf(';');
                mimeFromHeader = (semi > 0 ? header[..semi] : header).Trim().ToLowerInvariant();
                base64Data = base64Input[(commaIdx + 1)..];
            }
            else
            {
                base64Data = base64Input;
            }

            byte[] bytes;
            try { bytes = System.Convert.FromBase64String(base64Data); }
            catch (System.FormatException) { return null; }

            var maxBytes = (long)maxSizeMb * 1024 * 1024;
            if (bytes.LongLength == 0 || bytes.LongLength > maxBytes) return null;

            var ext = string.IsNullOrWhiteSpace(originalFileName)
                ? null
                : System.IO.Path.GetExtension(originalFileName)?.ToLowerInvariant();

            var contentType = mimeFromHeader
                ?? (string.IsNullOrWhiteSpace(declaredContentType) ? null : declaredContentType.Trim().ToLowerInvariant())
                ?? ExtensionToMime(ext)
                ?? SniffMimeFromBytes(bytes)
                ?? "application/octet-stream";

            if (!System.Linq.Enumerable.Contains(allowedMimeTypes, contentType, System.StringComparer.OrdinalIgnoreCase))
                return null;

            // 🔴 C'est le CONTENU qui décide, jamais la déclaration du client.
            //
            // Le défaut corrigé le 2026-09-04 : on ne rejetait que si la
            // signature était reconnue ET contredisait le type annoncé. Un
            // contenu SANS signature — donc n'importe quoi — passait dès lors
            // que le client déclarait « image/png », et atterrissait dans
            // /uploads/, servi publiquement par nginx sans authentification
            // (§122). Le fichier n'était alors pas ce qu'il prétendait être :
            // au mieux une image cassée sur tous les reçus et en-têtes de
            // l'école, sans que rien ne dise pourquoi.
            //
            // Les cinq types acceptés (JPEG, PNG, GIF, WEBP, PDF) portent TOUS
            // une signature universelle : exiger qu'elle soit reconnue ne
            // refuse aucun fichier légitime.
            var sniffed = SniffMimeFromBytes(bytes);
            if (sniffed == null) return null;
            if (!MimeMatchesFamily(contentType, sniffed)) return null;

            var finalExt = MimeToExtension(contentType) ?? ext ?? ".bin";
            return new Decoded(bytes, finalExt, contentType);
        }

        private static string? ExtensionToMime(string? ext) => ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => null
        };

        private static string? MimeToExtension(string mime) => mime.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "application/pdf" => ".pdf",
            _ => null
        };

        private static bool MimeMatchesFamily(string declared, string sniffed)
        {
            declared = declared.ToLowerInvariant();
            sniffed = sniffed.ToLowerInvariant();
            if (declared == sniffed) return true;
            if ((declared == "image/jpeg" || declared == "image/jpg") &&
                (sniffed == "image/jpeg" || sniffed == "image/jpg")) return true;
            return false;
        }

        public static string? SniffMimeFromBytes(byte[] bytes)
        {
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "image/jpeg";
            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return "image/png";
            if (bytes.Length >= 4 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
                return "image/gif";
            if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return "image/webp";
            if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
                return "application/pdf";
            return null;
        }
    }
}
