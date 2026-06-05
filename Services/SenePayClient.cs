using System.Net;
using System.Text;
using System.Text.Json;
using Idara.API.DTOs.Senepay;

namespace Idara.API.Services
{
    /// <summary>
    /// Lancée quand SenePay retourne un vrai 4xx/5xx (auth manquante, exception
    /// non gérée côté SenePay, timeout réseau). Les échecs FONCTIONNELS
    /// (refus PSP, OTP erroné) ne lèvent PAS — SenePay les retourne en 200 OK
    /// avec status=Failed.
    /// </summary>
    public class SenePayApiException : Exception
    {
        public int? StatusCode { get; }
        public string? ResponseBody { get; }

        public SenePayApiException(string message, int? statusCode = null, string? responseBody = null, Exception? inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }
    }

    public class SenePayClient : ISenePayClient
    {
        private readonly HttpClient _http;
        private readonly ILogger<SenePayClient> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public SenePayClient(HttpClient http, ILogger<SenePayClient> logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<SenePayInitiatePaymentResponse> InitiatePaymentAsync(
            SenePayInitiatePaymentRequest request,
            CancellationToken ct = default)
        {
            var startedAt = DateTime.UtcNow;
            var bodyJson = JsonSerializer.Serialize(request, JsonOptions);

            // Log REQUEST sans les secrets (les secrets sont dans les headers
            // de _http, jamais dans le body). On masque néanmoins le téléphone
            // partiellement pour les logs.
            _logger.LogInformation(
                "[SenePay] POST /payments/initiate amount={Amount} operator={Op} phone={Phone} orderId={OrderId}",
                request.Amount, request.Operator, MaskPhone(request.CustomerPhone), request.OrderId);

            HttpResponseMessage httpResponse;
            try
            {
                using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                httpResponse = await _http.PostAsync("api/v1/payments/initiate", content, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // Timeout interne au HttpClient (Timeout = 30s configuré dans Program.cs).
                _logger.LogError(ex, "[SenePay] TIMEOUT POST /payments/initiate");
                throw new SenePayApiException("Timeout calling SenePay /payments/initiate", null, null, ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[SenePay] NETWORK ERROR POST /payments/initiate");
                throw new SenePayApiException("Network error calling SenePay", null, null, ex);
            }

            var responseBody = await httpResponse.Content.ReadAsStringAsync(ct);
            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;

            // SenePay : 4xx/5xx = vrai problème technique. Les échecs métier
            // viennent en 200 + status=Failed (cf. doc §1 ligne 645).
            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[SenePay] HTTP {Status} POST /payments/initiate elapsedMs={Elapsed:0} body={Body}",
                    (int)httpResponse.StatusCode, elapsedMs, Truncate(responseBody, 500));
                throw new SenePayApiException(
                    $"SenePay returned HTTP {(int)httpResponse.StatusCode}",
                    (int)httpResponse.StatusCode,
                    responseBody);
            }

            SenePayInitiatePaymentResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<SenePayInitiatePaymentResponse>(responseBody, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "[SenePay] Body 200 OK mais JSON inattendu : {Body}", Truncate(responseBody, 500));
                throw new SenePayApiException("Malformed SenePay response", 200, responseBody, ex);
            }

            if (parsed == null)
            {
                _logger.LogError("[SenePay] Body 200 OK mais null après parsing : {Body}", Truncate(responseBody, 500));
                throw new SenePayApiException("Empty SenePay response", 200, responseBody);
            }

            // Log RESPONSE — le status fonctionnel, le nextAction, le token.
            _logger.LogInformation(
                "[SenePay] OK /payments/initiate status={Status} nextAction={NextAction} token={Token} internalId={InternalId} elapsedMs={Elapsed:0}",
                parsed.Status, parsed.NextAction, parsed.Token, parsed.InternalId, elapsedMs);

            return parsed;
        }

        public async Task<SenePayPayoutResponse> InitiatePayoutAsync(
            SenePayPayoutRequest request,
            CancellationToken ct = default)
        {
            var startedAt = DateTime.UtcNow;
            var bodyJson = JsonSerializer.Serialize(request, JsonOptions);

            _logger.LogInformation(
                "[SenePay] POST /payouts amount={Amount} operator={Op} phone={Phone} externalId={ExternalId}",
                request.Amount, request.Operator, MaskPhone(request.Phone), request.ExternalId);

            HttpResponseMessage httpResponse;
            try
            {
                using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                httpResponse = await _http.PostAsync("api/v1/payouts", content, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[SenePay] TIMEOUT POST /payouts");
                throw new SenePayApiException("Timeout calling SenePay /payouts", null, null, ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[SenePay] NETWORK ERROR POST /payouts");
                throw new SenePayApiException("Network error calling SenePay payout", null, null, ex);
            }

            var responseBody = await httpResponse.Content.ReadAsStringAsync(ct);
            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;

            // 4xx/5xx = vrai problème (auth, montant, solde marchand insuffisant).
            // L'appelant restituera la réservation wallet.
            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[SenePay] HTTP {Status} POST /payouts elapsedMs={Elapsed:0} body={Body}",
                    (int)httpResponse.StatusCode, elapsedMs, Truncate(responseBody, 500));
                throw new SenePayApiException(
                    $"SenePay returned HTTP {(int)httpResponse.StatusCode}",
                    (int)httpResponse.StatusCode,
                    responseBody);
            }

            SenePayPayoutResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<SenePayPayoutResponse>(responseBody, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "[SenePay] Payout body 200 OK mais JSON inattendu : {Body}", Truncate(responseBody, 500));
                throw new SenePayApiException("Malformed SenePay payout response", 200, responseBody, ex);
            }

            if (parsed == null)
            {
                _logger.LogError("[SenePay] Payout body 200 OK mais null après parsing : {Body}", Truncate(responseBody, 500));
                throw new SenePayApiException("Empty SenePay payout response", 200, responseBody);
            }

            _logger.LogInformation(
                "[SenePay] OK /payouts status={Status} disbursementId={DisbId} netAmount={Net} elapsedMs={Elapsed:0}",
                parsed.Status, parsed.DisbursementId, parsed.NetAmount, elapsedMs);

            return parsed;
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
