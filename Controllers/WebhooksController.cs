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
        private readonly IReceiptPdfService _receiptPdf;
        private readonly IPayoutSettlementService _settlement;
        private readonly Services.Notifications.INotificationService _notif;
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
            IPayoutSettlementService settlement,
            Services.Notifications.INotificationService notif,
            ILogger<WebhooksController> logger)
        {
            _context = context;
            _senepay = senepay.Value;
            _receiptPdf = receiptPdf;
            _settlement = settlement;
            _notif = notif;
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

            // -------- 8) SMS « paiement reçu » au responsable (best-effort) --------
            // Bloc séparé du PDF : un échec PDF ne doit pas priver le parent du
            // SMS, et inversement. NotificationService ne lève jamais, mais on
            // enveloppe quand même par principe (lecture DB du guardian/élève).
            if (completedPayment != null
                && completedPayment.Status == PaymentStatus.Completed
                && completedPayment.GuardianId.HasValue)
            {
                try
                {
                    var guardian = await _context.Users.FirstOrDefaultAsync(
                        u => u.Id == completedPayment.GuardianId.Value && !u.IsDeleted);
                    if (guardian?.PhoneNumber != null)
                    {
                        var student = completedPayment.StudentId.HasValue
                            ? await _context.Students.FirstOrDefaultAsync(x => x.Id == completedPayment.StudentId.Value)
                            : null;
                        var eleve = student != null
                            ? $"{student.FirstName} {student.LastName}".Trim()
                            : "votre enfant";
                        var platform = await _context.GetPlatformSettingsAsync();
                        // Montant CIBLE (ce que la facture considère payé), pas le
                        // débité parent (cible×1,08) — cohérent avec le reçu/facture
                        // (§82). Fallback AmountFcfa pour les anciens Payments.
                        var shownAmount = completedPayment.TargetAmountFcfa > 0
                            ? completedPayment.TargetAmountFcfa
                            : completedPayment.AmountFcfa;
                        var msg = NotificationTemplates.PaymentReceived(eleve, shownAmount);
                        await _notif.SendSmsAsync(new NotificationSmsRequest(
                            UserId: guardian.Id,
                            RawPhone: guardian.PhoneNumber,
                            PreferredLanguage: guardian.PreferredLanguage ?? "fr",
                            Message: msg,
                            Bilingual: platform.SmsBilingual,
                            TemplateCode: "PAYMENT_RECEIVED",
                            RelatedEntityId: completedPayment.Id,
                            PushRoute: "/guardian/invoices"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[webhook/payin] Échec SMS paiement reçu Payment.Id={Id} — pas bloquant",
                        completedPayment.Id);
                }
            }

            // -------- 9) Push « paiement reçu » à l'ÉCOLE (admin + personnel) --------
            // Push uniquement (pas de SMS : l'école recevrait trop de SMS payants).
            // Clic → page solde/wallet. Idempotent via le webhook (1 traitement/payment).
            if (completedPayment != null
                && completedPayment.Status == PaymentStatus.Completed
                && completedPayment.SchoolId > 0)
            {
                try
                {
                    var schoolUserIds = await _context.Users
                        .Where(u => u.SchoolId == completedPayment.SchoolId
                                    && !u.IsDeleted
                                    && (u.Role == UserRoles.SchoolAdmin || u.Role == UserRoles.SchoolStaff))
                        .Select(u => new { u.Id, u.PreferredLanguage })
                        .ToListAsync();
                    if (schoolUserIds.Count > 0)
                    {
                        var student = completedPayment.StudentId.HasValue
                            ? await _context.Students.FirstOrDefaultAsync(x => x.Id == completedPayment.StudentId.Value)
                            : null;
                        var eleve = student != null ? $"{student.FirstName} {student.LastName}".Trim() : "un eleve";
                        var shownAmount = completedPayment.TargetAmountFcfa > 0
                            ? completedPayment.TargetAmountFcfa
                            : completedPayment.AmountFcfa;
                        var msg = NotificationTemplates.PaymentReceivedSchool(eleve, shownAmount);
                        foreach (var su in schoolUserIds)
                        {
                            await _notif.SendPushOnlyAsync(new PushOnlyRequest(
                                UserId: su.Id,
                                PreferredLanguage: su.PreferredLanguage ?? "fr",
                                Message: msg,
                                TemplateCode: "SCHOOL_PAYMENT_RECEIVED",
                                RelatedEntityId: completedPayment.Id,
                                PushRoute: "/payments/overview"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[webhook/payin] Échec push école Payment.Id={Id} — pas bloquant",
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
                    await CreditSchoolWalletAsync(payment);
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

        private async Task CreditSchoolWalletAsync(Payment payment)
        {
            var netAmount = payment.NetCreditedFcfa;

            // Montant crédité au wallet école — dépend de QUI porte les frais :
            //
            //  • FeesPayer=Parent : on crédite le montant CIBLE (TargetAmountFcfa,
            //    ce que l'école a fixé), PAS le net reçu de SenePay. Le parent a
            //    payé target × 1,08 ; la majoration de 8% couvre les frais payin
            //    (5,37%) ET la réserve du futur payout (1,77%). L'école reçoit
            //    donc EXACTEMENT ce qu'elle a fixé, en payin comme au retrait
            //    (décision 2026-06-07 : les Daara reçoivent toujours leur montant
            //    fixé, ni plus ni moins). L'excédent (net réel − target) reste
            //    dans la réserve marchand SenePay : c'est le coussin qui finance
            //    les frais de payout et la marge plateforme. Sans ce choix, le
            //    wallet recevrait le net (variable, ex 529 pour une cible de 500)
            //    et l'école toucherait un surplus imprévisible.
            //
            //  • FeesPayer=School : l'école absorbe les frais à la source (spec
            //    §4.4) → on crédite le NET réellement reversé par SenePay. Le
            //    wallet reflète le net post-frais, c'est le principe de ce mode.
            //    Créditer le target ici sur-créditerait le wallet vs la réserve
            //    marchand réelle → casserait l'invariant de réconciliation §78.
            //
            //  Fallback sur netAmount pour les anciens Payments d'avant 1.10 qui
            //  n'ont pas TargetAmountFcfa rempli (= 0).
            var amountToCredit = payment.FeesPayer == FeesPayer.Parent && payment.TargetAmountFcfa > 0
                ? payment.TargetAmountFcfa
                : netAmount;

            if (amountToCredit <= 0)
            {
                _logger.LogWarning(
                    "[webhook/payin] montant à créditer={Amount} <= 0 pour Payment.Id={Id} (net={Net}, target={Target}, feesPayer={FeesPayer}), pas de crédit wallet",
                    amountToCredit, payment.Id, netAmount, payment.TargetAmountFcfa, payment.FeesPayer);
                return;
            }

            // SchoolWallet est seedé pour chaque école par DbInitializer (1.2).
            // Si jamais absent (race ou bug), on lève — on ne crée pas un
            // wallet à la volée depuis un webhook (audit trail).
            // Verrou pessimiste : sérialise vs un retrait concurrent sur le même
            // wallet (cf. WalletLockExtensions). Évite tout lost update Available.
            var wallet = await _context.LockWalletAsync(payment.SchoolId)
                ?? throw new InvalidOperationException(
                    $"SchoolWallet manquant pour SchoolId={payment.SchoolId}");

            wallet.AvailableBalance += amountToCredit;
            wallet.TotalCreditedLifetime += amountToCredit;
            wallet.UpdatedAt = DateTime.UtcNow;

            var txEntry = new WalletTransaction
            {
                SchoolId = payment.SchoolId,
                Type = WalletTransactionType.Credit,
                AmountFcfa = amountToCredit, // Crédit = montant signé positif.
                BalanceAfter = wallet.AvailableBalance,
                RelatedEntity = WalletRelatedEntity.Payment,
                RelatedId = payment.Id,
                Note = $"Payment {payment.SenePayTransactionId}",
                OccurredAt = DateTime.UtcNow
            };
            _context.WalletTransactions.Add(txEntry);

            // Si une Invoice est rattachée (mode FixedAmount), on met à jour son
            // AmountPaidFcfa et son statut. On crédite l'invoice avec le montant
            // CIBLE (cohérent avec le wallet en mode Parent), sinon elle resterait
            // éternellement "presque payée" avec quelques FCFA fantômes.
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
