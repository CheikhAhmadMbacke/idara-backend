using System.Reflection;
using System.Text.RegularExpressions;

namespace Idara.API.Services.Assistant
{
    /// <summary>
    /// Le guide pas à pas de l'application, avec les libellés EXACTS de l'écran
    /// dans la langue de l'utilisateur (§300).
    ///
    /// <para>Généré par <c>Tools/build-assistant-guide.js</c> depuis les
    /// fichiers de traduction de l'application, et embarqué dans l'assemblage.
    /// Un libellé renommé dans l'application rend le guide PÉRIMÉ, et le mode
    /// <c>--check</c> le refuse : le guide ne peut pas dire « cliquez sur
    /// Ajouter » à un écran qui dit « Nouvel élève ».</para>
    /// </summary>
    public static class AssistantGuide
    {
        private static readonly Lazy<Dictionary<string, (string Intro, Dictionary<string, string> Topics)>> Guides =
            new(() => new()
            {
                ["fr"] = Parse(Load("AssistantGuide.fr.md")),
                ["ar"] = Parse(Load("AssistantGuide.ar.md")),
            });

        /// <summary>Sujets disponibles, dans l'ordre du guide.</summary>
        public static IReadOnlyList<string> Topics => Guides.Value["fr"].Topics.Keys.ToList();

        /// <summary>L'introduction (onglets, conventions) suivie du sujet demandé.</summary>
        public static string? For(string topic, string lang)
        {
            var g = Guides.Value[lang == "ar" ? "ar" : "fr"];
            return g.Topics.TryGetValue(topic, out var body) ? g.Intro + "\n\n" + body : null;
        }

        private static string Load(string name)
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Ressource {name} absente : guide de l'assistant non embarqué.");
            using var r = new StreamReader(s);
            return r.ReadToEnd().Replace("\r\n", "\n");
        }

        private static (string, Dictionary<string, string>) Parse(string md)
        {
            md = Regex.Replace(md, @"^<!--.*?-->\n", "", RegexOptions.Singleline);
            var parts = Regex.Split(md, @"^## ", RegexOptions.Multiline);
            var topics = new Dictionary<string, string>();
            foreach (var p in parts.Skip(1))
            {
                var nl = p.IndexOf('\n');
                topics[p[..nl].Trim()] = "## " + p.Trim();
            }
            return (parts[0].Trim(), topics);
        }
    }
}
