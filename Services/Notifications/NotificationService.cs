using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.Models;
using Idara.API.Services.Push;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services.Notifications
{
    /// <summary>
    /// Orchestration des notifications. Deux familles d'envoi :
    ///   - <see cref="SendSmsAsync"/> : SMS + push en fan-out (parents, invitations…).
    ///   - <see cref="SendPushOnlyAsync"/> / <see cref="SendBroadcastAsync"/> :
    ///     push UNIQUEMENT (notifs école, suivi enfant, broadcast SuperAdmin) —
    ///     le SMS n'y est pas voulu (coût, et c'est de l'info temps-réel gratuite).
    ///
    /// Best-effort total : ne lève JAMAIS (un échec notif ne casse jamais une
    /// transaction métier — §42/§57). Le push n'atteint que les comptes ayant
    /// l'app + permission (sans token = no-op). Toutes les écritures DB passent
    /// par un scope DÉDIÉ (<see cref="IServiceScopeFactory"/>) pour ne pas
    /// interférer avec le change tracker de l'appelant (webhook, cron…).
    /// </summary>
    public class NotificationService : INotificationService
    {
        private const string BaseUrl = "https://idara.sn";

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

        // ===================== SMS + push (fan-out) =====================

        public async Task<bool> SendSmsAsync(NotificationSmsRequest req, CancellationToken ct = default)
        {
            // Push d'abord (indépendant du numéro), best-effort isolé.
            if (req.UserId != null)
            {
                await DispatchPushAsync(
                    req.UserId.Value, req.PreferredLanguage, req.Message,
                    req.TemplateCode, req.RelatedEntityId, req.PushRoute, url: null, ct);
            }

            try
            {
                var phone = SenegalPhone.Normalize(req.RawPhone);
                if (phone == null)
                {
                    _logger.LogWarning(
                        "[notif] {Template} SMS non envoyé : numéro invalide/absent (userId={UserId})",
                        req.TemplateCode, req.UserId);
                    await WriteLogAsync(req.UserId, "Sms", req.RawPhone ?? string.Empty, req.TemplateCode,
                        req.RelatedEntityId, success: false, providerMessageId: null,
                        error: "invalid_phone", cost: null, ct);
                    return false;
                }

                var text = req.Message.Compose(req.Bilingual, req.PreferredLanguage);
                var result = await _sms.SendAsync(phone, text, ct);

                await WriteLogAsync(req.UserId, "Sms", phone, req.TemplateCode, req.RelatedEntityId,
                    success: result.Success, providerMessageId: result.MessageId,
                    error: result.Error, cost: result.Cost, ct);
                return result.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[notif] Exception inattendue sur SMS {Template} (userId={UserId})",
                    req.TemplateCode, req.UserId);
                return false;
            }
        }

        // ===================== Push uniquement =====================

        public async Task SendPushOnlyAsync(PushOnlyRequest req, CancellationToken ct = default)
        {
            await DispatchPushAsync(
                req.UserId, req.PreferredLanguage, req.Message,
                req.TemplateCode, req.RelatedEntityId, req.PushRoute, url: null, ct);
        }

        /// <summary>Cœur d'envoi push à un utilisateur (compose une seule langue,
        /// envoie à tous ses appareils, purge les tokens morts, trace). Isolé.</summary>
        private async Task DispatchPushAsync(
            int userId, string lang, BilingualMessage message,
            string templateCode, int? relatedEntityId, string? route, string? url,
            CancellationToken ct)
        {
            if (!_push.IsConfigured) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var tokens = await db.PushDeviceTokens.Where(t => t.UserId == userId).ToListAsync(ct);
                if (tokens.Count == 0) return;

                // Push = une seule langue (gratuit, pas d'UCS-2 à optimiser).
                var body = message.Compose(bilingual: false, preferredLanguage: lang);
                var data = BuildData(templateCode, relatedEntityId, route, url);
                var ok = await PushToTokensAsync(db, tokens, "Idara", body, data, LinkFor(route, url), ct);

                await db.SaveChangesAsync(ct);
                await WriteLogAsync(userId, "Push", $"{tokens.Count} device(s)", templateCode,
                    relatedEntityId, success: ok > 0, providerMessageId: null,
                    error: ok > 0 ? null : "no_delivery", cost: null, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[notif] Exception inattendue sur push {Template} (userId={UserId})",
                    templateCode, userId);
            }
        }

        // ===================== Broadcast =====================

        public async Task<int> SendBroadcastAsync(
            IReadOnlyCollection<int> userIds, BroadcastContent content, CancellationToken ct = default)
        {
            if (!_push.IsConfigured || userIds.Count == 0) return 0;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var tokens = await db.PushDeviceTokens
                    .Where(t => userIds.Contains(t.UserId))
                    .ToListAsync(ct);
                if (tokens.Count == 0) return 0;

                var title = string.IsNullOrWhiteSpace(content.Title) ? "Idara" : content.Title;
                var data = BuildData(content.TemplateCode, relatedEntityId: null, route: null, url: content.Url);
                var ok = await PushToTokensAsync(db, tokens, title, content.Body, data, LinkFor(null, content.Url), ct);

                await db.SaveChangesAsync(ct);
                await WriteLogAsync(userId: null, "Push", $"broadcast {tokens.Count} device(s)",
                    content.TemplateCode, relatedEntityId: null, success: ok > 0,
                    providerMessageId: null, error: null, cost: null, ct);

                _logger.LogInformation("[notif] broadcast {Template} : {Ok}/{Total} appareil(s)",
                    content.TemplateCode, ok, tokens.Count);
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[notif] Exception inattendue sur broadcast {Template}", content.TemplateCode);
                return 0;
            }
        }

        // ===================== Helpers =====================

        /// <summary>Envoie à chaque token, met à jour LastSeenAt sur succès, marque
        /// les tokens morts pour purge (RemoveRange). Renvoie le nb de succès.
        /// Le SaveChanges est laissé à l'appelant (un seul commit).</summary>
        private async Task<int> PushToTokensAsync(
            AppDbContext db, List<PushDeviceToken> tokens, string title, string body,
            IReadOnlyDictionary<string, string> data, string link, CancellationToken ct)
        {
            var ok = 0;
            var stale = new List<PushDeviceToken>();
            foreach (var t in tokens)
            {
                var r = await _push.SendAsync(t.Token, title, body, data, link, ct);
                if (r.Success) { ok++; t.LastSeenAt = DateTime.UtcNow; }
                else if (r.TokenInvalid) stale.Add(t);
            }
            if (stale.Count > 0) db.PushDeviceTokens.RemoveRange(stale);
            return ok;
        }

        private static Dictionary<string, string> BuildData(
            string templateCode, int? relatedEntityId, string? route, string? url)
        {
            var data = new Dictionary<string, string> { ["templateCode"] = templateCode };
            if (relatedEntityId != null) data["relatedEntityId"] = relatedEntityId.Value.ToString();
            if (!string.IsNullOrWhiteSpace(route)) data["route"] = route!;
            if (!string.IsNullOrWhiteSpace(url)) data["url"] = url!;
            return data;
        }

        private static string LinkFor(string? route, string? url) =>
            !string.IsNullOrWhiteSpace(url) ? url!
            : !string.IsNullOrWhiteSpace(route) ? BaseUrl + route
            : BaseUrl;

        public async Task<bool> HasAttemptedAsync(
            string templateCode, int relatedEntityId, CancellationToken ct = default)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                return await db.NotificationLogs.AnyAsync(
                    n => n.TemplateCode == templateCode && n.RelatedEntityId == relatedEntityId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[notif] Échec lecture dédup {Template}/{Id}", templateCode, relatedEntityId);
                return false;
            }
        }

        public async Task NotifyGuardiansOfStudentAsync(
            int studentId, BilingualMessage message, string templateCode,
            string? pushRoute, bool oncePerDay, CancellationToken ct = default)
        {
            if (!_push.IsConfigured) return;
            try
            {
                // Plafond 1/élève/jour : si déjà tenté aujourd'hui pour ce template
                // et cet élève, on ne renotifie pas (plusieurs maîtres, re-saisies).
                if (oncePerDay
                    && await HasAttemptedSinceAsync(templateCode, studentId, DateTime.UtcNow.Date, ct))
                    return;

                List<GuardianRef> guardians;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    // Point de coupure CENTRAL des notifs élève : un élève
                    // supprimé (oubli préexistant corrigé le 2026-08-17) ou SORTI
                    // ne déclenche plus rien vers ses parents — absence, journal,
                    // cycle Coran, bulletin, d'un coup. Périmètre Enrolled écrit
                    // en ligne (l'extension porte sur IQueryable<Student>).
                    var today = DateTime.UtcNow.Date;
                    guardians = await db.StudentGuardians
                        .Where(sg => sg.StudentId == studentId
                                     && !sg.Guardian.IsDeleted
                                     && !sg.Student.IsDeleted
                                     && (sg.Student.ExitDate == null || sg.Student.ExitDate > today))
                        .Select(sg => new GuardianRef(sg.GuardianId, sg.Guardian.PreferredLanguage))
                        .ToListAsync(ct);
                }

                foreach (var g in guardians)
                {
                    await DispatchPushAsync(
                        g.Id, g.Lang ?? "fr", message, templateCode,
                        relatedEntityId: studentId, route: pushRoute, url: null, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[notif] Exception inattendue NotifyGuardiansOfStudent {Template} student={Student}",
                    templateCode, studentId);
            }
        }

        private sealed record GuardianRef(int Id, string? Lang);

        public async Task<bool> HasAttemptedSinceAsync(
            string templateCode, int relatedEntityId, DateTime sinceUtc, CancellationToken ct = default)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                return await db.NotificationLogs.AnyAsync(
                    n => n.TemplateCode == templateCode
                         && n.RelatedEntityId == relatedEntityId
                         && n.CreatedAt >= sinceUtc, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[notif] Échec lecture dédup-since {Template}/{Id}", templateCode, relatedEntityId);
                return false;
            }
        }

        private async Task WriteLogAsync(
            int? userId, string channel, string recipient, string templateCode, int? relatedEntityId,
            bool success, string? providerMessageId, string? error, string? cost, CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.NotificationLogs.Add(new NotificationLog
                {
                    UserId = userId,
                    Channel = channel,
                    Recipient = recipient,
                    TemplateCode = templateCode,
                    RelatedEntityId = relatedEntityId,
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
                _logger.LogError(ex, "[notif] Échec écriture NotificationLog {Template}", templateCode);
            }
        }
    }
}
