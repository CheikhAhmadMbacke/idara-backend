using Idara.API.Enums;

namespace Idara.API.Services
{
    public interface IOtpService
    {
        /// <summary>
        /// Génère un OTP et l'envoie par email dans la langue spécifiée
        /// (défaut "fr").
        /// </summary>
        Task<string> GenerateAndSendOtpAsync(string email, OtpPurpose purpose, string language = "fr");
        Task<bool> VerifyOtpAsync(string email, string otpCode, OtpPurpose purpose);
    }
}
