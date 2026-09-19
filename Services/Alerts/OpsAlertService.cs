using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.DTOs.Alerts;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Services.Alerts
{
    /// <inheritdoc cref="IOpsAlertService"/>
    public class OpsAlertService : IOpsAlertService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OpsAlertSettings _settings;
        private readonly IHostEnvironment _env;
        private readonly ILogger<OpsAlertService> _logger;

        public OpsAlertService(
            IServiceScopeFactory scopeFactory,
            IOptions<OpsAlertSettings> settings,
            IHostEnvironment env,
            ILogger<OpsAlertService> logger)
        {
            _scopeFactory = scopeFactory;
            _settings = settings.Value;
            _env = env;
            _logger = logger;
        }

        /// <summary>
        /// Préfixe apposé au sujet quand l'alerte NE vient PAS de la production.
        ///
        /// <para>🔴 Motif vécu, le 2026-09-06 : un banc d'essai local a mis le
        /// palier de dépense SMS à zéro pour prouver qu'il coupait bien les
        /// envois. L'alerte est partie — vers la vraie adresse d'alerte, avec le
        /// même sujet qu'en production : « Envoi de SMS TOTALEMENT suspendu ».
        /// Cheikh l'a reçue et a cru la plateforme à l'arrêt. Les chiffres du
        /// corps (0 FCFA dépensés, écoles de test) le démentaient, mais il faut
        /// les lire — un sujet alarmant se croit avant de se vérifier.</para>
        ///
        /// <para>On ne SUPPRIME pas l'alerte hors production : un banc d'essai
        /// qui n'alerte pas ne prouve rien. On la rend impossible à confondre.</para>
        /// </summary>
        private string SubjectPrefix =>
            _env.IsProduction() ? "[Idara] " : $"[Idara — {_env.EnvironmentName} — PAS la production] ";

        public void Queue(OpsAlertRequest request)
        {
            if (!_settings.Enabled) return;

            // Détaché de la requête en cours, et `CancellationToken.None` : une
            // alerte ne doit pas mourir parce que le téléphone de l'école a coupé
            // la connexion juste après son retrait raté — c'est même le cas le
            // plus probable.
            _ = Task.Run(async () =>
            {
                try { await SendAsync(request, CancellationToken.None); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[ops-alert] Alerte {Kind} impossible ({Key}).", request.Kind, request.GroupingKey);
                }
            });
        }

        public async Task SendAsync(OpsAlertRequest request, CancellationToken ct = default)
        {
            if (!_settings.Enabled) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
            var superAdmin = scope.ServiceProvider
                .GetRequiredService<IOptions<SuperAdminSettings>>().Value;

            var now = DateTime.UtcNow;
            var body = string.Join("\n", request.Facts
                .Where(f => !string.IsNullOrWhiteSpace(f.Value))
                .Select(f => $"{f.Label} : {f.Value}"));

            // La ligne est écrite AVANT toute décision d'envoi : une alerte
            // regroupée, plafonnée ou dont l'e-mail échoue doit rester
            // retrouvable. C'est exactement celle-là qu'on cherchera après coup.
            var alert = new OpsAlert
            {
                Kind = request.Kind,
                GroupingKey = Truncate(request.GroupingKey, 160),
                Subject = Truncate(request.Subject, 300),
                Body = Truncate(body, 4000),
                Advice = TruncateOrNull(request.Advice, 600),
                SchoolId = request.SchoolId,
                RelatedId = request.RelatedId,
                CreatedAt = now,
            };
            db.OpsAlerts.Add(alert);
            await db.SaveChangesAsync(ct);

            // Journal serveur systématique, même sans e-mail : c'est le filet
            // quand SMTP est indisponible, et c'est greppable.
            var level = IsUrgent(request.Kind) ? LogLevel.Critical : LogLevel.Warning;
            _logger.Log(level, "[ops-alert] {Kind} — {Subject} | {Body}",
                request.Kind, request.Subject, body.Replace('\n', ' '));

            // ---- SMS : la sonnette, traitée AVANT et INDÉPENDAMMENT -----
            // Volontairement placé avant la chaîne e-mail, et hors de ses
            // `return` : un e-mail regroupé ou plafonné ne doit pas faire taire
            // le SMS. Les deux canaux ont leurs propres compteurs et tombent en
            // panne séparément — c'est même quand la boîte déborde qu'on veut
            // encore être appelé.
            await TrySendSmsAsync(db, alert, request, now, ct);

            // ---- Regroupement ------------------------------------------
            // Une réserve de décaissement à sec fait échouer le retrait de
            // chaque école qui essaie : sans regroupement, ce serait vingt
            // e-mails identiques, et le vingt-et-unième — qui parlerait d'autre
            // chose — passerait inaperçu.
            var since = now.AddMinutes(-Math.Max(1, _settings.GroupingMinutes));
            var alreadySent = await db.OpsAlerts.AnyAsync(
                a => a.Id != alert.Id
                     && a.EmailedAt != null
                     && a.EmailedAt >= since
                     && a.GroupingKey == alert.GroupingKey, ct);
            if (alreadySent)
            {
                _logger.LogInformation(
                    "[ops-alert] {Key} déjà signalé dans les {Minutes} dernières minutes — regroupé.",
                    alert.GroupingKey, _settings.GroupingMinutes);
                return;
            }

            // ---- Plafond journalier ------------------------------------
            // Se faire limiter par Gmail ferait perdre AUSSI les e-mails métier
            // (identifiants d'un parent, factures d'abonnement) : le plafond
            // protège bien plus que notre confort.
            var sentToday = await db.OpsAlerts.CountAsync(
                a => a.EmailedAt != null && a.EmailedAt >= now.Date, ct);
            if (sentToday >= _settings.MaxEmailsPerDay)
            {
                _logger.LogWarning(
                    "[ops-alert] Plafond d'e-mails atteint ({Count}/jour) — {Key} enregistrée sans e-mail.",
                    sentToday, alert.GroupingKey);
                return;
            }

            var to = await AlertRecipient.ResolveAsync(db, _settings.Email, superAdmin, ct);
            if (to == null)
            {
                _logger.LogWarning("[ops-alert] Aucun destinataire — {Key} enregistrée sans e-mail.",
                    alert.GroupingKey);
                return;
            }

            await email.SendOpsAlertEmailAsync(to, new OpsAlertEmail
            {
                Urgent = IsUrgent(request.Kind),
                KindLabel = KindLabel(request.Kind),
                Heading = request.Subject,
                Subject = SubjectPrefix + request.Subject,
                Facts = request.Facts.Select(f => (f.Label, f.Value)).ToList(),
                Advice = request.Advice,
                CreatedAt = now,
            });

            // Marqué APRÈS l'envoi réussi : si SMTP échoue, la prochaine
            // occurrence du même défaut pourra retenter au lieu d'être regroupée
            // derrière un e-mail qui n'est jamais parti.
            alert.EmailedAt = now;
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("[ops-alert] Alerte {Kind} envoyée à {To:l} ({Key}).",
                request.Kind, to, alert.GroupingKey);
        }

        /// <summary>
        /// Envoie le SMS d'alerte, si cette nature le mérite et si la bourse du
        /// jour n'est pas épuisée.
        ///
        /// <para><b>Ne lève jamais</b> : une sonnette qui n'a pas sonné ne doit
        /// pas empêcher l'e-mail de partir, ni la ligne d'être écrite. Tout est
        /// déjà en base à ce stade.</para>
        /// </summary>
        private async Task TrySendSmsAsync(
            AppDbContext db, OpsAlert alert, OpsAlertRequest request, DateTime now, CancellationToken ct)
        {
            try
            {
                if (!_settings.SmsEnabled) return;
                if (!OpsAlertSms.ShouldSend(request.Kind)) return;

                // 🔴 Le numéro est validé ICI, et un numéro invalide éteint le
                // canal au lieu de tenter l'envoi. Ce n'est pas de la politesse :
                // un numéro étranger ferait lever au garde-fou une alerte
                // « destinataire hors Sénégal », qui est elle-même dans la liste
                // blanche du SMS, qui tenterait à nouveau le même numéro… La
                // boucle se ferme en refusant de partir, pas en s'en remettant à
                // un compteur.
                var phone = SenegalPhone.Normalize(_settings.SmsPhone);
                if (phone == null || !SmsSegmentCalculator.IsSenegalMobileE164(phone))
                {
                    if (!string.IsNullOrWhiteSpace(_settings.SmsPhone))
                        _logger.LogWarning(
                            "[ops-alert] Canal SMS éteint : OpsAlerts:SmsPhone n'est pas un mobile sénégalais valide.");
                    return;
                }

                // Regroupement, sur la marque du SMS et non sur celle de
                // l'e-mail. Même fenêtre, compteur distinct : quand la réserve
                // de décaissement est à sec, dix écoles échouent en quelques
                // minutes et dix SMS identiques videraient la bourse en une
                // fois, en noyant celui qui parlerait d'autre chose.
                var since = now.AddMinutes(-Math.Max(1, _settings.GroupingMinutes));
                var dejaSonne = await db.OpsAlerts.AnyAsync(
                    a => a.Id != alert.Id
                         && a.SmsSentAt != null
                         && a.SmsSentAt >= since
                         && a.GroupingKey == alert.GroupingKey, ct);
                if (dejaSonne)
                {
                    _logger.LogInformation(
                        "[ops-alert] SMS regroupé : {Key} a déjà sonné dans les {Minutes} dernières minutes.",
                        alert.GroupingKey, _settings.GroupingMinutes);
                    return;
                }

                // Bourse quotidienne du canal. C'est elle qui remplace les
                // plafonds dont ce canal est exempté : sans exemption il se
                // serait tu au bout de neuf messages (le numéro d'alerte avait
                // déjà reçu 11 SMS sur 30 jours au 2026-09-19, pour un plafond
                // par destinataire de 20), et sans bourse une boucle pourrait
                // envoyer sans fin.
                var sentToday = await db.OpsAlerts.CountAsync(
                    a => a.SmsSentAt != null && a.SmsSentAt >= now.Date, ct);
                if (sentToday >= Math.Max(0, _settings.SmsMaxPerDay))
                {
                    _logger.LogWarning(
                        "[ops-alert] Bourse SMS du jour épuisée ({Count}/jour) — {Key} reste en base et part par e-mail.",
                        sentToday, alert.GroupingKey);
                    return;
                }

                // Hors production, le SMS le DIT — et en tête, parce qu'une
                // notification tronquée sur un écran verrouillé ne montre que le
                // début. Même motif que SubjectPrefix : un banc d'essai qui
                // n'alerte pas ne prouve rien, mais une alerte de banc d'essai
                // indiscernable de la vraie fait croire la plateforme à l'arrêt.
                var headline = request.SmsHeadline ?? request.Subject;
                if (!_env.IsProduction()) headline = $"[TEST] {headline}";

                var text = OpsAlertSms.Compose(headline, now);

                using var scope = _scopeFactory.CreateScope();
                var notif = scope.ServiceProvider.GetRequiredService<INotificationService>();

                // Passe par le point unique d'envoi (règle CLAUDE.md) : c'est là
                // que vivent l'assainissement GSM-7 et le registre de dépense.
                // `Bilingual: false` et « fr » ne sont pas un oubli de l'arabe :
                // ce canal n'a qu'un destinataire, il est francophone, et un
                // corps bilingue coûterait TROIS segments au lieu d'un (§88).
                var ok = await notif.SendSmsAsync(new NotificationSmsRequest(
                    UserId: null,
                    RawPhone: phone,
                    PreferredLanguage: "fr",
                    Message: new BilingualMessage(text, string.Empty, PreComposed: true),
                    Bilingual: false,
                    TemplateCode: OpsAlertSms.TemplateCode,
                    RelatedEntityId: alert.Id,
                    Priority: SmsPriority.Critical,
                    TriggerSource: $"ops-alert:{request.Kind}",
                    OpsAlert: true), ct);

                if (!ok)
                {
                    // Pas de nouvelle tentative : la prochaine occurrence du même
                    // défaut retentera d'elle-même, puisque SmsSentAt reste nul
                    // et que le regroupement ne verra rien derrière quoi se taire.
                    _logger.LogWarning("[ops-alert] SMS non parti pour {Key}.", alert.GroupingKey);
                    return;
                }

                alert.SmsSentAt = now;
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("[ops-alert] SMS envoyé ({Kind}, {Key}).", request.Kind, alert.GroupingKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[ops-alert] Envoi SMS impossible pour {Key} — l'e-mail suit son cours.",
                    alert.GroupingKey);
            }
        }

        /// <summary>
        /// Urgent = de l'argent est bloqué, sorti à tort, ou la dépense dérape.
        /// Décide de la couleur du bandeau et du niveau de journal. Publique et
        /// pure exprès : c'est une règle de classement, elle doit se vérifier
        /// sans SMTP.
        /// </summary>
        public static bool IsUrgent(OpsAlertKind kind) => kind switch
        {
            OpsAlertKind.SmsHardCapReached => true,
            OpsAlertKind.SmsForeignRecipientBlocked => true,
            OpsAlertKind.WithdrawalProviderOutage => true,
            OpsAlertKind.WithdrawalStuck => true,
            OpsAlertKind.PayoutAnomaly => true,
            _ => false,
        };

        public static string KindLabel(OpsAlertKind kind) => kind switch
        {
            OpsAlertKind.SmsSoftCapReached => "Dépense SMS — palier d'alerte atteint",
            OpsAlertKind.SmsHardCapReached => "Dépense SMS — envois totalement suspendus",
            OpsAlertKind.SmsSchoolRunaway => "Dépense SMS — emballement sur une école",
            OpsAlertKind.SmsForeignRecipientBlocked => "SMS — destinataire hors Sénégal bloqué",
            OpsAlertKind.WithdrawalFailed => "Retrait — échec",
            OpsAlertKind.WithdrawalProviderOutage => "Retrait — le prestataire ne peut pas décaisser",
            OpsAlertKind.WithdrawalStuck => "Retrait — bloqué en vérification",
            OpsAlertKind.PayoutAnomaly => "Décaissement — anomalie comptable",
            _ => kind.ToString(),
        };

        private static string Truncate(string value, int max) =>
            string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";

        private static string? TruncateOrNull(string? value, int max) =>
            value == null ? null : Truncate(value, max);
    }
}
