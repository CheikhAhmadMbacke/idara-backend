using System.Security.Cryptography;
using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    public class OtpService : IOtpService
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notif;
        private readonly OtpSettings _settings;

        public OtpService(
            AppDbContext context,
            IEmailService emailService,
            INotificationService notif,
            IOptions<OtpSettings> settings)
        {
            _context = context;
            _emailService = emailService;
            _notif = notif;
            _settings = settings.Value;
        }

        public async Task<string> GenerateAndSendOtpAsync(string email, OtpPurpose purpose, string language = "fr")
        {
            // Invalide tout OTP non-utilisé pour cet email + cette action.
            var oldOtps = _context.OtpRecords
                .Where(o => o.Email == email && o.Purpose == purpose && !o.IsUsed);
            _context.OtpRecords.RemoveRange(oldOtps);

            var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var minutes = _settings.ExpirationMinutes > 0 ? _settings.ExpirationMinutes : 10;
            var expiresAt = DateTime.UtcNow.AddMinutes(minutes);

            _context.OtpRecords.Add(new OtpRecord
            {
                Email = email,
                OtpCode = otpCode,
                ExpiresAt = expiresAt,
                IsUsed = false,
                Purpose = purpose
            });
            await _context.SaveChangesAsync();

            await _emailService.SendOtpEmailAsync(email, otpCode, language);
            return otpCode;
        }

        public async Task<string> GenerateAndSendSmsOtpAsync(
            string phoneE164, OtpPurpose purpose, int? userId, string preferredLanguage = "fr")
        {
            // L'identifiant de l'OtpRecord est le numéro (la colonne "Email" sert
            // de clé d'identité générique). VerifyOtpAsync compare sur cette clé.
            var old = _context.OtpRecords
                .Where(o => o.Email == phoneE164 && o.Purpose == purpose && !o.IsUsed);
            _context.OtpRecords.RemoveRange(old);

            var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var minutes = _settings.ExpirationMinutes > 0 ? _settings.ExpirationMinutes : 10;

            _context.OtpRecords.Add(new OtpRecord
            {
                Email = phoneE164,
                OtpCode = otpCode,
                ExpiresAt = DateTime.UtcNow.AddMinutes(minutes),
                IsUsed = false,
                Purpose = purpose
            });
            await _context.SaveChangesAsync();

            await _notif.SendSmsAsync(new NotificationSmsRequest(
                UserId: userId,
                RawPhone: phoneE164,
                PreferredLanguage: preferredLanguage,
                Message: NotificationTemplates.OtpCode(otpCode),
                // 🔴 MONO-LANGUE, quoi que dise le réglage SmsBilingual.
                // Mesuré : français seul 79 caractères en GSM-7 = 1 segment ·
                // arabe seul 54 caractères en UCS-2 = 1 segment AUSSI · les deux
                // ensemble 136 caractères en UCS-2 = 3 SEGMENTS. Un code bilingue
                // coûterait donc trois fois le prix sans rien apporter : son
                // destinataire n'a qu'une langue, et elle est connue.
                // ⚠️ Mono-langue ne veut PAS dire « en français » : le message
                // part dans la langue de l'utilisateur — l'arabe ne coûte pas un
                // centime de plus que le français.
                Bilingual: false,
                TemplateCode: "OTP",
                RelatedEntityId: null,
                TriggerSource: "api:auth/request-code"));

            return otpCode;
        }

        public async Task<bool> VerifyOtpAsync(string email, string otpCode, OtpPurpose purpose)
        {
            var otpRecord = await _context.OtpRecords
                .FirstOrDefaultAsync(o =>
                    o.Email == email
                    && o.OtpCode == otpCode
                    && o.Purpose == purpose
                    && !o.IsUsed
                    && o.ExpiresAt > DateTime.UtcNow);

            if (otpRecord == null) return false;

            otpRecord.IsUsed = true;
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
