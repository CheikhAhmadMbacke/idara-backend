using System.Text.Json;
using Idara.API.Options;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    /// <summary>
    /// Client SMS Africa's Talking (API v1 messaging, form-urlencoded).
    /// Calqué sur <see cref="SenePayClient"/> pour la robustesse (timeout /
    /// réseau gérés, logs structurés sans secret), mais NE LÈVE PAS : tout échec
    /// revient en <see cref="SmsSendResult"/> Success=false. Inoffensif tant que
    /// <c>AfricasTalking:ApiKey</c> n'est pas configuré (no-op + warning) — ainsi
    /// un déploiement prod avant configuration ne casse rien.
    /// </summary>
    public class AfricasTalkingSmsService : ISmsService
    {
        private readonly HttpClient _http;
        private readonly AfricasTalkingSettings _settings;
        private readonly ILogger<AfricasTalkingSmsService> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public AfricasTalkingSmsService(
            HttpClient http,
            IOptions<AfricasTalkingSettings> settings,
            ILogger<AfricasTalkingSmsService> logger)
        {
            _http = http;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<SmsSendResult> SendAsync(string toE164, string message, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                _logger.LogWarning(
                    "[AT] SMS non envoyé : AfricasTalking:ApiKey non configuré (no-op). to={To}",
                    MaskPhone(toE164));
                return new SmsSendResult(false, Error: "SMS provider not configured");
            }

            if (string.IsNullOrWhiteSpace(toE164))
                return new SmsSendResult(false, Error: "Empty recipient");

            var startedAt = DateTime.UtcNow;

            // Corps form-urlencoded. `from` (Sender ID) optionnel : ignoré en
            // sandbox et tant que les opérateurs n'ont pas validé "Idara".
            var form = new List<KeyValuePair<string, string>>
            {
                new("username", _settings.Username),
                new("to", toE164),
                new("message", message),
            };
            if (!string.IsNullOrWhiteSpace(_settings.SenderId))
                form.Add(new("from", _settings.SenderId));

            _logger.LogInformation(
                "[AT] POST /version1/messaging to={To} from={From} len={Len}",
                MaskPhone(toE164),
                string.IsNullOrWhiteSpace(_settings.SenderId) ? "(shortcode)" : _settings.SenderId,
                message?.Length ?? 0);

            HttpResponseMessage httpResponse;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "version1/messaging")
                {
                    Content = new FormUrlEncodedContent(form)
                };
                // La clé est lue à chaque appel (jamais bakée dans le client) pour
                // rester fraîche et permettre le no-op si non configurée.
                request.Headers.Add("apiKey", _settings.ApiKey);
                request.Headers.Add("Accept", "application/json");

                httpResponse = await _http.SendAsync(request, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[AT] TIMEOUT POST /version1/messaging to={To}", MaskPhone(toE164));
                return new SmsSendResult(false, Error: "Timeout");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[AT] NETWORK ERROR POST /version1/messaging to={To}", MaskPhone(toE164));
                return new SmsSendResult(false, Error: "Network error");
            }

            var body = await httpResponse.Content.ReadAsStringAsync(ct);
            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[AT] HTTP {Status} POST /version1/messaging elapsedMs={Elapsed:0} body={Body}",
                    (int)httpResponse.StatusCode, elapsedMs, Truncate(body, 500));
                return new SmsSendResult(false, Error: $"HTTP {(int)httpResponse.StatusCode}");
            }

            AtMessagingResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<AtMessagingResponse>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "[AT] Réponse 2xx mais JSON inattendu : {Body}", Truncate(body, 500));
                return new SmsSendResult(false, Error: "Malformed response");
            }

            var recipient = parsed?.SMSMessageData?.Recipients?.FirstOrDefault();
            var ok = recipient != null
                     && (string.Equals(recipient.Status, "Success", StringComparison.OrdinalIgnoreCase)
                         || recipient.StatusCode is 100 or 101 or 102);

            _logger.LogInformation(
                "[AT] {Result} /version1/messaging summary=\"{Summary}\" to={To} status={Status} code={Code} msgId={MsgId} cost={Cost} elapsedMs={Elapsed:0}",
                ok ? "OK" : "REJECTED",
                parsed?.SMSMessageData?.Message,
                MaskPhone(toE164),
                recipient?.Status, recipient?.StatusCode, recipient?.MessageId, recipient?.Cost, elapsedMs);

            return ok
                ? new SmsSendResult(true, recipient!.MessageId, recipient.Status, Cost: recipient.Cost)
                : new SmsSendResult(false, recipient?.MessageId, recipient?.Status,
                    Error: recipient?.Status ?? parsed?.SMSMessageData?.Message ?? "Rejected");
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

        // ---- Forme de réponse AT v1 messaging ----
        private sealed class AtMessagingResponse
        {
            public AtMessageData? SMSMessageData { get; set; }
        }

        private sealed class AtMessageData
        {
            public string? Message { get; set; }
            public List<AtRecipient>? Recipients { get; set; }
        }

        private sealed class AtRecipient
        {
            public string? Number { get; set; }
            public string? Status { get; set; }
            public int StatusCode { get; set; }
            public string? Cost { get; set; }
            public string? MessageId { get; set; }
        }
    }
}
