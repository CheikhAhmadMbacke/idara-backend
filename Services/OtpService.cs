using System.Security.Cryptography;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    public class OtpService : IOtpService
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;
        private readonly OtpSettings _settings;

        public OtpService(AppDbContext context, IEmailService emailService, IOptions<OtpSettings> settings)
        {
            _context = context;
            _emailService = emailService;
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
