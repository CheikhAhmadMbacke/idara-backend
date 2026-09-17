using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Idara.API.DTOs.Wave;
using Idara.API.Options;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    /// <summary>
    /// Levée quand Wave répond 4xx/5xx, ou sur timeout/erreur réseau.
    /// <see cref="Code"/> porte le code métier de Wave (<c>insufficient-funds</c>,
    /// <c>ip-not-allowed</c>, <c>invalid-signature</c>…) quand il est présent.
    /// </summary>
    public class WaveApiException : Exception
    {
        public int? StatusCode { get; }
        public string? Code { get; }
        public string? ResponseBody { get; }

        public WaveApiException(
            string message, int? statusCode = null, string? code = null,
            string? responseBody = null, Exception? inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            Code = code;
            ResponseBody = responseBody;
        }

        /// <summary>
        /// Rejet AVANT exécution : Wave a refusé la requête sans rien créer, on
        /// peut clore en échec sans risque. Un 5xx, un 408 ou un timeout sont
        /// au contraire indéterminés — jamais de conclusion (§78).
        /// </summary>
        public bool IsDefinitiveRejection =>
            StatusCode is >= 400 and < 500 && StatusCode != 408 && StatusCode != 429;
    }

    /// <inheritdoc cref="IWaveClient"/>
    public class WaveClient : IWaveClient
    {
        private readonly HttpClient _http;
        private readonly WaveSettings _settings;
        private readonly ILogger<WaveClient> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public WaveClient(HttpClient http, IOptions<WaveSettings> settings, ILogger<WaveClient> logger)
        {
            _http = http;
            _settings = settings.Value;
            _logger = logger;
        }

        // =================================================================
        // Checkout
        // =================================================================

        public async Task<WaveCheckoutSession> CreateCheckoutSessionAsync(
            WaveCreateCheckoutRequest request, CancellationToken ct = default)
        {
            _logger.LogInformation(
                "[Wave] POST /v1/checkout/sessions amount={Amount} ref={Ref}",
                request.Amount, request.ClientReference);

            var session = await SendAsync<WaveCheckoutSession>(
                HttpMethod.Post, "v1/checkout/sessions", request, null, ct);

            if (session is null || string.IsNullOrWhiteSpace(session.Id))
                throw new WaveApiException("Wave a répondu sans identifiant de session");

            _logger.LogInformation(
                "[Wave] session {SessionId} créée (checkout={Checkout}, payment={Payment})",
                session.Id, session.CheckoutStatus, session.PaymentStatus);

            return session;
        }

        public Task<WaveCheckoutSession?> GetCheckoutSessionAsync(
            string sessionId, CancellationToken ct = default) =>
            SendAsync<WaveCheckoutSession>(
                HttpMethod.Get, $"v1/checkout/sessions/{Uri.EscapeDataString(sessionId)}",
                null, null, ct, nullOn404: true);

        public async Task<WaveCheckoutSession?> FindCheckoutByClientReferenceAsync(
            string clientReference, CancellationToken ct = default)
        {
            var page = await SendAsync<WaveCheckoutSearchResponse>(
                HttpMethod.Get,
                $"v1/checkout/sessions/search?client_reference={Uri.EscapeDataString(clientReference)}",
                null, null, ct, nullOn404: true);

            // Plusieurs sessions peuvent porter la même référence (le parent a
            // rouvert le lien après un abandon). La réussie prime ; sinon la
            // plus récente.
            return page?.Result
                .OrderByDescending(s => string.Equals(s.PaymentStatus, "succeeded", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(s => s.WhenCreated ?? DateTime.MinValue)
                .FirstOrDefault();
        }

        public async Task<bool> RefundCheckoutAsync(string sessionId, CancellationToken ct = default)
        {
            try
            {
                await SendRawAsync(
                    HttpMethod.Post,
                    $"v1/checkout/sessions/{Uri.EscapeDataString(sessionId)}/refund",
                    null, null, ct);
                _logger.LogWarning("[Wave] REMBOURSEMENT session {SessionId}", sessionId);
                return true;
            }
            catch (WaveApiException ex) when (ex.StatusCode == 404)
            {
                return false;
            }
        }

        // =================================================================
        // Payout
        // =================================================================

        public async Task<WavePayout> CreatePayoutAsync(
            WaveCreatePayoutRequest request, string idempotencyKey, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                throw new ArgumentException("Idempotency-Key obligatoire", nameof(idempotencyKey));

            _logger.LogInformation(
                "[Wave] POST /v1/payout receive={Amount} mobile={Mobile} ref={Ref} idem={Idem}",
                request.ReceiveAmount, MaskPhone(request.Mobile), request.ClientReference, idempotencyKey);

            var payout = await SendAsync<WavePayout>(
                HttpMethod.Post, "v1/payout", request,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey },
                ct: ct);

            if (payout is null || string.IsNullOrWhiteSpace(payout.Id))
                throw new WaveApiException("Wave a répondu sans identifiant de décaissement");

            _logger.LogInformation(
                "[Wave] payout {PayoutId} status={Status} fee={Fee}",
                payout.Id, payout.Status, payout.Fee);

            return payout;
        }

        public Task<WavePayout?> GetPayoutAsync(string payoutId, CancellationToken ct = default) =>
            SendAsync<WavePayout>(
                HttpMethod.Get, $"v1/payout/{Uri.EscapeDataString(payoutId)}",
                null, null, ct, nullOn404: true);

        public async Task<WavePayout?> FindPayoutByClientReferenceAsync(
            string clientReference, CancellationToken ct = default)
        {
            var page = await SendAsync<WavePayoutSearchResponse>(
                HttpMethod.Get,
                $"v1/payouts/search?client_reference={Uri.EscapeDataString(clientReference)}",
                null, null, ct, nullOn404: true);

            return page?.Result
                .OrderByDescending(p => p.Timestamp ?? DateTime.MinValue)
                .FirstOrDefault();
        }

        // =================================================================
        // Solde et registre
        // =================================================================

        public async Task<long> GetBalanceFcfaAsync(CancellationToken ct = default)
        {
            var balance = await SendAsync<WaveBalance>(HttpMethod.Get, "v1/balance", null, null, ct);
            return ParseAmount(balance?.Amount);
        }

        public async Task<WaveTransactionPage> GetTransactionsAsync(
            DateOnly? date, string? after, int? first, CancellationToken ct = default)
        {
            var query = new List<string>();
            if (date is DateOnly d) query.Add($"date={d:yyyy-MM-dd}");
            if (!string.IsNullOrWhiteSpace(after)) query.Add($"after={Uri.EscapeDataString(after)}");
            if (first is int f) query.Add($"first={f}");

            var path = "v1/transactions" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
            return await SendAsync<WaveTransactionPage>(HttpMethod.Get, path, null, null, ct)
                   ?? new WaveTransactionPage();
        }

        // =================================================================
        // Plomberie : signature, envoi, erreurs
        // =================================================================

        private async Task<T?> SendAsync<T>(
            HttpMethod method, string path, object? body,
            Dictionary<string, string>? headers, CancellationToken ct,
            bool nullOn404 = false) where T : class
        {
            var raw = await SendRawAsync(method, path, body, headers, ct, nullOn404);
            if (raw is null) return null;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                return JsonSerializer.Deserialize<T>(raw, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new WaveApiException(
                    $"Réponse Wave illisible sur {path}", null, null, Truncate(raw), ex);
            }
        }

        /// <summary>
        /// Envoi effectif. Renvoie le corps brut, ou <c>null</c> sur 404 quand
        /// <paramref name="nullOn404"/>. Lève <see cref="WaveApiException"/>
        /// sinon.
        /// </summary>
        private async Task<string?> SendRawAsync(
            HttpMethod method, string path, object? body,
            Dictionary<string, string>? headers, CancellationToken ct,
            bool nullOn404 = false)
        {
            // 🔴 Le corps signé DOIT être exactement celui envoyé : on sérialise
            // une seule fois et on réutilise la chaîne pour la signature ET pour
            // le contenu. Re-sérialiser change les espaces et casse le HMAC.
            var bodyJson = body is null ? string.Empty : JsonSerializer.Serialize(body, JsonOptions);

            using var message = new HttpRequestMessage(method, path);
            if (body is not null)
                message.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            if (headers is not null)
                foreach (var (name, value) in headers)
                    message.Headers.TryAddWithoutValidation(name, value);

            // Signature des requêtes sortantes. Pour un GET sans corps, la
            // charge signée est l'horodatage SEUL (corps = chaîne vide).
            if (!string.IsNullOrWhiteSpace(_settings.SigningSecret))
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    .ToString(CultureInfo.InvariantCulture);
                var signature = ComputeHmacHex(timestamp + bodyJson, _settings.SigningSecret);
                message.Headers.TryAddWithoutValidation(
                    "Wave-Signature", $"t={timestamp},v1={signature}");
            }

            HttpResponseMessage response;
            var startedAt = DateTime.UtcNow;
            try
            {
                response = await _http.SendAsync(message, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[Wave] TIMEOUT {Method} {Path}", method, path);
                throw new WaveApiException($"Timeout Wave {method} {path}", null, null, null, ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[Wave] RÉSEAU {Method} {Path}", method, path);
                throw new WaveApiException($"Erreur réseau Wave {method} {path}", null, null, null, ex);
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;

            if (response.StatusCode == HttpStatusCode.NotFound && nullOn404)
            {
                _logger.LogInformation("[Wave] 404 {Path} ({Ms:F0} ms) — traité comme absence", path, elapsedMs);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var code = TryExtractErrorCode(responseBody);
                _logger.LogError(
                    "[Wave] HTTP {Status} {Method} {Path} code={Code} ({Ms:F0} ms) body={Body}",
                    (int)response.StatusCode, method, path, code, elapsedMs, Truncate(responseBody));

                throw new WaveApiException(
                    $"Wave {method} {path} → HTTP {(int)response.StatusCode} ({code ?? "sans code"})",
                    (int)response.StatusCode, code, Truncate(responseBody));
            }

            _logger.LogInformation(
                "[Wave] {Status} {Method} {Path} ({Ms:F0} ms)",
                (int)response.StatusCode, method, path, elapsedMs);

            return responseBody;
        }

        /// <summary>
        /// Wave place le code d'erreur soit à la racine (<c>{"code":…}</c>),
        /// soit sous <c>error</c> (<c>{"error":{"code":…}}</c>, cas du refus
        /// par liste d'adresses). On lit les deux.
        /// </summary>
        private static string? TryExtractErrorCode(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("code", out var direct) && direct.ValueKind == JsonValueKind.String)
                    return direct.GetString();
                if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object
                    && err.TryGetProperty("code", out var nested) && nested.ValueKind == JsonValueKind.String)
                    return nested.GetString();
            }
            catch (JsonException) { /* corps non JSON : rien à extraire */ }
            return null;
        }

        /// <summary>
        /// Montants Wave : chaînes, sans décimale en XOF. On tolère malgré tout
        /// une partie décimale (un « 500.00 » ne doit pas faire échouer un
        /// règlement) en tronquant vers zéro.
        /// </summary>
        public static long ParseAmount(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                return whole;
            if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec))
                return (long)decimal.Truncate(dec);
            return 0;
        }

        /// <summary>Montant FCFA → chaîne attendue par Wave (aucune décimale).</summary>
        public static string FormatAmount(long fcfa) => fcfa.ToString(CultureInfo.InvariantCulture);

        internal static string ComputeHmacHex(string payload, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string MaskPhone(string? phone) =>
            string.IsNullOrWhiteSpace(phone) || phone.Length < 4
                ? "***"
                : string.Concat(new string('*', phone.Length - 4), phone.AsSpan(phone.Length - 4));

        private static string Truncate(string? s, int max = 2000) =>
            string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "…");
    }
}
