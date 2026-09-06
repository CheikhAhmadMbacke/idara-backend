using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.DTOs.Alerts;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
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
