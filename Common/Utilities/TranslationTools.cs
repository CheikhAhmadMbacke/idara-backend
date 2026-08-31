using System.Text.Json;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Règles de l'outil de relecture des traductions. Publiques et PURES : ce
    /// sont elles qui décident du texte qui finira dans l'application, et une
    /// règle qu'on ne peut vérifier qu'en montant une base ne se vérifie
    /// jamais (même principe qu'au §133).
    /// </summary>
    public static class TranslationTools
    {
        /// <summary>Langues ouvertes à la relecture. Le français est la source.</summary>
        public static readonly string[] SupportedLangs = { "ar", "wo" };

        public static bool IsSupportedLang(string? lang)
            => lang is not null && Array.Exists(SupportedLangs, l => l == lang);

        /// <summary>
        /// Section d'une clé : ce qui précède le premier point (« payment.fees »
        /// → « payment »). Sert à découper 1660 clés en lots relisibles.
        /// Une clé sans point tombe dans « divers » plutôt que dans une section
        /// portant son nom entier, qui n'aurait qu'un seul membre.
        /// </summary>
        public static string SectionOf(string key)
        {
            var i = key.IndexOf('.');
            if (i <= 0) return "divers";
            return key[..i];
        }

        /// <summary>
        /// Aplatit un JSON de traductions en paires clé → texte.
        ///
        /// Le format d'Idara est plat (« payment.fees »: "…"), mais on accepte
        /// aussi l'imbriqué : easy_localization tolère les deux, et un fichier
        /// réimporté après passage dans un autre outil peut revenir imbriqué.
        ///
        /// ⚠️ Les objets de PLURALISATION ({zero, one, other}) sont conservés
        /// tels quels, sérialisés : les aplatir en une chaîne ferait afficher
        /// la clé brute à l'utilisateur (piège déjà rencontré au §155).
        /// </summary>
        public static Dictionary<string, string> Flatten(string json)
        {
            var outp = new Dictionary<string, string>(StringComparer.Ordinal);
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement, "", outp);
            return outp;
        }

        private static readonly HashSet<string> PluralKeys =
            new(StringComparer.OrdinalIgnoreCase) { "zero", "one", "two", "few", "many", "other" };

        private static void Walk(JsonElement el, string prefix, Dictionary<string, string> outp)
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                if (prefix.Length > 0)
                    outp[prefix] = el.ValueKind == JsonValueKind.String
                        ? el.GetString() ?? string.Empty
                        : el.GetRawText();
                return;
            }

            // Un objet dont TOUTES les propriétés sont des formes plurielles est
            // une valeur, pas un niveau d'imbrication.
            var isPlural = prefix.Length > 0;
            foreach (var p in el.EnumerateObject())
            {
                if (!PluralKeys.Contains(p.Name)) { isPlural = false; break; }
            }
            if (isPlural && el.EnumerateObject().Any())
            {
                outp[prefix] = el.GetRawText();
                return;
            }

            foreach (var p in el.EnumerateObject())
            {
                var key = prefix.Length == 0 ? p.Name : $"{prefix}.{p.Name}";
                Walk(p.Value, key, outp);
            }
        }

        /// <summary>
        /// Reconstruit un JSON plat, trié comme la liste fournie — donc dans le
        /// même ordre que le fichier d'origine, pour que le diff se lise.
        /// </summary>
        public static string ToJson(IEnumerable<KeyValuePair<string, string>> entries)
        {
            var opts = new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, opts))
            {
                w.WriteStartObject();
                foreach (var (k, v) in entries)
                {
                    // Une valeur de pluralisation a été stockée sérialisée : on
                    // la réécrit comme un objet, pas comme une chaîne.
                    var t = v.TrimStart();
                    if (t.StartsWith('{') && LooksLikeJsonObject(v))
                    {
                        w.WritePropertyName(k);
                        using var d = JsonDocument.Parse(v);
                        d.RootElement.WriteTo(w);
                    }
                    else
                    {
                        w.WriteString(k, v);
                    }
                }
                w.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }

        private static bool LooksLikeJsonObject(string s)
        {
            try
            {
                using var d = JsonDocument.Parse(s);
                return d.RootElement.ValueKind == JsonValueKind.Object;
            }
            catch { return false; }
        }

        /// <summary>
        /// Texte à retenir pour une clé : la proposition si elle existe et n'est
        /// pas vide, sinon la valeur actuelle.
        ///
        /// ⚠️ Une proposition VIDE vaut « je retire ma proposition », jamais
        /// « le libellé doit être vide » : l'appliquer ferait disparaître un
        /// texte de l'application, et l'utilisateur verrait la clé brute.
        /// </summary>
        public static string Resolve(string? current, string? proposal)
            => string.IsNullOrWhiteSpace(proposal) ? (current ?? string.Empty) : proposal;

        /// <summary>
        /// Une proposition est « caduque » quand le texte sur lequel le
        /// relecteur s'est appuyé a changé depuis. Elle reste visible et
        /// applicable — c'est un signal, pas un rejet.
        /// </summary>
        public static bool IsStale(string? basedOn, string? currentReference)
            => basedOn is not null && !string.Equals(basedOn, currentReference ?? string.Empty, StringComparison.Ordinal);
    }
}
