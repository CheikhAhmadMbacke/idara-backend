using System.Net;
using System.Net.Mail;
using Idara.API.Options;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    /// <summary>
    /// Envoi d'emails transactionnels (OTP, invitation, validation école).
    /// Templates bilingues FR/AR — la langue est passée en paramètre par le
    /// service appelant (qui la lit depuis User.PreferredLanguage).
    ///
    /// Pour ajouter une nouvelle langue : ajouter une entrée à <see cref="_supported"/>
    /// puis fournir les chaînes dans chaque méthode template _build*.
    /// Si la langue demandée n'est pas supportée, on fallback vers "fr".
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;
        private readonly ILogger<EmailService> _logger;

        private static readonly HashSet<string> _supported =
            new(StringComparer.OrdinalIgnoreCase) { "fr", "ar" };

        public EmailService(IOptions<EmailSettings> settings, ILogger<EmailService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        // ----- Public API -----

        public Task SendOtpEmailAsync(string toEmail, string otpCode, string language = "fr")
        {
            var (subject, body, isHtml) = _buildOtp(NormalizeLang(language), otpCode);
            return SendAsync(toEmail, subject, body, isHtml);
        }

        public Task SendSchoolValidationEmailAsync(
            string toEmail,
            string schoolName,
            bool isValidated,
            string? rejectionReason = null,
            string language = "fr")
        {
            var (subject, body, isHtml) =
                _buildSchoolValidation(NormalizeLang(language), schoolName, isValidated, rejectionReason);
            return SendAsync(toEmail, subject, body, isHtml);
        }

        public Task SendInvitationEmailAsync(
            string toEmail,
            string fullName,
            string schoolName,
            string function,
            string temporaryPassword,
            string language = "fr")
        {
            var (subject, body, isHtml) =
                _buildInvitation(NormalizeLang(language), toEmail, fullName, schoolName, function, temporaryPassword);
            return SendAsync(toEmail, subject, body, isHtml);
        }

        // ----- Templates -----

        private static (string subject, string body, bool isHtml) _buildOtp(string lang, string otpCode)
        {
            if (lang == "ar")
            {
                return (
                    "رمز التحقق - إدارة",
                    $@"<div dir='rtl' style='font-family:Tahoma,Arial,sans-serif;font-size:14px;color:#0F172A'>
<p>السلام عليكم،</p>
<p>رمز التحقق الخاص بك هو :</p>
<p style='font-size:28px;font-weight:bold;letter-spacing:6px;color:#16A34A'>{otpCode}</p>
<p>هذا الرمز صالح لمدة 10 دقائق فقط.</p>
<p>إذا لم تطلب هذا الرمز، يرجى تجاهل هذه الرسالة.</p>
<hr style='border:none;border-top:1px solid #E2E8F0;margin:20px 0' />
<p style='color:#475569;font-size:12px'>فريق إدارة</p>
</div>",
                    true);
            }
            return (
                "Code de vérification - Idara",
                $@"<div style='font-family:Arial,sans-serif;font-size:14px;color:#0F172A'>
<p>Bonjour,</p>
<p>Votre code de vérification est :</p>
<p style='font-size:28px;font-weight:bold;letter-spacing:6px;color:#16A34A'>{otpCode}</p>
<p>Ce code est valable 10 minutes.</p>
<p>Si vous n'êtes pas à l'origine de cette demande, ignorez ce message.</p>
<hr style='border:none;border-top:1px solid #E2E8F0;margin:20px 0' />
<p style='color:#475569;font-size:12px'>L'équipe Idara</p>
</div>",
                true);
        }

        private static (string subject, string body, bool isHtml) _buildSchoolValidation(
            string lang, string schoolName, bool isValidated, string? rejectionReason)
        {
            if (lang == "ar")
            {
                if (isValidated)
                {
                    return (
                        "تمت المصادقة على مدرستك على إدارة",
                        $@"<div dir='rtl' style='font-family:Tahoma,Arial,sans-serif;font-size:14px;color:#0F172A'>
<p>السلام عليكم،</p>
<p>نحن سعداء بإعلامكم أن مدرستكم <b>'{schoolName}'</b> تمت المصادقة عليها بنجاح.</p>
<p>يمكنكم الآن تسجيل الدخول وإدارة فضائكم على المنصة.</p>
<p style='color:#475569;font-size:12px;margin-top:20px'>فريق إدارة</p>
</div>",
                        true);
                }
                return (
                    "تم رفض ملفكم على إدارة",
                    $@"<div dir='rtl' style='font-family:Tahoma,Arial,sans-serif;font-size:14px;color:#0F172A'>
<p>السلام عليكم،</p>
<p>نأسف لإعلامكم أن ملف مدرستكم <b>'{schoolName}'</b> قد تم رفضه.</p>
<p><b>السبب :</b> {rejectionReason}</p>
<p>يرجى تصحيح المعلومات وإعادة التقديم.</p>
<p style='color:#475569;font-size:12px;margin-top:20px'>فريق إدارة</p>
</div>",
                    true);
            }

            if (isValidated)
            {
                return (
                    "Validation de votre école sur Idara",
                    $@"<div style='font-family:Arial,sans-serif;font-size:14px;color:#0F172A'>
<p>Bonjour,</p>
<p>Nous avons le plaisir de vous informer que votre école <b>'{schoolName}'</b> a été validée.</p>
<p>Vous pouvez désormais vous connecter et gérer votre espace sur la plateforme.</p>
<p style='color:#475569;font-size:12px;margin-top:20px'>L'équipe Idara</p>
</div>",
                    true);
            }
            return (
                "Rejet de votre dossier Idara",
                $@"<div style='font-family:Arial,sans-serif;font-size:14px;color:#0F172A'>
<p>Bonjour,</p>
<p>Votre dossier pour l'école <b>'{schoolName}'</b> a été rejeté.</p>
<p><b>Motif :</b> {rejectionReason}</p>
<p>Veuillez corriger les informations et soumettre à nouveau votre dossier.</p>
<p style='color:#475569;font-size:12px;margin-top:20px'>L'équipe Idara</p>
</div>",
                true);
        }

        private static (string subject, string body, bool isHtml) _buildInvitation(
            string lang, string toEmail, string fullName, string schoolName, string function, string temporaryPassword)
        {
            if (lang == "ar")
            {
                return (
                    "دعوة للانضمام إلى إدارة",
                    $@"<div dir='rtl' style='font-family:Tahoma,Arial,sans-serif;font-size:14px;color:#0F172A'>
<p>السلام عليكم {fullName}،</p>
<p>تمت إضافتكم بصفة <b>{function}</b> على منصة <b>إدارة</b> من قبل مدرسة <b>{schoolName}</b>.</p>
<p>بيانات الدخول المؤقتة :</p>
<ul style='line-height:1.8'>
<li>البريد الإلكتروني : <code>{toEmail}</code></li>
<li>كلمة المرور : <code>{temporaryPassword}</code></li>
</ul>
<p style='color:#B91C1C'><b>هام :</b> يرجى تغيير كلمة المرور بعد أول تسجيل دخول.</p>
<p style='color:#475569;font-size:12px;margin-top:20px'>فريق إدارة</p>
</div>",
                    true);
            }
            return (
                "Invitation à rejoindre Idara",
                $@"<div style='font-family:Arial,sans-serif;font-size:14px;color:#0F172A'>
<p>Bonjour {fullName},</p>
<p>Vous avez été ajouté en tant que <b>{function}</b> sur la plateforme <b>Idara</b> par l'école <b>{schoolName}</b>.</p>
<p>Vos identifiants temporaires :</p>
<ul style='line-height:1.8'>
<li>Email : <code>{toEmail}</code></li>
<li>Mot de passe : <code>{temporaryPassword}</code></li>
</ul>
<p style='color:#B91C1C'><b>Important :</b> changez votre mot de passe après votre première connexion.</p>
<p style='color:#475569;font-size:12px;margin-top:20px'>L'équipe Idara</p>
</div>",
                true);
        }

        // ----- Helpers -----

        private static string NormalizeLang(string? language)
        {
            if (string.IsNullOrWhiteSpace(language)) return "fr";
            var lower = language.Trim().ToLowerInvariant();
            return _supported.Contains(lower) ? lower : "fr";
        }

        private async Task SendAsync(string toEmail, string subject, string body, bool isHtml)
        {
            try
            {
                using var client = new SmtpClient(_settings.SmtpServer, _settings.SmtpPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(_settings.SenderEmail, _settings.SenderPassword)
                };

                var from = new MailAddress(_settings.SenderEmail, _settings.SenderName);
                var to = new MailAddress(toEmail);
                using var message = new MailMessage(from, to)
                {
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = isHtml,
                    SubjectEncoding = System.Text.Encoding.UTF8,
                    BodyEncoding = System.Text.Encoding.UTF8,
                };

                await client.SendMailAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec d'envoi d'email à {ToEmail} (sujet: {Subject})", toEmail, subject);
                throw;
            }
        }
    }
}
