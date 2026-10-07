using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.Models;
using Idara.API.Options;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    public interface ICashReceiptNotifier
    {
        /// <summary>
        /// Prévient le responsable qu'un encaissement manuel a été enregistré,
        /// avec le lien de son reçu. Jamais bloquant : l'encaissement est déjà
        /// écrit quand on arrive ici (§42/§57).
        /// </summary>
        /// <returns>Vrai si un SMS est parti.</returns>
        Task<bool> NotifyAsync(Payment payment, string triggerSource, CancellationToken ct);
    }

    /// <summary>
    /// Le SMS « paiement reçu » d'un encaissement au guichet. Extrait de
    /// <c>FeesController</c> le 2026-10-07 : l'écran de caisse et l'assistant IA
    /// enregistrent le même fait, ils doivent prévenir la famille de la même
    /// façon (§199).
    /// </summary>
    public class CashReceiptNotifier : ICashReceiptNotifier
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notif;
        private readonly WaveSettings _waveSettings;
        private readonly ILogger<CashReceiptNotifier> _logger;

        public CashReceiptNotifier(
            AppDbContext context,
            INotificationService notif,
            IOptions<WaveSettings> waveSettings,
            ILogger<CashReceiptNotifier> logger)
        {
            _context = context;
            _notif = notif;
            _waveSettings = waveSettings.Value;
            _logger = logger;
        }

        public async Task<bool> NotifyAsync(Payment payment, string triggerSource, CancellationToken ct)
        {
            if (payment.GuardianId is not int guardianId) return false;
            try
            {
                var guardian = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == guardianId && !u.IsDeleted, ct);
                if (guardian?.PhoneNumber == null) return false;

                var student = await _context.Students
                    .Where(s => s.Id == payment.StudentId)
                    .Select(s => new { s.FirstName, s.LastName })
                    .FirstOrDefaultAsync(ct);
                var eleve = student == null
                    ? "votre enfant"
                    : $"{student.FirstName} {student.LastName}".Trim();

                var platform = await _context.GetPlatformSettingsAsync(ct);
                return await _notif.SendSmsAsync(new NotificationSmsRequest(
                    UserId: guardian.Id,
                    RawPhone: guardian.PhoneNumber,
                    PreferredLanguage: guardian.PreferredLanguage ?? "fr",
                    // Le lien du reçu voyage avec la confirmation : une famille
                    // qui paie au guichet n'a souvent pas de compte, donc aucun
                    // autre chemin vers sa preuve de paiement (§229).
                    Message: NotificationTemplates.PaymentReceived(
                        eleve, payment.AmountFcfa,
                        PublicLinks.Receipt(_waveSettings.PublicBaseUrl, payment.Id, payment.PublicResultToken)),
                    Bilingual: platform.SmsBilingual,
                    TemplateCode: "PAYMENT_RECEIVED",
                    RelatedEntityId: payment.Id,
                    PushRoute: "/guardian/invoices",
                    SchoolId: payment.SchoolId,
                    TriggerSource: triggerSource), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[cash] SMS « paiement reçu » non envoyé pour {Id} — pas bloquant", payment.Id);
                return false;
            }
        }
    }
}
