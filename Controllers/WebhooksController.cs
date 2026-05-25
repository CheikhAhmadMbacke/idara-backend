using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.DTOs.Senepay;
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
        private readonly IReceiptPdfService _receiptPdf;
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
            IReceiptPdfService receiptPdf,
            ILogger<WebhooksController> logger)
        {
            _context = context;
            _senepay = senepay.Value;
            _receiptPdf = receiptPdf;
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

            // -------- 6) Traitement métier dans une transaction PG --------
            string? processingError = null;
            Payment? completedPayment = null;
            try
            {
                await using var tx = await _context.Database.BeginTransactionAsync();
                completedPayment = await ProcessPayinPayloadAsync(ev, payload);
                ev.ProcessedAt = DateTime.UtcNow;
                ev.Status = WebhookEventStatus.Processed;
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                processingError = ex.Message;
                _logger.LogError(ex,
                    "[webhook/payin] ÉCHEC TRAITEMENT métier transactionId={Tx} event={Event}",
                    payload.TransactionId, payload.Event);
                // La transaction métier a rollback automatiquement (using await tx).
                // ATTENTION : le change tracker EF a encore les valeurs in-memory
                // que la tx a annulées (ev.ProcessedAt, ev.Status...). Un simple
                // SaveChanges renverrait l'UPDATE avec ces valeurs périmées.
                // On bypass via ExecuteUpdateAsync — UPDATE directe en DB,
                // change tracker ignoré.
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

            // -------- 7) Génération du reçu PDF (best-effort, post-commit) --------
            // Volontairement HORS de la transaction métier : un échec de
            // génération PDF (disque plein, font manquante, exception
            // QuestPDF...) ne doit PAS rollback le crédit wallet ni faire
            // remonter en erreur le webhook. Le PDF peut toujours être
            // regénéré à la demande via GET /api/payments/{id}/receipt.
            if (completedPayment != null && completedPayment.Status == PaymentStatus.Completed)
            {
                try
                {
                    var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == completedPayment.SchoolId);
                    var student = completedPayment.StudentId.HasValue
                        ? await _context.Students.FirstOrDefaultAsync(x => x.Id == completedPayment.StudentId.Value)
                        : null;
                    var invoice = completedPayment.InvoiceId.HasValue
                        ? await _context.Invoices.FirstOrDefaultAsync(x => x.Id == completedPayment.InvoiceId.Value)
                        : null;
                    if (school != null)
                    {
                        var pdfPath = await _receiptPdf.GenerateAsync(completedPayment, school, student, invoice);
                        // ExecuteUpdate pour bypass change tracker (la tx précédente est déjà commitée
                        // mais on veut juste poser le chemin sans repasser par les autres champs).
                        await _context.Payments
                            .Where(p => p.Id == completedPayment.Id)
                            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ReceiptPdfPath, pdfPath));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[webhook/payin] Échec génération reçu PDF Payment.Id={Id} — pas bloquant",
                        completedPayment.Id);
                }
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

        // ====================================================================
        // ===== Traitement métier =====
        // ====================================================================

        /// <summary>
        /// Retourne le Payment complété (ou null si le webhook n'a rien
        /// changé : statut non-success, déjà traité, etc.). Utilisé par
        /// l'appelant pour décider s'il doit générer le reçu PDF post-commit.
        /// </summary>
        private async Task<Payment?> ProcessPayinPayloadAsync(
            WebhookEvent ev,
            SenePayPayinWebhookPayload payload)
        {
            // OrderId = Payment.Id sérialisé (cf. 1.4 — on l'envoie comme
            // `orderId` à SenePay dans /payments/initiate, et SenePay nous le
            // renvoie dans `order_id` du webhook).
            if (!int.TryParse(payload.OrderId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var paymentId))
            {
                throw new InvalidOperationException(
                    $"OrderId '{payload.OrderId}' non parsable en Payment.Id");
            }

            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.Id == paymentId);

            if (payment == null)
            {
                // Cas plausible : webhook reçu avant que /initiate ait commit le
                // Payment côté DB (latence Postgres). On lève — la transaction
                // métier rollback, ev passe en ProcessingFailed, l'admin peut
                // rejouer. On ne crée PAS un Payment fantôme depuis le webhook.
                throw new InvalidOperationException(
                    $"Payment.Id={paymentId} introuvable pour ce webhook");
            }

            // Idempotence "métier" : si on a déjà traité un webhook pour ce
            // Payment (Status non-Pending), on ne rejoue rien — webhook tardif
            // ou doublon logique côté SenePay.
            if (payment.Status != PaymentStatus.Pending)
            {
                _logger.LogInformation(
                    "[webhook/payin] Payment.Id={Id} déjà en statut {Status}, webhook ignoré",
                    payment.Id, payment.Status);
                return null;
            }

            // Map du statut SenePay → notre enum.
            var newStatus = MapSenePayStatus(payload.Status);

            // SenePay envoie les montants en decimal (200.0 / 196.0 / 4.0). En
            // XOF (FCFA), pas de centimes — on tronque vers long sans perte.
            // Math.Round par sécurité au cas où SenePay enverrait 195.9999.
            payment.FeesFcfa = (long)Math.Round(payload.Fees, MidpointRounding.AwayFromZero);
            payment.NetCreditedFcfa = (long)Math.Round(payload.NetAmount, MidpointRounding.AwayFromZero);
            payment.SenePayTransactionId = payload.TransactionId;
            payment.Status = newStatus;

            switch (newStatus)
            {
                case PaymentStatus.Completed:
                    payment.PaidAt = payload.Timestamp?.ToUtcSafe() ?? DateTime.UtcNow;
                    await CreditSchoolWalletAsync(payment, payment.NetCreditedFcfa);
                    break;

                case PaymentStatus.Failed:
                case PaymentStatus.Cancelled:
                case PaymentStatus.Expired:
                    payment.FailedAt = payload.Timestamp?.ToUtcSafe() ?? DateTime.UtcNow;
                    payment.FailureReason = payload.FailedReason ?? payload.ErrorCode ?? payload.Status;
                    // Rien à débiter — le wallet n'a jamais été crédité.
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Statut SenePay inattendu '{payload.Status}'");
            }

            return payment;
        }

        private async Task CreditSchoolWalletAsync(Payment payment, long netAmount)
        {
            if (netAmount <= 0)
            {
                _logger.LogWarning(
                    "[webhook/payin] netAmount={Net} <= 0 pour Payment.Id={Id}, pas de crédit wallet",
                    netAmount, payment.Id);
                return;
            }

            // SchoolWallet est seedé pour chaque école par DbInitializer (1.2).
            // Si jamais absent (race ou bug), on lève — on ne crée pas un
            // wallet à la volée depuis un webhook (audit trail).
            var wallet = await _context.SchoolWallets
                .FirstOrDefaultAsync(w => w.SchoolId == payment.SchoolId)
                ?? throw new InvalidOperationException(
                    $"SchoolWallet manquant pour SchoolId={payment.SchoolId}");

            wallet.AvailableBalance += netAmount;
            wallet.TotalCreditedLifetime += netAmount;
            wallet.UpdatedAt = DateTime.UtcNow;

            var txEntry = new WalletTransaction
            {
                SchoolId = payment.SchoolId,
                Type = WalletTransactionType.Credit,
                AmountFcfa = netAmount, // Crédit = montant signé positif.
                BalanceAfter = wallet.AvailableBalance,
                RelatedEntity = WalletRelatedEntity.Payment,
                RelatedId = payment.Id,
                Note = $"Payment {payment.SenePayTransactionId}",
                OccurredAt = DateTime.UtcNow
            };
            _context.WalletTransactions.Add(txEntry);

            // Si une Invoice est rattachée (mode FixedAmount), on met à jour
            // son AmountPaidFcfa et son statut (paid/over-paid).
            //
            // ATTENTION : on crédite l'invoice avec TargetAmountFcfa (montant
            // cible original), PAS avec netAmount. En FeesPayer=Parent, on a
            // chargé targetAmount × 1.08 — la majoration de 8% est censée
            // couvrir les frais SenePay (~5,37%). Le wallet reçoit le net
            // (~196 FCFA pour une cible de 200), mais l'invoice doit
            // considérer les 200 comme payés sinon elle reste éternellement
            // "presque payée" avec ~4 FCFA fantômes. Fallback sur netAmount
            // pour les anciens Payments d'avant 1.10 qui n'ont pas le champ.
            if (payment.InvoiceId is int invoiceId)
            {
                var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
                if (invoice != null)
                {
                    var creditedToInvoice = payment.TargetAmountFcfa > 0
                        ? payment.TargetAmountFcfa
                        : netAmount;
                    invoice.AmountPaidFcfa += creditedToInvoice;
                    invoice.UpdatedAt = DateTime.UtcNow;
                    if (invoice.AmountPaidFcfa >= invoice.AmountDueFcfa)
                    {
                        invoice.Status = InvoiceStatus.Paid;
                    }
                }
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
            string? eventHeader, string signatureHash, string rawBody)
        {
            try
            {
                var fakeId = $"INVALID_SIG_{DateTime.UtcNow.Ticks}_{signatureHash[..8]}";
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
            string? eventHeader, string signatureHash, string rawBody, string reason)
        {
            try
            {
                var fakeId = $"MALFORMED_{DateTime.UtcNow.Ticks}_{signatureHash[..8]}";
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
