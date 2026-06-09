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

        /// <summary>
        /// Génère un OTP et l'envoie par SMS au numéro (E.164). Utilisé pour
        /// l'activation / la réinitialisation de mot de passe des comptes
        /// téléphone (parents, enseignants). L'identifiant de l'OtpRecord est le
        /// numéro lui-même.
        /// </summary>
        Task<string> GenerateAndSendSmsOtpAsync(
            string phoneE164, OtpPurpose purpose, int? userId, string preferredLanguage = "fr");

        Task<bool> VerifyOtpAsync(string identifier, string otpCode, OtpPurpose purpose);
    }
}
