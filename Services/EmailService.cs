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
            // Le nom de l'école et le motif de rejet sont SAISIS (par l'école,
            // par le SuperAdmin) et atterrissent dans du HTML : sans échappement,
            // un nom contenant des balises injecte du contenu arbitraire dans un
            // e-mail qui porte l'identité d'Idara. L'e-mail de facture d'abonnement
            // encodait déjà — l'oubli ici était une incohérence, pas un choix.
            schoolName = System.Net.WebUtility.HtmlEncode(schoolName);
            rejectionReason = rejectionReason is null
                ? null
                : System.Net.WebUtility.HtmlEncode(rejectionReason);
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
            // Mêmes valeurs saisies, même risque : on encode avant d'interpoler.
            schoolName = System.Net.WebUtility.HtmlEncode(schoolName);
            fullName = System.Net.WebUtility.HtmlEncode(fullName);
            function = System.Net.WebUtility.HtmlEncode(function);
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

        public Task SendSubscriptionInvoiceEmailAsync(
            string toEmail, string schoolName, long amountFcfa,
            DateTime periodStart, DateTime periodEnd, string language = "fr")
        {
            var subject = $"Idara — Facture d'abonnement {amountFcfa:N0} FCFA";
            var body =
                "<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:520px;margin:auto;color:#0F172A\">" +
                "<h2 style=\"color:#0B744D\">Idara — Abonnement réglé</h2>" +
                $"<p>Bonjour,</p>" +
                $"<p>L'abonnement de votre école <b>{System.Net.WebUtility.HtmlEncode(schoolName)}</b> a été prélevé avec succès depuis votre wallet.</p>" +
                "<table style=\"border-collapse:collapse;width:100%;margin:16px 0\">" +
                $"<tr><td style=\"padding:8px;border-bottom:1px solid #E2E8F0;color:#475569\">Montant</td><td style=\"padding:8px;border-bottom:1px solid #E2E8F0;text-align:right;font-weight:bold\">{amountFcfa:N0} FCFA</td></tr>" +
                $"<tr><td style=\"padding:8px;border-bottom:1px solid #E2E8F0;color:#475569\">Période</td><td style=\"padding:8px;border-bottom:1px solid #E2E8F0;text-align:right\">{periodStart:dd/MM/yyyy} → {periodEnd:dd/MM/yyyy}</td></tr>" +
                "</table>" +
                "<p style=\"color:#475569;font-size:13px\">Votre abonnement est actif. La facture détaillée est disponible dans l'application.</p>" +
                "<p style=\"color:#94A3B8;font-size:12px\">— L'équipe Idara</p>" +
                "</div>";
            return SendAsync(toEmail, subject, body, isHtml: true);
        }

        public Task SendIncidentAlertEmailAsync(
            string toEmail, DTOs.Observability.IncidentAlertEmail a)
        {
            var (subject, body) = BuildIncidentAlert(a);
            return SendAsync(toEmail, subject, body, isHtml: true);
        }

        /// <summary>
        /// Compose l'alerte, séparément de son envoi.
        ///
        /// <para><b>Public et statique exprès</b> : un e-mail dont le contenu n'est
        /// vérifiable qu'en l'envoyant réellement ne se vérifie jamais. Ici on
        /// peut contrôler la mise en page et l'échappement sur des données
        /// hostiles sans toucher au serveur SMTP — même raisonnement que pour les
        /// PDF, rendus en image avant d'être crus (§116).</para>
        /// </summary>
        public static (string Subject, string Body) BuildIncidentAlert(
            DTOs.Observability.IncidentAlertEmail a)
        {
            // Le sujet doit se lire ENTIER dans la liste des e-mails, sur un
            // téléphone : c'est là qu'on décide si on ouvre tout de suite. D'où
            // l'ordre « qui · où · quoi », le plus discriminant d'abord.
            var scope = a.SimilarLast24h > 1 ? $" ×{a.SimilarLast24h}" : string.Empty;
            // Repli sur la personne quand il n'y a pas d'école : un compte
            // SuperAdmin ou donateur n'est rattaché à aucun daara, et le sujet
            // donnait alors « [Idara] — — /students/new », qui n'apprend rien.
            var who = a.SchoolName is { Length: > 0 } and not "—"
                ? a.SchoolName
                : (string.IsNullOrWhiteSpace(a.PersonName) ? "compte inconnu" : a.PersonName);
            var subject = $"[Idara{scope}] {Trim(who, 28)} — {Trim(a.Route, 24)} — {a.Code}";

            var rows = new System.Text.StringBuilder();
            void Row(string label, string? value, bool strong = false)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var style = strong ? "font-weight:bold;font-size:15px" : "";
                rows.Append(
                    "<tr>" +
                    $"<td style=\"padding:7px 10px;border-bottom:1px solid #E2E8F0;color:#64748B;white-space:nowrap\">{Esc(label)}</td>" +
                    $"<td style=\"padding:7px 10px;border-bottom:1px solid #E2E8F0;{style}\">{Esc(value!)}</td>" +
                    "</tr>");
            }

            // Le téléphone d'abord, en gras : c'est l'action à mener, pas un
            // détail de contexte. L'utilisateur, lui, n'a rien à faire.
            Row("Téléphone", a.PhoneNumber, strong: true);
            Row("Personne", $"{a.PersonName}{(string.IsNullOrWhiteSpace(a.RoleLabel) ? "" : $" · {a.RoleLabel}")}");
            Row("Daara", a.SchoolName);
            Row("Écran", a.Route);
            Row("Quand", a.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy à HH:mm"));
            // Pas de ligne « Type » : l'en-tête de l'e-mail l'affiche déjà, et
            // une information répétée deux fois se lit moins bien qu'une fois.
            Row("Application", $"{a.Platform} · v{a.AppVersion}");
            Row("Appareil", a.Device);
            Row("Langue", a.LocaleCode == "ar" ? "arabe" : "français");
            Row("Code", a.Code);
            Row("Trace serveur", a.RequestTrace);

            var body = new System.Text.StringBuilder();
            body.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:640px;margin:auto\">");
            // Le titre s'accorde au nombre de personnes touchées : annoncer
            // « un utilisateur » au-dessus d'un bandeau disant « 14 personnes »
            // ferait douter du reste de l'e-mail.
            var heading = a.SimilarLast24h > 1
                ? $"{a.SimilarLast24h} utilisateurs ont rencontré un problème"
                : "Un utilisateur a rencontré un problème";
            // Pas d'échappement sur `heading` : il est composé de nos propres
            // littéraux et d'un entier. Échapper un libellé interne ne protège de
            // rien et transforme les accents en entités numériques, ce qui rend le
            // HTML pénible à relire quand on débogue un e-mail.
            body.Append("<div style=\"background:#0B744D;color:#fff;padding:14px 16px;border-radius:8px 8px 0 0\">"
                + $"<div style=\"font-size:17px;font-weight:bold\">{heading}</div>"
                + $"<div style=\"font-size:13px;opacity:.85;margin-top:2px\">{Esc(a.KindLabel)}</div></div>");
            body.Append("<div style=\"border:1px solid #E2E8F0;border-top:none;border-radius:0 0 8px 8px;padding:16px\">");

            if (a.SimilarLast24h > 1)
            {
                body.Append("<div style=\"background:#FEF3C7;border-left:4px solid #F59E0B;padding:10px 12px;"
                    + "margin-bottom:14px;font-size:14px\"><b>"
                    + $"{a.SimilarLast24h} personnes</b> ont rencontré le même problème depuis 24 h.</div>");
            }

            if (!string.IsNullOrWhiteSpace(a.UserComment))
            {
                // Mis en avant : c'est la seule partie écrite par un humain, et
                // souvent la plus parlante.
                body.Append("<div style=\"background:#F0F9FF;border-left:4px solid #0284C7;padding:10px 12px;"
                    + "margin-bottom:14px;font-size:14px\"><div style=\"color:#64748B;font-size:12px\">"
                    + "Ce que dit l'utilisateur</div>"
                    + $"<div style=\"margin-top:4px\">{Esc(a.UserComment!)}</div></div>");
            }

            if (!string.IsNullOrWhiteSpace(a.Message))
            {
                body.Append($"<p style=\"font-size:14px;margin:0 0 14px\"><b>{Esc(a.Message)}</b>"
                    + (string.IsNullOrWhiteSpace(a.ExceptionType)
                        ? string.Empty
                        : $"<br><span style=\"color:#94A3B8;font-size:12px\">{Esc(a.ExceptionType)}</span>")
                    + "</p>");
            }

            body.Append($"<table style=\"border-collapse:collapse;width:100%;font-size:13px\">{rows}</table>");

            if (!string.IsNullOrWhiteSpace(a.StackTrace))
            {
                // Tronquée à 2 500 caractères : les premières lignes situent le
                // défaut, le reste est dans la page SuperAdmin. Un e-mail
                // illisible sur téléphone ne sert personne.
                body.Append("<div style=\"margin-top:14px\"><div style=\"color:#64748B;font-size:12px;"
                    + "margin-bottom:4px\">Pile d'appels</div>"
                    + "<pre style=\"background:#F8FAFC;border:1px solid #E2E8F0;border-radius:6px;padding:10px;"
                    + "font-size:11px;overflow-x:auto;white-space:pre-wrap;margin:0\">"
                    + Esc(Trim(a.StackTrace, 2500)) + "</pre></div>");
            }

            body.Append("<p style=\"color:#94A3B8;font-size:12px;margin-top:16px\">"
                + $"Retrouvez le détail complet dans Idara → SuperAdmin → Incidents et journaux, "
                + $"en recherchant <b>{Esc(a.Code)}</b>.<br>"
                + "L'utilisateur n'a rien eu à faire pour envoyer ce rapport.</p>");
            body.Append("</div></div>");

            return (subject, body.ToString());
        }

        /// <summary>
        /// Échappement HTML. Indispensable : le message d'erreur et le
        /// commentaire de l'utilisateur sont du texte non contrôlé, qui casserait
        /// la mise en page (ou pire) s'il contenait des chevrons.
        /// </summary>
        private static string Esc(string value) =>
            System.Net.WebUtility.HtmlEncode(value);

        private static string Trim(string value, int max) =>
            string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";

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
