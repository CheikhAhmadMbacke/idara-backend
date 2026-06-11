namespace Idara.API.Services
{
    public interface IEmailService
    {
        Task SendOtpEmailAsync(string toEmail, string otpCode, string language = "fr");
        Task SendInvitationEmailAsync(string toEmail, string fullName, string schoolName, string function, string temporaryPassword, string language = "fr");
        Task SendSchoolValidationEmailAsync(string toEmail, string schoolName, bool isValidated, string? rejectionReason = null, string language = "fr");
        Task SendSubscriptionInvoiceEmailAsync(string toEmail, string schoolName, long amountFcfa, DateTime periodStart, DateTime periodEnd, string language = "fr");
    }
}
