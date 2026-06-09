using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services.Notifications
{
    /// <summary>
    /// Orchestration des notifications SMS : normalise le numéro, compose le
    /// texte (bilingue ou non), envoie via <see cref="ISmsService"/>, et trace
    /// dans <see cref="NotificationLog"/>. Best-effort : ne lève jamais (un échec
    /// SMS ne doit jamais casser une transaction métier — cf. §42/§57).
    ///
    /// Les écritures DB (log, dédup) passent par un scope DÉDIÉ
    /// (<see cref="IServiceScopeFactory"/>) pour ne pas interférer avec le
    /// change tracker du contexte appelant (webhook, cron…).
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISmsService _sms;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            IServiceScopeFactory scopeFactory,
            ISmsService sms,
            ILogger<NotificationService> logger)
        {
            _scopeFactory = scopeFactory;
            _sms = sms;
            _logger = logger;
        }

        public async Task SendSmsAsync(NotificationSmsRequest req, CancellationToken ct = default)
        {
            try
            {
                var phone = SenegalPhone.Normalize(req.RawPhone);
                if (phone == null)
                {
                    _logger.LogWarning(
                        "[notif] {Template} non envoyé : numéro invalide/absent (userId={UserId})",
                        req.TemplateCode, req.UserId);
                    await WriteLogAsync(req, recipient: req.RawPhone ?? string.Empty,
                        success: false, providerMessageId: null, error: "invalid_phone", cost: null, ct);
                    return;
                }

                var text = req.Message.Compose(req.Bilingual, req.PreferredLanguage);
                var result = await _sms.SendAsync(phone, text, ct);

                await WriteLogAsync(req, recipient: phone,
                    success: result.Success, providerMessageId: result.MessageId,
                    error: result.Error, cost: result.Cost, ct);
            }
            catch (Exception ex)
            {
                // Garde-fou ultime : aucune notification ne doit jamais remonter.
                _logger.LogError(ex, "[notif] Exception inattendue sur {Template} (userId={UserId})",
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
            NotificationSmsRequest req, string recipient, bool success,
            string? providerMessageId, string? error, string? cost, CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.NotificationLogs.Add(new NotificationLog
                {
                    UserId = req.UserId,
                    Channel = "Sms",
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
