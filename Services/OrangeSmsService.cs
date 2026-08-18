using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Idara.API.Options;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    /// <summary>
    /// Client SMS Orange SMS Pro (API HTTP, cf. CLAUDE.md §117 et le guide
    /// « api http.pdf »). Remplace Africa's Talking derrière <see cref="ISmsService"/>
    /// (§89 : changer de fournisseur = ce seul fichier + la config).
    ///
    /// Particularités de cette API, toutes gérées ici :
    ///  - double authentification : Basic <c>login:token</c> EN PLUS des
    ///    paramètres <c>token/key/timestamp</c> de la requête ;
    ///  - <c>key</c> = HMAC-SHA1(token+subject+signature+recipient+content+timestamp,
    ///    clé privée) en hexadécimal, À USAGE UNIQUE (rejouer = erreur 115) →
    ///    régénérée à chaque appel, jamais de retry interne ;
    ///  - destinataire SANS le <c>+</c> (<c>221771234567</c>, sinon erreur 110) ;
    ///  - la réponse est du TEXTE BRUT (<c>STATUS_CODE: …</c>) et les codes
    ///    d'erreur arrivent dans le CORPS d'un HTTP 200 — ne jamais se fier au
    ///    statut HTTP seul.
    ///
    /// NE LÈVE PAS : tout échec revient en <see cref="SmsSendResult"/>
    /// Success=false. No-op + warning tant que la config est incomplète — un
    /// déploiement prod avant configuration ne casse rien.
    /// </summary>
    public class OrangeSmsService : ISmsService
    {
        private readonly HttpClient _http;
        private readonly OrangeSmsSettings _settings;
        private readonly ILogger<OrangeSmsService> _logger;

        public OrangeSmsService(
            HttpClient http,
            IOptions<OrangeSmsSettings> settings,
            ILogger<OrangeSmsService> logger)
        {
            _http = http;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<SmsSendResult> SendAsync(string toE164, string message, CancellationToken ct = default)
        {
            if (!_settings.IsConfigured)
            {
                _logger.LogWarning(
                    "[orange-sms] SMS non envoyé : OrangeSms non configuré (no-op). to={To}",
                    MaskPhone(toE164));
                return new SmsSendResult(false, Error: "SMS provider not configured");
            }

            if (string.IsNullOrWhiteSpace(toE164))
                return new SmsSendResult(false, Error: "Empty recipient");
            if (string.IsNullOrWhiteSpace(message))
                return new SmsSendResult(false, Error: "Empty message");

            // "+221771234567" → "221771234567" (le + est refusé, erreur 110).
            var recipient = toE164.Trim().TrimStart('+');
            var startedAt = DateTime.UtcNow;

            // Clé publique à usage unique : signer la concaténation EXACTE, dans
            // cet ordre, avec le timestamp de CETTE tentative.
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var chaine = _settings.Token + _settings.Subject + _settings.Signature
                         + recipient + message + timestamp;
            var key = HmacSha1Hex(chaine, _settings.PrivateKey);

            var query = BuildQuery(new[]
            {
                ("token", _settings.Token),
                ("subject", _settings.Subject),
                ("signature", _settings.Signature),
                ("recipient", recipient),
                ("content", message),
                ("timestamp", timestamp),
                ("key", key),
            });
            var url = _settings.BaseUrl.TrimEnd('?') + "?" + query;

            _logger.LogInformation(
                "[orange-sms] GET /api to={To} signature={Signature} len={Len}",
                MaskPhone(toE164), _settings.Signature, message.Length);

            HttpResponseMessage httpResponse;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                // Basic login:token relu à chaque appel (jamais baké dans le
                // client) pour rester frais et permettre le no-op si déconfiguré.
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.Login}:{_settings.Token}")));
                request.Headers.Add("Accept", "application/json");

                httpResponse = await _http.SendAsync(request, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[orange-sms] TIMEOUT GET /api to={To}", MaskPhone(toE164));
                return new SmsSendResult(false, Error: "Timeout");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[orange-sms] NETWORK ERROR GET /api to={To}", MaskPhone(toE164));
                return new SmsSendResult(false, Error: "Network error");
            }

            var body = await httpResponse.Content.ReadAsStringAsync(ct);
            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;

            // HTTP non-2xx = souche transport/auth (401 Basic = login ou token faux).
            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[orange-sms] HTTP {Status} GET /api to={To} elapsedMs={Elapsed:0} body={Body}",
                    (int)httpResponse.StatusCode, MaskPhone(toE164), elapsedMs, Truncate(body, 300));
                return new SmsSendResult(false, Error: $"HTTP {(int)httpResponse.StatusCode}");
            }

            // ⚠️ La doc décrit une réponse TEXTE (« STATUS_CODE: 200 ») mais
            // l'API réelle répond en JSON (constaté en prod le 2026-08-18) :
            // {"response":[{"status_code":200,"status_text":"Message envoye",
            //  "message_id":…,"messagedetail_id":…,"recipient":"…"}]}.
            // On accepte LES DEUX formes — même leçon qu'avec SenePay (§53/§66) :
            // le comportement observé fait foi, jamais la doc. Les codes d'erreur
            // sont dans le CORPS, pas dans le statut HTTP.
            var (statusCode, statusText, messageId) = ParseResponse(body);

            var ok = statusCode == "200";
            if (ok)
            {
                _logger.LogInformation(
                    "[orange-sms] OK to={To} msgId={MsgId} elapsedMs={Elapsed:0}",
                    MaskPhone(toE164), messageId, elapsedMs);
                return new SmsSendResult(true, messageId, statusText ?? "Sent");
            }

            // Diagnostics explicites des pièges connus (§117) — pour que le
            // journal dise la cause, pas juste « échec ».
            var hint = statusCode switch
            {
                "102" => $"signature '{_settings.Signature}' invalide, EN ATTENTE DE VALIDATION ou bloquée côté Orange — ce n'est PAS un bug de code, vérifier la signature dans Alert SMS",
                "115" => "clé publique rejouée — bug interne : la clé doit être régénérée à CHAQUE tentative",
                "116" => "token invalide — recopier le token depuis Paramètres → API du portail",
                "117" => "horloge désynchronisée — vérifier le NTP du serveur",
                "121" => "clé publique non concordante — vérifier la clé privée et la chaîne signée",
                "110" => "longueur du numéro incorrecte — format attendu : indicatif sans le +",
                _ => null,
            };

            // Le corps brut est loggé (tronqué) : c'est lui qui a permis de
            // découvrir le format JSON non documenté — sans lui, « code=? »
            // ne dit rien d'exploitable.
            _logger.LogError(
                "[orange-sms] REJECTED code={Code} text={Text} to={To} elapsedMs={Elapsed:0} body={Body}{Hint}",
                statusCode ?? "?", statusText, MaskPhone(toE164), elapsedMs,
                Truncate(body, 300), hint == null ? "" : " — " + hint);

            return new SmsSendResult(false, messageId, statusText,
                Error: $"{statusCode ?? "?"} {statusText}".Trim());
        }

        // ===================== Helpers =====================

        private static string HmacSha1Hex(string data, string privateKey)
        {
            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(privateKey));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string BuildQuery(IEnumerable<(string Name, string Value)> parameters) =>
            string.Join("&", parameters.Select(p =>
                $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.Value)}"));

        /// <summary>
        /// Extrait (code, texte, identifiant de message) de la réponse Orange,
        /// quelle que soit sa forme : JSON réel
        /// (<c>{"response":[{"status_code":…}]}</c>, constaté en prod) ou texte
        /// documenté (<c>STATUS_CODE: …</c>). Public et statique EXPRÈS : une
        /// réponse qu'on ne peut vérifier qu'en payant un SMS ne se vérifie
        /// jamais (§133) — le banc d'essai la contrôle sur les corps réels.
        /// </summary>
        public static (string? Code, string? Text, string? MessageId) ParseResponse(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return (null, null, null);

            var trimmed = body.TrimStart();
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    if (root.ValueKind == System.Text.Json.JsonValueKind.Object
                        && root.TryGetProperty("response", out var arr)
                        && arr.ValueKind == System.Text.Json.JsonValueKind.Array
                        && arr.GetArrayLength() > 0)
                    {
                        var e = arr[0];
                        return (
                            JsonScalar(e, "status_code"),
                            JsonScalar(e, "status_text"),
                            JsonScalar(e, "messagedetail_id") ?? JsonScalar(e, "message_id"));
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // Corps qui ressemble à du JSON sans en être : retombe sur le texte.
                }
            }

            return (
                Extract(body, "STATUS_CODE"),
                Extract(body, "STATUS_TEXT"),
                Extract(body, "MESSAGEDETAIL_ID") ?? Extract(body, "MESSAGE_ID"));
        }

        /// <summary>Valeur scalaire d'une propriété JSON (string OU number →
        /// string), null si absente ou d'un autre type.</summary>
        private static string? JsonScalar(System.Text.Json.JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v)) return null;
            return v.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => v.GetString(),
                System.Text.Json.JsonValueKind.Number => v.GetRawText(),
                _ => null,
            };
        }

        /// <summary>Extrait la valeur d'une ligne <c>NOM: valeur</c> de la réponse
        /// texte. Insensible aux espaces. Null si absente.</summary>
        private static string? Extract(string body, string field)
        {
            if (string.IsNullOrEmpty(body)) return null;
            var m = Regex.Match(body, $@"{Regex.Escape(field)}\s*:\s*(.+?)\s*$",
                RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private static string MaskPhone(string phone)
        {
            if (string.IsNullOrEmpty(phone) || phone.Length < 4) return "***";
            return phone[..^4] + "****";
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s[..max] + "…";
        }
    }
}
