using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Wave;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Endpoint public recevant les notifications signées de Wave.
    ///
    /// <para>🔴 Trois différences de fond avec le prestataire précédent, toutes
    /// structurantes :</para>
    /// <list type="number">
    /// <item>L'URL est enregistrée <b>dans le portail</b>, pas envoyée à chaque
    /// requête : il n'y a qu'une seule adresse, donc aucun routage
    /// développement/production possible.</item>
    /// <item>La signature porte un <b>horodatage</b> (<c>t=…,v1=…</c>) et une
    /// fenêtre de 5 minutes — c'est ce qui bloque le rejeu d'un message capté.</item>
    /// <item><b>Aucun événement de décaissement n'existe.</b> Ce contrôleur ne
    /// traite donc que des encaissements ; les retraits sont tranchés par
    /// <see cref="PayoutVerificationJob"/>, seul chemin disponible.</item>
    /// </list>
    ///
    /// <para>Wave exige une réponse en <b>moins de 5 secondes</b> et rejoue
    /// pendant 3 jours tant qu'elle n'est pas 2xx. Les événements peuvent
    /// arriver en double ou dans le désordre : l'idempotence repose sur
    /// <c>event.id</c>, via l'index unique (Provider, ExternalEventId).</para>
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/webhooks/wave")]
    public class WaveWebhooksController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly WaveSettings _wave;
        private readonly IPayinSettlementService _payinSettlement;
        private readonly ILogger<WaveWebhooksController> _logger;

        private const string ProviderName = PaymentProviders.Wave;

        public WaveWebhooksController(
            AppDbContext context,
            IOptions<WaveSettings> wave,
            IPayinSettlementService payinSettlement,
            ILogger<WaveWebhooksController> logger)
        {
            _context = context;
            _wave = wave.Value;
            _payinSettlement = payinSettlement;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Handle()
        {
            var startedAt = DateTime.UtcNow;

            // -------- 1) Corps BRUT, avant tout parsing --------
            // La signature porte sur les octets exacts reçus. Parser puis
            // re-sérialiser change les espaces et l'ordre des clés : le HMAC ne
            // correspondrait plus (§49).
            string rawBody;
            try
            {
                Request.EnableBuffering();
                using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
                rawBody = await reader.ReadToEndAsync();
                Request.Body.Position = 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[webhook/wave] Échec lecture du corps brut");
                return BadRequest(new { error = "Unable to read body" });
            }

            // -------- 2) Signature --------
            var signatureHeader = Request.Headers["Wave-Signature"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(signatureHeader))
            {
                _logger.LogWarning("[webhook/wave] En-tête Wave-Signature absent (octets={Len})", rawBody.Length);
                return Unauthorized(new { error = "Missing signature header" });
            }

            var signatureHash = Sha256Hex(signatureHeader);
            var verdict = VerifySignature(signatureHeader, rawBody, _wave.WebhookSecret, _wave.WebhookToleranceSeconds);

            if (verdict != SignatureVerdict.Valid)
            {
                _logger.LogWarning(
                    "[webhook/wave] SIGNATURE REFUSÉE ({Verdict}) sigHash={SigHash} octets={Len}",
                    verdict, signatureHash, rawBody.Length);
                await TrySaveAuditAsync($"invalid-signature:{verdict}", signatureHash, rawBody,
                    WebhookEventStatus.InvalidSignature, verdict.ToString());
                return Unauthorized(new { error = "Invalid signature" });
            }

            // -------- 3) Parsing (le corps est maintenant digne de confiance) --------
            WaveWebhookEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<WaveWebhookEnvelope>(
                    rawBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "[webhook/wave] Signature valide mais JSON illisible (octets={Len})", rawBody.Length);
                await TrySaveAuditAsync("malformed", signatureHash, rawBody,
                    WebhookEventStatus.ProcessingFailed, ex.Message);
                return Ok(new { received = true, processed = false, reason = "malformed_json" });
            }

            if (envelope is null || string.IsNullOrWhiteSpace(envelope.Id))
            {
                _logger.LogWarning("[webhook/wave] Événement sans identifiant — impossible d'idempotenter");
                await TrySaveAuditAsync("missing-id", signatureHash, rawBody,
                    WebhookEventStatus.ProcessingFailed, "missing_event_id");
                return Ok(new { received = true, processed = false, reason = "missing_event_id" });
            }

            // -------- 4) Insertion idempotente --------
            var ev = new WebhookEvent
            {
                Provider = ProviderName,
                ExternalEventId = envelope.Id,
                EventType = envelope.Type ?? "unknown",
                Payload = rawBody,
                SignatureHash = signatureHash,
                ReceivedAt = startedAt,
                Status = WebhookEventStatus.Received
            };

            _context.WebhookEvents.Add(ev);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException dbex) when (IsUniqueViolation(dbex))
            {
                _context.Entry(ev).State = EntityState.Detached;
                _logger.LogInformation(
                    "[webhook/wave] DOUBLON event={EventId} type={Type} — déjà traité",
                    envelope.Id, envelope.Type);
                return Ok(new { received = true, processed = false, duplicate = true });
            }

            // -------- 5) Traitement métier --------
            string? processingError = null;
            int? completedPaymentId = null;
            try
            {
                completedPaymentId = await ProcessAsync(envelope);
                ev.ProcessedAt = DateTime.UtcNow;
                ev.Status = WebhookEventStatus.Processed;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                processingError = ex.Message;
                _logger.LogError(ex,
                    "[webhook/wave] ÉCHEC du traitement event={EventId} type={Type}",
                    envelope.Id, envelope.Type);
                try
                {
                    await _context.WebhookEvents
                        .Where(w => w.Id == ev.Id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(w => w.Status, WebhookEventStatus.ProcessingFailed)
                            .SetProperty(w => w.ProcessingError, processingError));
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "[webhook/wave] Échec du marquage ProcessingFailed (ev={Id})", ev.Id);
                }
            }

            // -------- 6) Effets post-complétion, hors transaction --------
            if (processingError is null && completedPaymentId is int pid)
            {
                await _payinSettlement.RunPostCompletionEffectsAsync(pid, "webhook-wave");
            }

            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
            _logger.LogInformation(
                "[webhook/wave] OK event={EventId} type={Type} traité={Processed} ms={Elapsed:0}",
                envelope.Id, envelope.Type, processingError is null, elapsedMs);

            // Toujours 200 quand la signature est valide : un rejeu ne ferait que
            // rejouer le même défaut, déjà tracé en base.
            return Ok(new { received = true, processed = processingError is null });
        }

        // =================================================================
        // Traitement par type d'événement
        // =================================================================

        /// <summary>Renvoie l'identifiant du paiement passé à Completed, s'il y en a un.</summary>
        private async Task<int?> ProcessAsync(WaveWebhookEnvelope envelope)
        {
            var type = envelope.Type?.ToLowerInvariant();
            var data = envelope.Data;

            switch (type)
            {
                case "checkout.session.completed":
                    return await SettleCheckoutAsync(data, PaymentStatus.Completed);

                case "checkout.session.payment_failed":
                    await SettleCheckoutAsync(data, PaymentStatus.Failed);
                    return null;

                case "merchant.payment_received":
                    // Encaissement REÇU HORS de nos sessions (paiement marchand
                    // direct depuis l'application Wave, sans passer par Idara).
                    // Rien à régler : aucune facture ne l'attend. On le trace —
                    // la réconciliation le verra dans le registre, et c'est là
                    // qu'il doit être traité, pas ici.
                    _logger.LogInformation(
                        "[webhook/wave] paiement marchand hors session : id={Id} montant={Amount} frais={Fee}",
                        data?.Id, data?.Amount, data?.Fee);
                    return null;

                case "test.test_event":
                    _logger.LogInformation("[webhook/wave] événement de test reçu — chaîne bout en bout validée");
                    return null;

                default:
                    _logger.LogInformation("[webhook/wave] type non traité : {Type}", envelope.Type);
                    return null;
            }
        }

        private async Task<int?> SettleCheckoutAsync(WaveWebhookData? data, PaymentStatus terminalStatus)
        {
            if (data is null)
                throw new InvalidOperationException("Événement checkout sans données");

            // client_reference = notre Payment.Id, posé à la création de la session.
            if (!int.TryParse(data.ClientReference, NumberStyles.Integer, CultureInfo.InvariantCulture, out var paymentId))
            {
                throw new InvalidOperationException(
                    $"client_reference '{data.ClientReference}' non convertible en Payment.Id");
            }

            var charged = WaveClient.ParseAmount(data.Amount);

            // 🔑 Wave ne met PAS les frais dans l'événement de session. On les
            // dérive de la grille saisie (la même qui a servi à calculer ce
            // qu'on réclame), et la réconciliation quotidienne les confronte au
            // `fee` réel du registre. Ne JAMAIS inventer un taux ici : s'il n'est
            // pas configuré, on laisse zéro plutôt qu'une valeur fausse (§256).
            long fees = 0;
            if (terminalStatus == PaymentStatus.Completed && charged > 0)
            {
                var settings = await _context.PlatformSettings.AsNoTracking().FirstOrDefaultAsync();
                var grid = settings?.Fees;
                if (grid is { IsConfigured: true })
                    fees = grid.Value.PayinFeesFor(charged);
            }

            var net = Math.Max(0, charged - fees);
            var reason = data.LastPaymentError?.Code
                         ?? data.LastPaymentError?.Message
                         ?? data.PaymentStatus;

            var result = await _payinSettlement.SettleAsync(
                paymentId,
                terminalStatus,
                fees,
                net,
                data.Id,                    // identifiant de session cos-…
                data.WhenCompleted,
                reason,
                "webhook-wave");

            return result.Outcome == PayinSettlementOutcome.Transitioned
                   && result.FinalStatus == PaymentStatus.Completed
                ? paymentId
                : null;
        }

        // =================================================================
        // Vérification de signature
        // =================================================================

        public enum SignatureVerdict
        {
            Valid,
            /// <summary>En-tête qui ne respecte pas <c>t={ts},v1={sig}</c>.</summary>
            Malformed,
            /// <summary>Horodatage hors de la fenêtre de tolérance — rejeu probable.</summary>
            Expired,
            /// <summary>HMAC différent : mauvais secret, ou corps modifié en route.</summary>
            Mismatch,
            /// <summary>Aucun secret configuré côté serveur — on refuse tout.</summary>
            NotConfigured
        }

        /// <summary>
        /// Vérifie <c>Wave-Signature: t={unix},v1={hmac}</c> où le HMAC-SHA256
        /// porte sur <c>timestamp + corps brut</c>.
        /// </summary>
        /// <remarks>
        /// Public et statique pour être exerçable directement au banc d'essai :
        /// il n'existe aucun environnement de test chez Wave, donc la seule
        /// façon de voir cette fonction échouer AVANT la production est de
        /// l'appeler en test.
        /// </remarks>
        public static SignatureVerdict VerifySignature(
            string? header, string rawBody, string secret, int toleranceSeconds)
        {
            if (string.IsNullOrWhiteSpace(secret)) return SignatureVerdict.NotConfigured;
            if (string.IsNullOrWhiteSpace(header)) return SignatureVerdict.Malformed;

            string? timestamp = null;
            var candidates = new List<string>();

            foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var key = part[..eq].Trim();
                var value = part[(eq + 1)..].Trim();

                if (key == "t") timestamp = value;
                else if (key == "v1") candidates.Add(value);   // plusieurs v1 possibles pendant une rotation de secret
            }

            if (string.IsNullOrWhiteSpace(timestamp) || candidates.Count == 0)
                return SignatureVerdict.Malformed;

            if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
                return SignatureVerdict.Malformed;

            var age = Math.Abs((DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix)).TotalSeconds);
            if (age > toleranceSeconds) return SignatureVerdict.Expired;

            var expected = WaveClient.ComputeHmacHex(timestamp + rawBody, secret);
            var expectedBytes = TryHexDecode(expected);

            foreach (var candidate in candidates)
            {
                var actualBytes = TryHexDecode(candidate);
                if (actualBytes is null || expectedBytes is null) continue;
                if (actualBytes.Length != expectedBytes.Length) continue;
                // Comparaison à temps constant : une comparaison naïve laisse
                // deviner la signature octet par octet.
                if (CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes))
                    return SignatureVerdict.Valid;
            }

            return SignatureVerdict.Mismatch;
        }

        // =================================================================
        // Utilitaires
        // =================================================================

        private async Task TrySaveAuditAsync(
            string kind, string signatureHash, string rawBody,
            WebhookEventStatus status, string? error)
        {
            try
            {
                _context.WebhookEvents.Add(new WebhookEvent
                {
                    Provider = ProviderName,
                    // Pas d'identifiant d'événement digne de confiance ici : on
                    // fabrique une clé unique pour ne pas heurter l'index.
                    ExternalEventId = $"{kind}:{DateTime.UtcNow:yyyyMMddHHmmssfff}:{signatureHash[..8]}",
                    EventType = kind,
                    Payload = Truncate(rawBody),
                    SignatureHash = signatureHash,
                    ReceivedAt = DateTime.UtcNow,
                    Status = status,
                    ProcessingError = error
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[webhook/wave] Échec de l'écriture d'audit ({Kind})", kind);
            }
        }

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505";

        private static byte[]? TryHexDecode(string hex)
        {
            try { return Convert.FromHexString(hex); }
            catch (FormatException) { return null; }
        }

        private static string Sha256Hex(string input) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

        private static string Truncate(string s, int max = 8000) =>
            s.Length <= max ? s : s[..max] + "…";
    }
}
