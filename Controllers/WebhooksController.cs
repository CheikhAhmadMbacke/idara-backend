using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Senepay;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Idara.API.Services;
using Idara.API.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Endpoints publics (anonymes) recevant les notifications signées des PSPs.
    /// Sécurité = signature HMAC obligatoire + idempotence stricte (cf. gotchas
    /// §50, §51). Aucune authentification utilisateur, aucun CORS pertinent —
    /// les appels viennent de IPs serveurs SenePay.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/webhooks/senepay")]
    public class WebhooksController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly SenePaySettings _senepay;
        private readonly IPayinSettlementService _payinSettlement;
        private readonly IPayoutSettlementService _settlement;
        private readonly ILogger<WebhooksController> _logger;

        // Constantes du provider — figées dans le code pour empêcher une typo
        // dans la config de casser silencieusement l'idempotence multi-PSP.
        private const string ProviderName = "SenePay";

        // Statuts payin SenePay tels qu'envoyés dans le payload. ATTENTION :
        // "Complete" sans 'd' final (doc §4 note ligne 304). On compare en
        // case-insensitive pour tolérer une future normalisation côté SenePay.
        private static readonly string[] StatusSuccess = { "Complete", "Completed" };
        private static readonly string[] StatusFailed = { "Failed" };
        private static readonly string[] StatusCancelled = { "Cancelled" };
        private static readonly string[] StatusExpired = { "Expired" };

        public WebhooksController(
            AppDbContext context,
            IOptions<SenePaySettings> senepay,
            IPayinSettlementService payinSettlement,
            IPayoutSettlementService settlement,
            ILogger<WebhooksController> logger)
        {
            _context = context;
            _senepay = senepay.Value;
            _payinSettlement = payinSettlement;
            _settlement = settlement;
            _logger = logger;
        }

        /// <summary>
        /// Webhook payin SenePay (Checkout et API Direct partagent le même
        /// format). On répond TOUJOURS 200 le plus tôt possible — sauf 401
        /// signature invalide — pour éviter les retry SenePay (~1s/5s/30s)
        /// même quand notre traitement métier déraille : un retry ne ferait
        /// que ré-essayer un bug déjà loggé.
        /// </summary>
        [HttpPost("payin")]
        public async Task<IActionResult> HandlePayin()
        {
            var startedAt = DateTime.UtcNow;

            // -------- 1) Lecture du CORPS BRUT (avant tout parsing JSON) --------
            // La signature HMAC porte sur les bytes exacts envoyés par SenePay.
            // Si on parse + re-sérialise, on perd les espaces, l'ordre des
            // clés, l'encodage des unicode escapes — la signature ne match plus.
            string rawBody;
            try
            {
                Request.EnableBuffering();
                using var reader = new StreamReader(
                    Request.Body, Encoding.UTF8, leaveOpen: true);
                rawBody = await reader.ReadToEndAsync();
                Request.Body.Position = 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[webhook/payin] Échec lecture body brut");
                // Sans body, on ne peut rien faire. 400 = SenePay arrête de retry.
                return BadRequest(new { error = "Unable to read body" });
            }

            // -------- 2) Récupération de la signature et de l'événement --------
            var signatureHeader = Request.Headers["X-SenePay-Signature"].FirstOrDefault();
            var eventHeader = Request.Headers["X-SenePay-Event"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(signatureHeader))
            {
                _logger.LogWarning(
                    "[webhook/payin] Header X-SenePay-Signature manquant (rawBytes={Len})",
                    rawBody.Length);
                return Unauthorized(new { error = "Missing signature header" });
            }

            // -------- 3) Vérification HMAC-SHA256 sur corps brut --------
            // Constant-time comparison pour bloquer les attaques par timing.
            var expectedSignature = ComputeHmacHex(rawBody, _senepay.WebhookSecret);
            var actualBytes = TryHexDecode(signatureHeader);
            var expectedBytes = TryHexDecode(expectedSignature);

            var signatureValid =
                actualBytes != null
                && expectedBytes != null
                && actualBytes.Length == expectedBytes.Length
                && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);

            // Hash audit-only de la signature reçue (pour pouvoir tracer un
            // pattern d'attaque sans stocker la signature en clair).
            var signatureHash = Sha256Hex(signatureHeader);

            if (!signatureValid)
            {
                // On enregistre quand même l'event pour audit, status = InvalidSignature.
                // Pas de unique violation possible car on n'utilise pas le
                // transactionId du payload (on ne lui fait pas confiance avant
                // signature valide). On utilise un slug uniqueisé par (now, hash sig).
                _logger.LogWarning(
                    "[webhook/payin] SIGNATURE INVALIDE — rejected. event={Event} sigHash={SigHash} rawBytes={Len}",
                    eventHeader, signatureHash, rawBody.Length);

                await TrySaveInvalidSignatureAuditAsync(eventHeader, signatureHash, rawBody);
                return Unauthorized(new { error = "Invalid signature" });
            }

            // -------- 4) Parsing du payload (maintenant qu'on lui fait confiance) --------
            SenePayPayinWebhookPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<SenePayPayinWebhookPayload>(
                    rawBody,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "[webhook/payin] Signature OK mais body JSON invalide. event={Event} rawBytes={Len}",
                    eventHeader, rawBody.Length);
                await TrySaveMalformedAuditAsync(eventHeader, signatureHash, rawBody, ex.Message);
                // 200 pour ne pas que SenePay retry sur un body cassé.
                return Ok(new { received = true, processed = false, reason = "malformed_json" });
            }

            if (payload == null || string.IsNullOrWhiteSpace(payload.TransactionId))
            {
                _logger.LogWarning(
                    "[webhook/payin] Payload sans transactionId — impossible d'idempotenter. event={Event}",
                    eventHeader);
                await TrySaveMalformedAuditAsync(eventHeader, signatureHash, rawBody, "missing_transactionId");
                return Ok(new { received = true, processed = false, reason = "missing_transactionId" });
            }

            // -------- 5) Insertion IDEMPOTENTE de WebhookEvent --------
            // L'unique index (Provider, ExternalEventId) joue le rôle de
            // dedupe : si SenePay retry, le 2e INSERT échoue → on retourne 200
            // sans rejouer le traitement métier.
            var ev = new WebhookEvent
            {
                Provider = ProviderName,
                ExternalEventId = payload.TransactionId,
                EventType = payload.Event ?? eventHeader ?? "unknown",
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
                // Détecter doublon = succès idempotent attendu.
                _context.Entry(ev).State = EntityState.Detached;
                _logger.LogInformation(
                    "[webhook/payin] DUPLICATE — transactionId={Tx} event={Event} déjà traité",
                    payload.TransactionId, payload.Event);
                return Ok(new { received = true, processed = false, duplicate = true });
            }

            // -------- 6) Règlement métier --------
            // Délégué à IPayinSettlementService (transaction + verrou pessimiste
            // wallet + garde Status==Pending) : SOURCE UNIQUE partagée avec le
            // PayinVerificationJob. On NE wrappe PAS ici (le service possède sa
            // propre transaction) — même schéma que le payout. L'idempotence
            // webhook reste garantie par l'INSERT unique WebhookEvent ci-dessus,
            // et l'idempotence métier par la garde de statut dans le service.
            string? processingError = null;
            int? completedPaymentId = null;
            try
            {
                completedPaymentId = await SettlePayinAsync(payload);
                ev.ProcessedAt = DateTime.UtcNow;
                ev.Status = WebhookEventStatus.Processed;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                processingError = ex.Message;
                _logger.LogError(ex,
                    "[webhook/payin] ÉCHEC TRAITEMENT métier transactionId={Tx} event={Event}",
                    payload.TransactionId, payload.Event);
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
                    _logger.LogError(innerEx,
                        "[webhook/payin] Échec mise à jour Status=ProcessingFailed sur ev.Id={Id}", ev.Id);
                    // Never throw from a webhook endpoint.
                }
            }

            // -------- 7) Effets post-complétion (best-effort, hors transaction) --------
            // Reçu PDF + SMS parent + push école + retry abonnement. Délégué au
            // service (même code que le PayinVerificationJob). Ne lève jamais.
            // Garde processingError==null : si le règlement a échoué/rollback, on
            // ne notifie/génère rien (sinon fausse confirmation de paiement).
            if (processingError == null && completedPaymentId is int pid)
            {
                await _payinSettlement.RunPostCompletionEffectsAsync(pid, "webhook");
            }

            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
            _logger.LogInformation(
                "[webhook/payin] OK transactionId={Tx} event={Event} status={Status} processed={Processed} elapsedMs={Elapsed:0}",
                payload.TransactionId,
                payload.Event,
                payload.Status,
                processingError == null,
                elapsedMs);

            // On répond TOUJOURS 200 quand la signature est valide : retry SenePay
            // ne ferait que rejouer le même bug. L'admin verra ProcessingFailed
            // dans WebhookEvent et pourra rejouer manuellement.
            return Ok(new { received = true, processed = processingError == null });
        }

        /// <summary>
        /// Webhook payout SenePay (`disbursement.completed` / `.failed`). Même
        /// schéma que le payin : HMAC sur corps brut, idempotence stricte via
        /// WebhookEvent (ExternalEventId = disbursement_id), traitement en
        /// transaction PG. Toujours 200 si signature valide (sauf 401/400).
        /// </summary>
        [HttpPost("payout")]
        public async Task<IActionResult> HandlePayout()
        {
            var startedAt = DateTime.UtcNow;

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
                _logger.LogError(ex, "[webhook/payout] Échec lecture body brut");
                return BadRequest(new { error = "Unable to read body" });
            }

            var signatureHeader = Request.Headers["X-SenePay-Signature"].FirstOrDefault();
            var eventHeader = Request.Headers["X-SenePay-Event"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(signatureHeader))
            {
                _logger.LogWarning("[webhook/payout] Header X-SenePay-Signature manquant");
                return Unauthorized(new { error = "Missing signature header" });
            }

            var expectedSignature = ComputeHmacHex(rawBody, _senepay.WebhookSecret);
            var actualBytes = TryHexDecode(signatureHeader);
            var expectedBytes = TryHexDecode(expectedSignature);
            var signatureValid =
                actualBytes != null && expectedBytes != null
                && actualBytes.Length == expectedBytes.Length
                && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
            var signatureHash = Sha256Hex(signatureHeader);

            if (!signatureValid)
            {
                _logger.LogWarning(
                    "[webhook/payout] SIGNATURE INVALIDE — rejected. event={Event} sigHash={SigHash}",
                    eventHeader, signatureHash);
                await TrySaveInvalidSignatureAuditAsync(eventHeader, signatureHash, rawBody, "payout");
                return Unauthorized(new { error = "Invalid signature" });
            }

            SenePayPayoutWebhookPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<SenePayPayoutWebhookPayload>(
                    rawBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "[webhook/payout] Signature OK mais JSON invalide. event={Event}", eventHeader);
                await TrySaveMalformedAuditAsync(eventHeader, signatureHash, rawBody, ex.Message, "payout");
                return Ok(new { received = true, processed = false, reason = "malformed_json" });
            }

            if (payload == null || string.IsNullOrWhiteSpace(payload.DisbursementId))
            {
                _logger.LogWarning("[webhook/payout] Payload sans disbursement_id — impossible d'idempotenter.");
                await TrySaveMalformedAuditAsync(eventHeader, signatureHash, rawBody, "missing_disbursement_id", "payout");
                return Ok(new { received = true, processed = false, reason = "missing_disbursement_id" });
            }

            var ev = new WebhookEvent
            {
                Provider = ProviderName,
                // Préfixe "payout:" pour garantir qu'un disbursement_id ne
                // collisionne jamais avec un transaction_id payin sur l'unique
                // (Provider, ExternalEventId) — espaces de nommage SenePay non
                // garantis disjoints par contrat (cf. revue Phase 3, m1).
                ExternalEventId = "payout:" + payload.DisbursementId,
                EventType = payload.Event ?? eventHeader ?? "unknown",
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
                    "[webhook/payout] DUPLICATE — disbursementId={Disb} déjà traité", payload.DisbursementId);
                return Ok(new { received = true, processed = false, duplicate = true });
            }

            string? processingError = null;
            try
            {
                // Le IPayoutSettlementService gère sa PROPRE transaction (verrou
                // pessimiste wallet) — on n'en ouvre donc PAS ici, sinon Npgsql
                // lèverait sur une transaction imbriquée. L'idempotence du webhook
                // est déjà garantie par l'INSERT unique WebhookEvent ci-dessus, et
                // celle du règlement par les gardes de statut dans le service.
                await ProcessPayoutPayloadAsync(payload);
                ev.ProcessedAt = DateTime.UtcNow;
                ev.Status = WebhookEventStatus.Processed;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                processingError = ex.Message;
                _logger.LogError(ex,
                    "[webhook/payout] ÉCHEC TRAITEMENT disbursementId={Disb} event={Event}",
                    payload.DisbursementId, payload.Event);
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
                    _logger.LogError(innerEx, "[webhook/payout] Échec maj ProcessingFailed ev.Id={Id}", ev.Id);
                }
            }

            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
            _logger.LogInformation(
                "[webhook/payout] OK disbursementId={Disb} event={Event} status={Status} processed={Processed} elapsedMs={Elapsed:0}",
                payload.DisbursementId, payload.Event, payload.Status, processingError == null, elapsedMs);

            return Ok(new { received = true, processed = processingError == null });
        }

        // ====================================================================
        // ===== Traitement métier =====
        // ====================================================================

        /// <summary>
        /// Mappe le payload webhook payin → appel <see cref="IPayinSettlementService"/>.
        /// Retourne le Payment.Id si le règlement vient de transiter vers
        /// Completed (→ effets post-complétion à déclencher), sinon null.
        /// Lève si l'OrderId n'est pas parsable ou si le Payment est introuvable
        /// (webhook reçu avant le commit de l'initiate) → ev en ProcessingFailed
        /// pour rejeu.
        /// </summary>
        private async Task<int?> SettlePayinAsync(SenePayPayinWebhookPayload payload)
        {
            // OrderId = Payment.Id sérialisé (envoyé comme `orderId` à l'initiate).
            if (!int.TryParse(payload.OrderId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var paymentId))
            {
                throw new InvalidOperationException(
                    $"OrderId '{payload.OrderId}' non parsable en Payment.Id");
            }

            var terminalStatus = MapSenePayStatus(payload.Status);

            // SenePay envoie les montants en decimal (200.0 / 196.0). En XOF, pas
            // de centimes — on arrondit au long sans perte.
            var fees = (long)Math.Round(payload.Fees, MidpointRounding.AwayFromZero);
            var net = (long)Math.Round(payload.NetAmount, MidpointRounding.AwayFromZero);
            var reason = payload.FailedReason ?? payload.ErrorCode ?? payload.Status;

            var result = await _payinSettlement.SettleAsync(
                paymentId,
                terminalStatus,
                fees,
                net,
                payload.TransactionId,
                payload.Timestamp?.ToUtcSafe(),
                reason,
                "webhook");

            return result.Outcome == PayinSettlementOutcome.Transitioned
                   && result.FinalStatus == PaymentStatus.Completed
                ? paymentId
                : null;
        }

        /// <summary>
        /// Clôt un retrait depuis le webhook payout. external_id = Withdrawal.Id.
        /// Délègue à <see cref="IPayoutSettlementService"/> (verrou pessimiste,
        /// idempotence, et webhook CORRECTEUR : un `completed` arrivant sur un
        /// retrait déjà Failed annule la restitution au lieu d'être ignoré).
        /// </summary>
        private async Task ProcessPayoutPayloadAsync(SenePayPayoutWebhookPayload payload)
        {
            if (!int.TryParse(payload.ExternalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var withdrawalId))
            {
                throw new InvalidOperationException(
                    $"external_id '{payload.ExternalId}' non parsable en Withdrawal.Id");
            }

            var statusLower = payload.Status?.ToLowerInvariant();

            switch (statusLower)
            {
                case "completed":
                    await _settlement.SettleCompletedAsync(
                        withdrawalId, payload.DisbursementId,
                        (long)Math.Round(payload.Fees?.Provider ?? 0, MidpointRounding.AwayFromZero),
                        (long)Math.Round(payload.NetAmount, MidpointRounding.AwayFromZero),
                        payload.CompletedAt ?? payload.Timestamp, "webhook");
                    break;

                case "failed":
                case "cancelled":
                    await _settlement.SettleFailedAsync(
                        withdrawalId,
                        payload.ErrorMessage ?? payload.ErrorCode ?? statusLower,
                        payload.DisbursementId, payload.Timestamp, "webhook");
                    break;

                default:
                    // Webhook avec un statut NON terminal (inhabituel — SenePay
                    // envoie normalement completed/failed). On ne touche pas aux
                    // fonds : le PayoutVerificationJob tranchera via GET status.
                    _logger.LogInformation(
                        "[webhook/payout] Withdrawal.Id={Id} statut non terminal '{Status}' (event={Event}) — ignoré, le poll tranchera",
                        withdrawalId, payload.Status, payload.Event);
                    break;
            }
        }

        // ====================================================================
        // ===== Helpers =====
        // ====================================================================

        private PaymentStatus MapSenePayStatus(string? status)
        {
            if (StatusSuccess.Any(s => s.Equals(status, StringComparison.OrdinalIgnoreCase)))
                return PaymentStatus.Completed;
            if (StatusFailed.Any(s => s.Equals(status, StringComparison.OrdinalIgnoreCase)))
                return PaymentStatus.Failed;
            if (StatusCancelled.Any(s => s.Equals(status, StringComparison.OrdinalIgnoreCase)))
                return PaymentStatus.Cancelled;
            if (StatusExpired.Any(s => s.Equals(status, StringComparison.OrdinalIgnoreCase)))
                return PaymentStatus.Expired;

            throw new InvalidOperationException(
                $"Statut SenePay non reconnu : '{status}'. Si SenePay introduit un nouveau statut, étendre WebhooksController.MapSenePayStatus.");
        }

        private static string ComputeHmacHex(string body, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static byte[]? TryHexDecode(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            // Tolère les casse mixte ; rejette les caractères non-hex.
            if (hex.Length % 2 != 0) return null;
            try { return Convert.FromHexString(hex); }
            catch (FormatException) { return null; }
        }

        private static string Sha256Hex(string input)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static bool IsUniqueViolation(DbUpdateException ex)
        {
            // Npgsql expose le SqlState PG via l'inner PostgresException.
            // 23505 = unique_violation.
            return ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505";
        }

        /// <summary>
        /// Audit d'une signature invalide. On stocke avec un ExternalEventId
        /// fabriqué (ne risque PAS de collisionner avec un vrai transactionId
        /// car les vrais commencent par "SENEPAY_PAYIN_").
        /// </summary>
        private async Task TrySaveInvalidSignatureAuditAsync(
            string? eventHeader, string signatureHash, string rawBody, string source = "payin")
        {
            try
            {
                var fakeId = $"INVALID_SIG_{source.ToUpperInvariant()}_{DateTime.UtcNow.Ticks}_{signatureHash[..8]}";
                _context.WebhookEvents.Add(new WebhookEvent
                {
                    Provider = ProviderName,
                    ExternalEventId = fakeId,
                    EventType = eventHeader ?? "unknown",
                    // SafePayload : un attaquant peut envoyer n'importe quel
                    // octet — on enveloppe pour respecter la contrainte jsonb.
                    Payload = SafePayload(rawBody, "invalid_signature"),
                    SignatureHash = signatureHash,
                    ReceivedAt = DateTime.UtcNow,
                    Status = WebhookEventStatus.InvalidSignature,
                    ProcessingError = "HMAC signature verification failed"
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[webhook/payin] Échec audit signature invalide");
            }
        }

        private async Task TrySaveMalformedAuditAsync(
            string? eventHeader, string signatureHash, string rawBody, string reason, string source = "payin")
        {
            try
            {
                var fakeId = $"MALFORMED_{source.ToUpperInvariant()}_{DateTime.UtcNow.Ticks}_{signatureHash[..8]}";
                _context.WebhookEvents.Add(new WebhookEvent
                {
                    Provider = ProviderName,
                    ExternalEventId = fakeId,
                    EventType = eventHeader ?? "unknown",
                    // SafePayload : par définition, rawBody n'est pas du JSON
                    // valide sur ce chemin — on enveloppe pour le jsonb.
                    Payload = SafePayload(rawBody, reason),
                    SignatureHash = signatureHash,
                    ReceivedAt = DateTime.UtcNow,
                    Status = WebhookEventStatus.ProcessingFailed,
                    ProcessingError = reason
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[webhook/payin] Échec audit malformed");
            }
        }

        /// <summary>
        /// Enveloppe un corps brut potentiellement non-JSON dans un objet JSON
        /// valide, pour qu'il passe la contrainte du type PG `jsonb`. Utilisé
        /// uniquement sur les chemins défensifs (signature invalide, JSON
        /// malformé) — le happy path stocke le rawBody tel quel puisqu'il
        /// vient d'être parsé avec succès.
        /// </summary>
        private static string SafePayload(string rawBody, string reason)
        {
            return JsonSerializer.Serialize(new
            {
                rawBody,
                reason,
                envelopedAt = DateTime.UtcNow
            });
        }
    }
}
