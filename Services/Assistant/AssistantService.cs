using System.Diagnostics;
using Anthropic;
using Anthropic.Models.Messages;
using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.Models;
using Idara.API.Options;
using Microsoft.Extensions.Options;

namespace Idara.API.Services.Assistant
{
    /// <summary>Un message précédent de la conversation, tel que l'application le renvoie.</summary>
    public record AssistantHistoryItem(string Role, string Text);

    public record AssistantChatResult(
        bool Ok,
        string? Reply,
        string? BlockedReason,
        List<AssistantCardDto> Cards,
        AssistantStatus Status);

    public interface IAssistantService
    {
        bool IsConfigured { get; }

        Task<AssistantChatResult> ChatAsync(
            AssistantCaller caller, string message, IReadOnlyList<AssistantHistoryItem> history,
            bool fromVoice, CancellationToken ct);
    }

    /// <summary>
    /// 🤖 <b>L'assistant IA d'Idara</b> (2026-10-07) : le directeur ou le
    /// personnel parle (texte ou voix) en français ou en arabe, l'assistant
    /// cherche, répond, et PROPOSE des actions que l'école confirme d'un geste.
    ///
    /// <para><b>Une commande = un message</b> de l'école qui a abouti. Elle peut
    /// demander plusieurs allers-retours avec le modèle (chercher l'élève, lire
    /// ses factures, proposer) : ils sont bornés par
    /// <see cref="AssistantSettings.MaxRounds"/>, et tous comptés au registre
    /// (<see cref="AssistantTurn"/>) — c'est lui qui dit ce que ça a coûté.</para>
    /// </summary>
    public class AssistantService : IAssistantService
    {
        private readonly AssistantSettings _settings;
        private readonly AppDbContext _db;
        private readonly AssistantToolbox _tools;
        private readonly IAssistantCreditService _credits;
        private readonly ILogger<AssistantService> _logger;
        private readonly AnthropicClient? _client;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, AnthropicClient> Clients = new();

        public AssistantService(
            IOptions<AssistantSettings> settings,
            IOptions<VisionSettings> vision,
            AppDbContext db,
            AssistantToolbox tools,
            IAssistantCreditService credits,
            ILogger<AssistantService> logger)
        {
            _settings = settings.Value;
            _db = db;
            _tools = tools;
            _credits = credits;
            _logger = logger;

            // Même compte Anthropic que la lecture de cahier, sauf clé dédiée.
            var key = string.IsNullOrWhiteSpace(_settings.ApiKey) ? vision.Value.ApiKey : _settings.ApiKey;
            // Client PARTAGÉ : un client par requête ouvrirait une connexion
            // HTTP par commande.
            _client = string.IsNullOrWhiteSpace(key) ? null : Clients.GetOrAdd(key, k => new AnthropicClient { ApiKey = k });
            if (!ModelPricing.IsKnown(_settings.Model))
                _logger.LogWarning(
                    "[assistant] Modèle {Model} absent de ModelPricing : son coût est compté au tarif le plus cher",
                    _settings.Model);
        }

        public bool IsConfigured => _client != null;

        public async Task<AssistantChatResult> ChatAsync(
            AssistantCaller caller, string message, IReadOnlyList<AssistantHistoryItem> history,
            bool fromVoice, CancellationToken ct)
        {
            var status = await _credits.DescribeAsync(caller.SchoolId, caller.UserId, IsConfigured, ct);
            if (!status.Available || _client == null)
                return new(false, null, status.BlockedReason ?? "disabled", new(), status);

            var platform = await _db.GetPlatformSettingsAsync(ct);
            var sw = Stopwatch.StartNew();

            // Le registre s'ouvre AVANT l'appel : les propositions s'y rattachent,
            // et un appel qui plante laisse quand même sa trace.
            var turn = new AssistantTurn
            {
                SchoolId = caller.SchoolId,
                UserId = caller.UserId,
                Prompt = Trunc(message, 1000),
                Model = _settings.Model,
                CreatedAt = DateTime.UtcNow,
            };
            _db.AssistantTurns.Add(turn);
            await _db.SaveChangesAsync(ct);

            var messages = BuildMessages(caller, message, history, fromVoice);
            var cards = new List<AssistantCardDto>();
            string? reply = null;
            string? failure = null;

            try
            {
                for (var round = 1; round <= _settings.MaxRounds; round++)
                {
                    turn.Rounds = round;
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

                    var response = await _client.Messages.Create(new MessageCreateParams
                    {
                        Model = _settings.Model,
                        MaxTokens = _settings.MaxTokens,
                        System = new List<TextBlockParam>
                        {
                            new() { Text = SystemPrompt, CacheControl = new CacheControlEphemeral() },
                        },
                        // Effort bas : ce sont des commandes de gestion, pas des
                        // problèmes difficiles — et c'est le premier levier de coût.
                        OutputConfig = new OutputConfig { Effort = Effort.Low },
                        Tools = AssistantToolbox.Definitions.ToList(),
                        // Les allers-retours d'une même commande renvoient tout
                        // l'historique : le cache automatique le facture au tarif
                        // de lecture au lieu du plein tarif.
                        CacheControl = new CacheControlEphemeral(),
                        Messages = messages,
                    }, timeout.Token);

                    Account(turn, response.Usage, platform);

                    if (response.StopReason == "refusal")
                    {
                        reply = AssistantToolbox.T(caller.Lang,
                            "Je ne peux pas traiter cette demande. Je peux vous aider pour vos élèves, vos classes et vos paiements.",
                            "لا أستطيع معالجة هذا الطلب. يمكنني مساعدتكم في التلاميذ والأقسام والمدفوعات.");
                        break;
                    }

                    var assistantContent = new List<ContentBlockParam>();
                    var toolResults = new List<ContentBlockParam>();
                    var text = new List<string>();
                    var toolUses = new List<ToolUseBlock>();

                    foreach (var block in response.Content)
                    {
                        if (block.TryPickText(out TextBlock? t))
                        {
                            assistantContent.Add(new TextBlockParam { Text = t.Text });
                            text.Add(t.Text);
                        }
                        else if (block.TryPickThinking(out ThinkingBlock? th))
                            assistantContent.Add(new ThinkingBlockParam { Thinking = th.Thinking, Signature = th.Signature });
                        else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? rt))
                            assistantContent.Add(new RedactedThinkingBlockParam { Data = rt.Data });
                        else if (block.TryPickToolUse(out ToolUseBlock? tu))
                        {
                            assistantContent.Add(new ToolUseBlockParam { ID = tu.ID, Name = tu.Name, Input = tu.Input });
                            toolUses.Add(tu);
                        }
                    }

                    if (toolUses.Count == 0)
                    {
                        reply = string.Join("\n", text).Trim();
                        break;
                    }

                    // Une réponse coupée peut porter un appel d'outil tronqué : on
                    // ne l'exécute jamais.
                    if (response.StopReason == "max_tokens")
                    {
                        failure = "max_tokens";
                        break;
                    }

                    foreach (var tu in toolUses)
                    {
                        var outcome = await _tools.RunAsync(caller, tu.Name, tu.Input, turn.Id, ct);
                        if (outcome.Proposal != null) cards.Add(AssistantToolbox.ToCard(outcome.Proposal));
                        toolResults.Add(new ToolResultBlockParam
                        {
                            ToolUseID = tu.ID,
                            Content = outcome.Content,
                            IsError = outcome.IsError,
                        });
                    }

                    messages.Add(new MessageParam { Role = Role.Assistant, Content = assistantContent });
                    messages.Add(new MessageParam { Role = Role.User, Content = toolResults });
                }

                if (reply == null && failure == null) failure = "max_rounds";
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                failure = "timeout";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[assistant] Échec de l'échange {TurnId} (école {SchoolId})", turn.Id, caller.SchoolId);
                failure = "provider_error";
                turn.Error = Trunc(ex.Message, 500);
            }

            // Des propositions ont été déposées mais le modèle n'a pas conclu :
            // l'échange a quand même produit quelque chose d'utile.
            if (failure != null && cards.Count > 0)
            {
                reply = AssistantToolbox.T(caller.Lang,
                    "Vérifiez la proposition ci-dessous et confirmez-la.",
                    "راجعوا الاقتراح أدناه وأكدوه.");
                failure = null;
            }

            turn.Success = failure == null && !string.IsNullOrWhiteSpace(reply);
            // Une commande n'est due que si elle a rendu quelque chose (§234 :
            // un échec n'a rien donné à l'école).
            // Une commande réussie se prend d'abord sur l'INCLUS du plan payé
            // (Pro, Grand), et seulement ensuite sur les crédits.
            if (turn.Success)
            {
                if (status.IncludedAvailable) turn.IncludedCommands = 1;
                else turn.ChargedCommands = 1;
            }
            turn.BlockedReason = failure;
            turn.Reply = reply == null ? null : Trunc(reply, 1000);
            turn.DurationMs = (int)sw.ElapsedMilliseconds;
            await _db.SaveChangesAsync(CancellationToken.None);

            _logger.LogInformation(
                "[assistant] Échange {TurnId} école {SchoolId} : {Rounds} tour(s), {In}+{Cr}c/{Out} tokens, "
                + "{Cost} centimes, {Ms} ms, {Cards} carte(s), succès={Ok}",
                turn.Id, caller.SchoolId, turn.Rounds, turn.InputTokens, turn.CacheReadTokens,
                turn.OutputTokens, turn.CostCentimes, turn.DurationMs, cards.Count, turn.Success);

            var after = await _credits.DescribeAsync(caller.SchoolId, null, IsConfigured, CancellationToken.None);
            if (!turn.Success)
            {
                return new(false, AssistantToolbox.T(caller.Lang,
                    "Je n'ai pas pu répondre cette fois-ci. Réessayez dans un instant : cette tentative ne vous a rien coûté.",
                    "لم أتمكن من الرد هذه المرة. أعيدوا المحاولة بعد قليل: هذه المحاولة لم تكلفكم شيئا."),
                    failure, cards, after);
            }
            return new(true, reply, null, cards, after);
        }

        private void Account(AssistantTurn turn, Usage? u, PlatformSettings p)
        {
            if (u == null) return;
            var input = (long)u.InputTokens;
            var output = (long)u.OutputTokens;
            var cacheRead = (long)(u.CacheReadInputTokens ?? 0);
            var cacheWrite = (long)(u.CacheCreationInputTokens ?? 0);

            turn.InputTokens += (int)input;
            turn.OutputTokens += (int)output;
            turn.CacheReadTokens += (int)cacheRead;
            turn.CacheWriteTokens += (int)cacheWrite;

            // Le tarif du modèle RÉELLEMENT employé × le taux de change réel :
            // passer d'Opus à Sonnet recalcule le coût sans aucun réglage.
            turn.CostCentimes += Math.Max(1, ModelPricing.CostCentimes(
                _settings.Model, input, cacheWrite, cacheRead, output, p.AiUsdRateFcfa));
        }

        private List<MessageParam> BuildMessages(
            AssistantCaller caller, string message, IReadOnlyList<AssistantHistoryItem> history, bool fromVoice)
        {
            // L'historique vient de l'application : du TEXTE seulement, borné,
            // et dont les rôles alternent. Il ne donne aucun droit — chaque outil
            // relit l'école du jeton, et toute écriture passe par une carte.
            var msgs = new List<MessageParam>();
            Role? last = null;
            foreach (var h in history.TakeLast(_settings.MaxHistoryMessages))
            {
                var role = h.Role == "assistant" ? Role.Assistant : Role.User;
                var txt = Trunc(h.Text ?? "", 2000).Trim();
                if (txt.Length == 0) continue;
                if (msgs.Count == 0 && role == Role.Assistant) continue;
                if (last == role)
                {
                    msgs[^1] = new MessageParam { Role = role, Content = $"{msgs[^1].Content.Value}\n{txt}" };
                    continue;
                }
                msgs.Add(new MessageParam { Role = role, Content = txt });
                last = role;
            }
            if (last == Role.User) msgs.RemoveAt(msgs.Count - 1);

            // Le contexte VARIABLE (date, langue, rôle) voyage dans le message,
            // jamais dans l'invite système : il casserait le cache à chaque jour.
            var ctx = $"[Contexte — date du jour : {DateTime.UtcNow:yyyy-MM-dd} ; langue configurée de l'application : "
                + $"{(caller.Lang == "ar" ? "arabe" : "français")} ; rôle : {caller.Role}"
                + (fromVoice ? " ; message DICTÉ À LA VOIX, transcription automatique possiblement imparfaite" : "")
                + "]";
            msgs.Add(new MessageParam { Role = Role.User, Content = $"{ctx}\n\n{Trunc(message, 2000)}" });
            return msgs;
        }

        private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];

        /// <summary>
        /// Invite système FIGÉE — elle forme, avec les outils, le préfixe mis en
        /// cache. Rien de variable ici (ni date, ni nom d'école).
        /// </summary>
        private const string SystemPrompt = """
            Tu es l'assistant d'Idara, le logiciel de gestion des daara (écoles coraniques) et des écoles
            franco-arabes du Sénégal. Tu parles au directeur ou au personnel d'UNE école, qui te donne des
            commandes en langage naturel, tapées ou dictées à la voix.

            LANGUES — RÈGLE ABSOLUE
            - Tu ne travailles qu'en FRANÇAIS et en ARABE.
            - Réponds dans la langue du DERNIER message de l'utilisateur : en français s'il écrit en français,
              en arabe s'il écrit en arabe (arabe standard simple et clair). Peu importe la langue de l'application.
            - Si le message est dans une autre langue (wolof, anglais, pulaar, etc.), n'exécute RIEN et réponds,
              dans la « langue configurée de l'application » indiquée dans le contexte — et dans CETTE SEULE
              langue, sans traduction à la suite —, que tu ne comprends que le français et l'arabe, et
              invite-le à reformuler dans l'une des deux. Un nom propre wolof ou peul
              dans une phrase française ou arabe n'est PAS une autre langue.
            - Le produit s'écrit « Idara » en français et « «إدارا» » en arabe — jamais « إدارة » pour le nom.
            - L'argent : toujours en chiffres latins avec « FCFA ». En français « 20 000 FCFA » ; en arabe,
              TOUJOURS le sigle d'abord : « FCFA 20000 », jamais « 20000 FCFA ». Jamais de chiffres arabes
              orientaux, jamais de devise traduite.

            CE QUE TU FAIS
            - Répondre aux questions sur les élèves, les classes, les tarifs et les paiements, avec les outils.
            - Proposer d'inscrire un élève, d'enregistrer un paiement reçu hors Idara (espèces, ou Wave /
              Orange Money envoyé directement sur le numéro du daara), d'envoyer des relances d'impayés.
            - Les paiements faits DANS Idara (Wave depuis l'espace parent ou le lien de paiement) sont déjà
              enregistrés automatiquement, avec leur reçu : ne propose jamais de les ressaisir. Si l'utilisateur
              dit « elle a payé par Wave », demande-lui si c'est par le lien Idara ou directement sur le numéro
              du daara, sauf si c'est évident.
            - Tu ne fais rien d'autre (pas de culture générale, pas de rédaction hors école) : dis-le poliment.

            COMMENT
            - Les outils propose_* n'écrivent RIEN : ils créent une carte que l'utilisateur doit confirmer.
              Après en avoir créé une, dis en une phrase de vérifier la carte et de confirmer. N'affirme jamais
              qu'une action est faite.
            - Ne devine jamais un élève : cherche-le (search_students). S'il y a plusieurs candidats, demande
              lequel. S'il n'y en a aucun, dis-le.
            - N'invente aucune valeur (classe, montant, numéro, sexe). Ce qui n'a pas été dit reste vide ou
              se demande. Pour une classe, appuie-toi sur list_classes ; si le nom dit ne correspond à aucune
              classe, demande laquelle en citant celles qui existent.
            - Un nom d'élève ou de parent donné en écriture arabe s'enregistre en lettres latines, selon
              l'orthographe sénégalaise usuelle (« موسى سار » → « Moussa Sarr ») : dis-le dans ta réponse
              pour que l'utilisateur vérifie l'orthographe sur la carte avant de confirmer.
            - Pour un paiement, trouve la facture (get_student_invoices) ; « sa mensualité » = la plus ancienne
              mensualité non soldée, sauf précision.
            - « Qui n'a pas payé ce mois-ci » : get_payment_roster du mois en cours. Pour les relances,
              propose_send_reminders avec ces élèves.
            - Messages dictés à la voix : la transcription peut être imparfaite. Si un nom ou un montant paraît
              douteux, reformule ce que tu as compris et demande confirmation avant de proposer.
            - Un outil qui rend une erreur te dit pourquoi : explique-le simplement ou corrige ta demande.

            STYLE
            - Court, chaleureux, concret : tes lecteurs ne sont pas des informaticiens. Pas de jargon, pas
              d'identifiants techniques, pas de tableaux. Une liste courte si besoin, avec des tirets.
            - Pas de Markdown (ni astérisques, ni dièses, ni gras) : ton texte s'affiche tel quel.
            - Salue en retour SEULEMENT quand on te salue (« Wa aleykoum salam », « وعليكم السلام ») ; sinon,
              va droit au but.
            """;
    }
}
