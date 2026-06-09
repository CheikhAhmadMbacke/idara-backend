using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.Models;
using Idara.API.Services.Push;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services.Notifications
{
    /// <summary>
    /// Orchestration des notifications : pour chaque demande, on tente DEUX
    /// canaux best-effort — (1) SMS via <see cref="ISmsService"/> (no-op tant que
    /// la clé Africa's Talking n'est pas posée), (2) push via
    /// <see cref="IPushService"/> vers les appareils enregistrés de l'utilisateur
    /// (no-op tant que FCM n'est pas configuré). Composition du texte bilingue ou
    /// non, puis trace dans <see cref="NotificationLog"/> (un log par canal).
    /// Best-effort total : ne lève jamais (un échec notif ne doit jamais casser
    /// une transaction métier — cf. §42/§57).
    ///
    /// Le push n'atteint QUE les utilisateurs ayant l'app installée + permission
    /// accordée (un compte sans token = no-op silencieux) ; il complète le SMS,
    /// il ne le remplace pas pour le premier contact (onboarding = modal récap).
    ///
    /// Les écritures DB (log, dédup, lookup/purge des tokens) passent par un scope
    /// DÉDIÉ (<see cref="IServiceScopeFactory"/>) pour ne pas interférer avec le
    /// change tracker du contexte appelant (webhook, cron…).
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISmsService _sms;
        private readonly IPushService _push;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            IServiceScopeFactory scopeFactory,
            ISmsService sms,
            IPushService push,
            ILogger<NotificationService> logger)
        {
            _scopeFactory = scopeFactory;
            _sms = sms;
            _push = push;
            _logger = logger;
        }

        public async Task SendSmsAsync(NotificationSmsRequest req, CancellationToken ct = default)
        {
            // Push d'abord (indépendant du numéro) : best-effort, isolé.
            await DispatchPushAsync(req, ct);

            try
            {
                var phone = SenegalPhone.Normalize(req.RawPhone);
                if (phone == null)
                {
                    _logger.LogWarning(
                        "[notif] {Template} SMS non envoyé : numéro invalide/absent (userId={UserId})",
                        req.TemplateCode, req.UserId);
                    await WriteLogAsync(req, channel: "Sms", recipient: req.RawPhone ?? string.Empty,
                        success: false, providerMessageId: null, error: "invalid_phone", cost: null, ct);
                    return;
                }

                var text = req.Message.Compose(req.Bilingual, req.PreferredLanguage);
                var result = await _sms.SendAsync(phone, text, ct);

                await WriteLogAsync(req, channel: "Sms", recipient: phone,
                    success: result.Success, providerMessageId: result.MessageId,
                    error: result.Error, cost: result.Cost, ct);
            }
            catch (Exception ex)
            {
                // Garde-fou ultime : aucune notification ne doit jamais remonter.
                _logger.LogError(ex, "[notif] Exception inattendue sur SMS {Template} (userId={UserId})",
                    req.TemplateCode, req.UserId);
            }
        }

        /// <summary>
        /// Envoie le push à tous les appareils enregistrés de l'utilisateur (le
        /// push, gratuit, ne force pas l'UCS-2 : on envoie une seule langue, plus
        /// lisible). Purge les tokens que FCM déclare morts. Best-effort, isolé.
        /// </summary>
        private async Task DispatchPushAsync(NotificationSmsRequest req, CancellationToken ct)
        {
            if (req.UserId == null || !_push.IsConfigured)
                return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var tokens = await db.PushDeviceTokens
                    .Where(t => t.UserId == req.UserId)
                    .ToListAsync(ct);
                if (tokens.Count == 0)
                    return;

                // Push = une seule langue (préférence fiable de l'utilisateur).
                var body = req.Message.Compose(bilingual: false, req.PreferredLanguage);
                var data = new Dictionary<string, string> { ["templateCode"] = req.TemplateCode };
                if (req.RelatedEntityId != null)
                    data["relatedEntityId"] = req.RelatedEntityId.Value.ToString();

                var anySuccess = false;
                var stale = new List<PushDeviceToken>();

                foreach (var t in tokens)
                {
                    var r = await _push.SendAsync(t.Token, "Idara", body, data, link: "https://idara.sn", ct);
                    if (r.Success)
                    {
                        anySuccess = true;
                        t.LastSeenAt = DateTime.UtcNow;
                    }
                    else if (r.TokenInvalid)
                    {
                        stale.Add(t);
                    }
                }

                if (stale.Count > 0)
                    db.PushDeviceTokens.RemoveRange(stale);
                await db.SaveChangesAsync(ct);

                await WriteLogAsync(req, channel: "Push",
                    recipient: $"{tokens.Count} device(s)",
                    success: anySuccess, providerMessageId: null,
                    error: anySuccess ? null : "no_delivery", cost: null, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[notif] Exception inattendue sur push {Template} (userId={UserId})",
                    req.TemplateCode, req.UserId);
            }
        }

        public async Task<bool> HasAttemptedAsync(
            string templateCode, int relatedEntityId, CancellationToken ct = default)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                return await db.NotificationLogs.AnyAsync(
                    n => n.TemplateCode == templateCode
                         && n.RelatedEntityId == relatedEntityId, ct);
            }
            catch (Exception ex)
            {
                // En cas d'erreur de lecture, on renvoie false (au pire un rappel
                // de plus, jamais un blocage).
                _logger.LogError(ex, "[notif] Échec lecture dédup {Template}/{Id}",
                    templateCode, relatedEntityId);
                return false;
            }
        }

        private async Task WriteLogAsync(
            NotificationSmsRequest req, string channel, string recipient, bool success,
            string? providerMessageId, string? error, string? cost, CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.NotificationLogs.Add(new NotificationLog
                {
                    UserId = req.UserId,
                    Channel = channel,
                    Recipient = recipient,
                    TemplateCode = req.TemplateCode,
                    RelatedEntityId = req.RelatedEntityId,
                    Success = success,
                    ProviderMessageId = providerMessageId,
                    Error = error,
                    Cost = cost,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[notif] Échec écriture NotificationLog {Template}", req.TemplateCode);
            }
        }
    }
}
