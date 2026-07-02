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
                "[SenePay] OK /payouts status={Status} disbursementId={DisbId} amount={Amount} debited={Debited} net={Net} feeMode={FeeMode} elapsedMs={Elapsed:0}",
                parsed.Status, parsed.DisbursementId, parsed.Amount, parsed.AmountDebited, parsed.NetAmount, parsed.FeeMode, elapsedMs);

            return parsed;
        }

        public async Task<SenePayPayoutStatusResponse?> GetPayoutStatusAsync(
            string idOrExternalId,
            CancellationToken ct = default)
        {
            var startedAt = DateTime.UtcNow;
            var path = $"api/v1/payouts/{Uri.EscapeDataString(idOrExternalId)}";

            _logger.LogInformation("[SenePay] GET /payouts/{Id}", idOrExternalId);

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _http.GetAsync(path, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[SenePay] TIMEOUT GET /payouts/{Id}", idOrExternalId);
                throw new SenePayApiException("Timeout calling SenePay GET /payouts/{id}", null, null, ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[SenePay] NETWORK ERROR GET /payouts/{Id}", idOrExternalId);
                throw new SenePayApiException("Network error calling SenePay GET payout", null, null, ex);
            }

            var responseBody = await httpResponse.Content.ReadAsStringAsync(ct);
            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;

            // 404 = décaissement jamais créé (ex. rejet pré-exécution). On
            // retourne null — l'appelant tranchera (généralement : pas de payout
            // réel donc on peut restituer, ou rester prudent selon le contexte).
            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation(
                    "[SenePay] GET /payouts/{Id} → 404 (introuvable) elapsedMs={Elapsed:0}",
                    idOrExternalId, elapsedMs);
                return null;
            }

            // Tout autre non-2xx (auth, 5xx, timeout côté serveur) = indéterminé :
            // on lève, l'appelant garde UnderVerification et réessaiera.
            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[SenePay] HTTP {Status} GET /payouts/{Id} elapsedMs={Elapsed:0} body={Body}",
                    (int)httpResponse.StatusCode, idOrExternalId, elapsedMs, Truncate(responseBody, 500));
                throw new SenePayApiException(
                    $"SenePay returned HTTP {(int)httpResponse.StatusCode}",
                    (int)httpResponse.StatusCode,
                    responseBody);
            }

            SenePayPayoutStatusResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<SenePayPayoutStatusResponse>(responseBody, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "[SenePay] GET /payouts/{Id} body 200 mais JSON inattendu : {Body}",
                    idOrExternalId, Truncate(responseBody, 500));
                throw new SenePayApiException("Malformed SenePay payout status response", 200, responseBody, ex);
            }

            if (parsed == null)
            {
                _logger.LogError(
                    "[SenePay] GET /payouts/{Id} body 200 mais null après parsing : {Body}",
                    idOrExternalId, Truncate(responseBody, 500));
                throw new SenePayApiException("Empty SenePay payout status response", 200, responseBody);
            }

            _logger.LogInformation(
                "[SenePay] OK GET /payouts/{Id} status={Status} disbursementId={DisbId} elapsedMs={Elapsed:0}",
                idOrExternalId, parsed.Status, parsed.DisbursementId, elapsedMs);

            return parsed;
        }

        public async Task<SenePayPayinStatusResponse?> GetPayinStatusAsync(
            string token,
            CancellationToken ct = default)
        {
            var startedAt = DateTime.UtcNow;
            // Chemin CORRECT = api/v1/payments/{token}/status (avec le segment
            // "payments/"). Sans lui → 404 systématique, même pour un paiement
            // réussi (piège vécu le 2026-06-24, cf. §108).
            var path = $"api/v1/payments/{Uri.EscapeDataString(token)}/status";

            _logger.LogInformation("[SenePay] GET /{Token}/status", token);

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _http.GetAsync(path, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[SenePay] TIMEOUT GET /{Token}/status", token);
                throw new SenePayApiException("Timeout calling SenePay GET /{token}/status", null, null, ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[SenePay] NETWORK ERROR GET /{Token}/status", token);
                throw new SenePayApiException("Network error calling SenePay GET payin status", null, null, ex);
            }

            var responseBody = await httpResponse.Content.ReadAsStringAsync(ct);
            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;

            // 404 = PAYMENT_NOT_FOUND : SenePay n'a aucun paiement pour ce token
            // (ex. l'initiate n'a jamais abouti côté SenePay). On retourne null —
            // l'appelant tranchera (aucun paiement réel → sûr de marquer échoué).
            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation(
                    "[SenePay] GET /{Token}/status → 404 (introuvable) elapsedMs={Elapsed:0}",
                    token, elapsedMs);
                return null;
            }

            // Tout autre non-2xx (auth, 5xx, timeout serveur) = indéterminé :
            // on lève, l'appelant garde le Payment en Pending et réessaiera.
            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[SenePay] HTTP {Status} GET /{Token}/status elapsedMs={Elapsed:0} body={Body}",
                    (int)httpResponse.StatusCode, token, elapsedMs, Truncate(responseBody, 500));
                throw new SenePayApiException(
                    $"SenePay returned HTTP {(int)httpResponse.StatusCode}",
                    (int)httpResponse.StatusCode,
                    responseBody);
            }

            SenePayPayinStatusResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<SenePayPayinStatusResponse>(responseBody, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "[SenePay] GET /{Token}/status body 200 mais JSON inattendu : {Body}",
                    token, Truncate(responseBody, 500));
                throw new SenePayApiException("Malformed SenePay payin status response", 200, responseBody, ex);
            }

            if (parsed == null)
            {
                _logger.LogError(
                    "[SenePay] GET /{Token}/status body 200 mais null après parsing : {Body}",
                    token, Truncate(responseBody, 500));
                throw new SenePayApiException("Empty SenePay payin status response", 200, responseBody);
            }

            _logger.LogInformation(
                "[SenePay] OK GET /{Token}/status status={Status} credited={Credited} elapsedMs={Elapsed:0}",
                token, parsed.Status, parsed.CreditedAmount, elapsedMs);

            return parsed;
        }

        public async Task<SenePayMerchantBalanceResponse> GetMerchantBalanceAsync(
            CancellationToken ct = default)
        {
            var startedAt = DateTime.UtcNow;

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _http.GetAsync("api/v1/merchant/wallet/balance", ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[SenePay] TIMEOUT GET /merchant/wallet/balance");
                throw new SenePayApiException("Timeout calling SenePay merchant balance", null, null, ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[SenePay] NETWORK ERROR GET /merchant/wallet/balance");
                throw new SenePayApiException("Network error calling SenePay merchant balance", null, null, ex);
            }

            var responseBody = await httpResponse.Content.ReadAsStringAsync(ct);
            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[SenePay] HTTP {Status} GET /merchant/wallet/balance elapsedMs={Elapsed:0} body={Body}",
                    (int)httpResponse.StatusCode, elapsedMs, Truncate(responseBody, 500));
                throw new SenePayApiException(
                    $"SenePay returned HTTP {(int)httpResponse.StatusCode}",
                    (int)httpResponse.StatusCode,
                    responseBody);
            }

            var parsed = JsonSerializer.Deserialize<SenePayMerchantBalanceResponse>(responseBody, JsonOptions)
                ?? throw new SenePayApiException("Empty SenePay merchant balance response", 200, responseBody);

            _logger.LogInformation(
                "[SenePay] OK GET /merchant/wallet/balance reserve={Reserve} elapsedMs={Elapsed:0}",
                parsed.ReserveBalanceFcfa, elapsedMs);

            return parsed;
        }

        public async Task<SenePayPayoutListResponse> GetPayoutsAsync(
            string? status, string? dateFrom, string? dateTo,
            int page, int pageSize, CancellationToken ct = default)
        {
            var startedAt = DateTime.UtcNow;

            var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
            if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrWhiteSpace(dateFrom)) query.Add($"dateFrom={Uri.EscapeDataString(dateFrom)}");
            if (!string.IsNullOrWhiteSpace(dateTo)) query.Add($"dateTo={Uri.EscapeDataString(dateTo)}");
            var url = "api/v1/payouts?" + string.Join("&", query);

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _http.GetAsync(url, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[SenePay] TIMEOUT GET /payouts");
                throw new SenePayApiException("Timeout calling SenePay list payouts", null, null, ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[SenePay] NETWORK ERROR GET /payouts");
                throw new SenePayApiException("Network error calling SenePay list payouts", null, null, ex);
            }

            var responseBody = await httpResponse.Content.ReadAsStringAsync(ct);
            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[SenePay] HTTP {Status} GET /payouts elapsedMs={Elapsed:0} body={Body}",
                    (int)httpResponse.StatusCode, elapsedMs, Truncate(responseBody, 500));
                throw new SenePayApiException(
                    $"SenePay returned HTTP {(int)httpResponse.StatusCode}",
                    (int)httpResponse.StatusCode, responseBody);
            }

            var parsed = JsonSerializer.Deserialize<SenePayPayoutListResponse>(responseBody, JsonOptions)
                ?? throw new SenePayApiException("Empty SenePay payouts list response", 200, responseBody);

            _logger.LogInformation(
                "[SenePay] OK GET /payouts page={Page} count={Count} total={Total} elapsedMs={Elapsed:0}",
                page, parsed.Data.Count, parsed.Pagination?.TotalCount, elapsedMs);

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
