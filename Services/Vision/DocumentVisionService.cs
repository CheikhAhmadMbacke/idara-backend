using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Idara.API.Common.Utilities;
using Idara.API.Enums;
using Idara.API.Options;
using Microsoft.Extensions.Options;

namespace Idara.API.Services.Vision
{
    /// <inheritdoc cref="IDocumentVisionService"/>
    public class DocumentVisionService : IDocumentVisionService
    {
        /// <summary>
        /// Pages envoyées par appel. Ce découpage est possible — et gratuit —
        /// parce que <b>c'est NOUS qui dictons les colonnes</b> : le modèle n'a
        /// pas besoin de voir la page 1 pour connaître la disposition. Sans
        /// cela, il aurait fallu tout envoyer d'un bloc, et une école de
        /// 40 pages aurait dépassé le plafond de tokens en produisant une
        /// réponse tronquée — inexploitable, et payée.
        /// </summary>
        private const int PagesPerCall = 4;

        /// <summary>
        /// Lots menés de front. Les lots sont indépendants — c'est NOUS qui
        /// dictons les colonnes, donc aucun lot n'a besoin du précédent — et
        /// c'est ce qui rend le PDF utilisable : un cahier scanné de 40 pages
        /// fait dix appels, soit sept minutes en file indienne (au-delà de ce
        /// qu'un téléphone accepte d'attendre) contre deux à trois ici.
        ///
        /// <para>Trois, et pas plus : chaque appel en vol est de l'argent engagé
        /// que le garde-fou a déjà autorisé mais pas encore vu dépenser, et un
        /// parallélisme large exposerait aux limitations de débit du fournisseur
        /// — dont le remède serait de réessayer, donc de payer deux fois.</para>
        /// </summary>
        private const int MaxConcurrentCalls = 3;

        private readonly VisionSettings _settings;
        private readonly ILogger<DocumentVisionService> _logger;
        private readonly Lazy<AnthropicClient?> _client;

        public DocumentVisionService(
            IOptions<VisionSettings> settings, ILogger<DocumentVisionService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
            // Construction paresseuse : une instance sans clé (dev, banc d'essai)
            // ne doit pas échouer au démarrage — elle doit juste ne rien offrir.
            _client = new Lazy<AnthropicClient?>(() => _settings.IsConfigured
                ? new AnthropicClient { ApiKey = _settings.ApiKey }
                : null);
        }

        public bool IsConfigured => _settings.IsConfigured;

        public async Task<VisionReadResult> ReadAsync(
            IReadOnlyList<VisionPage> pages, ImportKind kind, CancellationToken ct = default)
        {
            var client = _client.Value ?? throw new InvalidOperationException(
                "La lecture d'un cahier n'est pas disponible sur cette installation. "
                + "Utilisez le fichier Excel.");
            if (pages.Count == 0)
                throw new InvalidOperationException("Aucune page à lire.");

            var columns = ImportColumns.For(kind);
            var sw = Stopwatch.StartNew();

            // Les lots sont menés de front mais RANGÉS PAR INDICE : l'ordre des
            // élèves dans l'aperçu doit être celui du cahier, pas celui dans
            // lequel le fournisseur a répondu. Une école qui relit son aperçu
            // suit son cahier des yeux, ligne à ligne.
            var chunks = new List<(int Index, int Offset, List<VisionPage> Pages)>();
            for (int offset = 0; offset < pages.Count; offset += PagesPerCall)
                chunks.Add((chunks.Count, offset, pages.Skip(offset).Take(PagesPerCall).ToList()));

            var results = new (List<(List<string> Cells, List<int> Uncertain)> Rows, int In, int Out)[chunks.Count];
            using var gate = new SemaphoreSlim(MaxConcurrentCalls);

            await Task.WhenAll(chunks.Select(async c =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var (rows, _, i, o) = await ReadChunkAsync(client, c.Pages, kind, columns, c.Offset, ct);
                    results[c.Index] = (rows, i, o);
                }
                finally { gate.Release(); }
            }));

            var table = new SheetTable();
            table.Headers.AddRange(columns);
            var uncertain = new List<(int, int)>();
            int inTok = 0, outTok = 0;

            foreach (var (rows, i, o) in results)
            {
                inTok += i;
                outTok += o;

                foreach (var (cells, flagged) in rows)
                {
                    var rowIndex = table.Rows.Count;
                    // Normalise la longueur : le parseur indexe par colonne, une
                    // ligne trop courte lèverait, une ligne trop longue mentirait.
                    var normalized = new List<string>(columns.Length);
                    for (int c = 0; c < columns.Length; c++)
                        normalized.Add(c < cells.Count ? (cells[c] ?? string.Empty).Trim() : string.Empty);

                    table.Rows.Add(normalized);
                    // Numéro affiché à l'école : « photo 2, ligne 7 » n'existe
                    // pas dans son cahier, mais un numéro d'ordre continu lui
                    // permet au moins de retrouver la ligne dans l'aperçu.
                    table.RowNumbers.Add(rowIndex + 1);

                    foreach (var c in flagged)
                        if (c >= 0 && c < columns.Length)
                            uncertain.Add((rowIndex, c));
                }
            }

            sw.Stop();
            _logger.LogInformation(
                "[vision] {Pages} page(s) lues en {Ms} ms ({Chunks} appel(s)) : {Rows} ligne(s), "
                + "{Unc} cellule(s) douteuse(s), {In} tokens entrée / {Out} sortie",
                pages.Count, sw.ElapsedMilliseconds, chunks.Count, table.Rows.Count,
                uncertain.Count, inTok, outTok);

            return new VisionReadResult(
                table, uncertain, inTok, outTok, _settings.Model, (int)sw.ElapsedMilliseconds);
        }

        // ---------------------------------------------------------------

        private async Task<(List<(List<string> Cells, List<int> Uncertain)> Rows,
                            List<int> _, int InputTokens, int OutputTokens)>
            ReadChunkAsync(
                AnthropicClient client, List<VisionPage> chunk, ImportKind kind,
                string[] columns, int pageOffset, CancellationToken ct)
        {
            var content = new List<ContentBlockParam>();
            foreach (var page in chunk)
            {
                if (page.IsPdf)
                {
                    // Une page de PDF part en bloc « document », pas en image :
                    // l'API en extrait à la fois le rendu visuel ET le texte
                    // quand il y en a. Sur un ancien cahier tapé puis exporté en
                    // PDF, c'est ce texte qui donne des numéros de téléphone
                    // exacts là où une photo laisserait un doute.
                    content.Add(new DocumentBlockParam
                    {
                        Source = new Base64PdfSource { Data = Convert.ToBase64String(page.Bytes) },
                    });
                }
                else
                {
                    content.Add(new ImageBlockParam
                    {
                        Source = new Base64ImageSource
                        {
                            Data = Convert.ToBase64String(page.Bytes),
                            MediaType = MediaTypeOf(page.MediaType),
                        },
                    });
                }
            }
            content.Add(new TextBlockParam { Text = UserPrompt(chunk.Count, pageOffset) });

            var parameters = new MessageCreateParams
            {
                Model = _settings.Model,
                MaxTokens = _settings.MaxTokens,
                System = SystemPrompt(kind, columns),
                Thinking = new ThinkingConfigAdaptive(),
                OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = Schema() } },
                Messages = [new MessageParam { Role = Role.User, Content = content }],
            };

            var response = await client.Messages.Create(parameters, ct);

            var text = string.Concat(response.Content
                .Select(b => b.Value)
                .OfType<TextBlock>()
                .Select(t => t.Text));

            var rows = ParseRows(text, columns.Length);
            return (rows, new List<int>(),
                (int)(response.Usage?.InputTokens ?? 0),
                (int)(response.Usage?.OutputTokens ?? 0));
        }

        /// <summary>
        /// Le schéma de sortie. Il garantit que la réponse est TOUJOURS
        /// analysable : sans lui, une phrase d'introduction polie suffirait à
        /// faire échouer la lecture d'un import de 300 élèves déjà payé.
        /// </summary>
        private static Dictionary<string, JsonElement> Schema() => new()
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "rows" }),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                rows = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "cells" },
                        properties = new
                        {
                            cells = new
                            {
                                type = "array",
                                items = new { type = "string" },
                                description = "Une valeur par colonne, dans l'ordre donné. Chaîne vide si absent.",
                            },
                            uncertain = new
                            {
                                type = "array",
                                items = new { type = "integer" },
                                description = "Index (0-based) des cellules dont tu n'es pas sûr.",
                            },
                        },
                    },
                },
            }),
        };

        private static string SystemPrompt(ImportKind kind, string[] columns)
        {
            var what = kind == ImportKind.Staff
                ? "la liste du personnel (enseignants, surveillants, agents)"
                : "la liste des élèves";

            var numbered = string.Join("\n",
                columns.Select((c, i) => $"  {i}. {c}"));

            return $"""
                Tu transcris {what} d'une école coranique ou franco-arabe au Sénégal,
                photographiée ou numérisée depuis un cahier, un registre papier ou un
                ancien document. Le cahier peut être manuscrit, en français, en arabe, ou
                les deux mélangés. Les noms sont sénégalais (wolof, peul, arabe).

                Rends UNE LIGNE par personne, avec exactement {columns.Length} cellules
                dans cet ordre :
                {numbered}

                {ImportColumns.Guidance(kind)}

                RÈGLES ABSOLUES :
                1. TRANSCRIS, n'interprète pas. Recopie ce qui est écrit, même si cela
                   te paraît fautif ou incomplet. Tu n'es pas là pour corriger le cahier.
                2. N'INVENTE JAMAIS une valeur. Une case vide dans le cahier reste une
                   chaîne vide. Ne complète pas un prénom, ne déduis pas un sexe d'un
                   prénom, ne devine pas une classe.
                3. N'AJOUTE ET N'OMETS AUCUNE LIGNE. Si tu ne parviens pas à lire une
                   ligne, rends-la quand même avec ce que tu distingues et signale les
                   cellules concernées.
                4. SIGNALE TES DOUTES dans « uncertain » : l'index de chaque cellule dont
                   tu n'es pas certain. C'est ce qui permet à l'école de vérifier six
                   cases au lieu de deux cents. Ne signale pas les cases vides.
                5. Un chiffre mal lu dans un TÉLÉPHONE ou un MONTANT est la faute la plus
                   coûteuse : dans le doute, signale la cellule.
                6. Les en-têtes de colonnes du cahier, les totaux, les titres de section
                   et les pages vides ne sont pas des personnes : ne les rends pas.
                7. Écris l'arabe en arabe, le français en français. Ne translittère pas.
                """;
        }

        private static string UserPrompt(int count, int pageOffset) =>
            pageOffset == 0
                ? $"Voici {count} page(s) du cahier. Transcris toutes les personnes qui y figurent."
                : $"Voici {count} page(s) supplémentaire(s) du MÊME cahier "
                  + $"(la suite, à partir de la page {pageOffset + 1}). Les colonnes sont les mêmes ; "
                  + "ces pages n'ont peut-être pas de ligne d'en-tête. Transcris uniquement les "
                  + "personnes de ces pages.";

        private static List<(List<string> Cells, List<int> Uncertain)> ParseRows(string json, int columnCount)
        {
            var outp = new List<(List<string>, List<int>)>();
            if (string.IsNullOrWhiteSpace(json)) return outp;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("rows", out var rows)
                || rows.ValueKind != JsonValueKind.Array)
                return outp;

            foreach (var r in rows.EnumerateArray())
            {
                if (!r.TryGetProperty("cells", out var cells) || cells.ValueKind != JsonValueKind.Array)
                    continue;

                var values = cells.EnumerateArray()
                    .Select(c => c.ValueKind == JsonValueKind.String ? c.GetString() ?? string.Empty : string.Empty)
                    .ToList();

                // Une ligne entièrement vide est un artefact de lecture, pas une
                // personne : l'importer créerait une fiche fantôme.
                if (values.All(string.IsNullOrWhiteSpace)) continue;

                var flagged = new List<int>();
                if (r.TryGetProperty("uncertain", out var unc) && unc.ValueKind == JsonValueKind.Array)
                    flagged.AddRange(unc.EnumerateArray()
                        .Where(u => u.ValueKind == JsonValueKind.Number)
                        .Select(u => u.GetInt32()));

                outp.Add((values, flagged));
            }
            return outp;
        }

        /// <summary>
        /// Type MIME accepté par l'API. On ne fait pas confiance à ce que
        /// déclare le téléphone : un type inconnu devient JPEG, qui est ce que
        /// produit un appareil photo dans la quasi-totalité des cas.
        /// </summary>
        private static string MediaTypeOf(string mime) => mime.ToLowerInvariant() switch
        {
            "image/png" => "image/png",
            "image/gif" => "image/gif",
            "image/webp" => "image/webp",
            _ => "image/jpeg",
        };
    }
}
