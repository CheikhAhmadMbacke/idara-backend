using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Observability;
using Idara.API.Models;
using Idara.API.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Services.Observability
{
    public interface IIncidentAlertService
    {
        /// <summary>
        /// Prévient le SuperAdmin par e-mail. <b>Ne bloque pas l'appelant</b> et
        /// ne lève jamais.
        /// </summary>
        void QueueAlert(int incidentId);
    }

    /// <summary>
    /// Envoie une alerte par e-mail dès qu'un utilisateur rencontre un problème.
    ///
    /// <para><b>Pourquoi c'est LA pièce qui rend l'observabilité utile ici.</b> Le
    /// public d'Idara — directeurs de daara, parents, donateurs — ne remonte pas
    /// les problèmes : il abandonne l'écran, ou appelle sans savoir dire ce qui
    /// s'est passé, et beaucoup lisent le français (et l'arabe) avec difficulté.
    /// Un dispositif qui repose sur « l'utilisateur nous dicte son code » ne
    /// capte qu'une fraction des incidents. Avec l'alerte, <b>il n'a plus rien à
    /// faire du tout</b> : l'e-mail arrive avec son numéro de téléphone, et c'est
    /// nous qui l'appelons.</para>
    ///
    /// <para><b>Non bloquant, volontairement.</b> Un envoi SMTP prend une à trois
    /// secondes. L'application, elle, attend la réponse de l'endpoint : faire
    /// patienter un utilisateur déjà en difficulté pour envoyer notre propre
    /// e-mail serait absurde. L'alerte partira donc en tâche de fond, avec son
    /// propre périmètre d'injection (motif de <c>NotificationService</c>) pour ne
    /// pas se servir d'un <c>DbContext</c> dont la requête est terminée.</para>
    /// </summary>
    public class IncidentAlertService : IIncidentAlertService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ObservabilitySettings _settings;
        private readonly ILogger<IncidentAlertService> _logger;

        public IncidentAlertService(
            IServiceScopeFactory scopeFactory,
            IOptions<ObservabilitySettings> settings,
            ILogger<IncidentAlertService> logger)
        {
            _scopeFactory = scopeFactory;
            _settings = settings.Value;
            _logger = logger;
        }

        /// <summary>
        /// Marqueur que l'application pose dans le message d'un redémarrage
        /// provoqué par la mise à jour du service worker (§79). ⚠️ Doit rester
        /// IDENTIQUE au littéral de <c>restart_detector.dart</c> côté Flutter.
        /// </summary>
        public const string SwReloadMarker = "cause=mise a jour service worker";

        /// <summary>
        /// Un redémarrage « cause=mise a jour service worker » est NOTRE propre
        /// geste : chaque déploiement web en produit un par utilisateur actif, à
        /// mesure que les navigateurs découvrent le nouveau service worker
        /// (jusqu'à 24 h+ après le push). L'alerter par e-mail noierait les vrais
        /// incidents — constaté le 2026-08-28, première vague après le
        /// déploiement du détecteur. L'incident reste ENREGISTRÉ (chip
        /// « Redémarrages », page SuperAdmin) : seul l'e-mail est retenu.
        ///
        /// Publique et pure exprès (§133) : c'est elle qui décide qu'un e-mail ne
        /// part pas, elle doit se vérifier sans SMTP ni base.
        /// </summary>
        public static bool IsSelfInflictedRestart(Enums.IncidentKind kind, string? message) =>
            kind == Enums.IncidentKind.UnexpectedRestart &&
            message != null &&
            message.Contains(SwReloadMarker, StringComparison.Ordinal);

        public void QueueAlert(int incidentId)
        {
            if (!_settings.AlertsEnabled) return;

            // Détaché de la requête en cours. `CancellationToken.None` : l'alerte
            // ne doit pas être annulée parce que le téléphone a coupé la
            // connexion juste après avoir envoyé son rapport — c'est même le cas
            // le plus probable quand l'application vient de planter.
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendAsync(incidentId);
                }
                catch (Exception ex)
                {
                    // Une alerte ratée ne doit jamais devenir un incident : elle
                    // est déjà consignée en base et dans le journal.
                    _logger.LogWarning(ex,
                        "[observability] Alerte e-mail impossible pour l'incident {Id}.", incidentId);
                }
            });
        }

        /// <summary>
        /// Publique pour être vérifiable sur banc (QueueAlert part en tâche de
        /// fond, inattendable) — même motif que <c>EmailService.BuildIncidentAlert</c>.
        /// </summary>
        public async Task SendAsync(int incidentId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
            var superAdmin = scope.ServiceProvider
                .GetRequiredService<IOptions<SuperAdminSettings>>().Value;

            var incident = await db.ClientIncidents
                .AsNoTracking()
                .Include(i => i.User)
                .Include(i => i.School)
                .FirstOrDefaultAsync(i => i.Id == incidentId);
            if (incident == null) return;

            // Reload du service worker : notre propre geste, jamais un e-mail.
            // L'incident est déjà en base (chip « Redémarrages ») ; `AlertedAt`
            // reste vide, donc il ne « consomme » ni le regroupement ni le
            // plafond journalier des vrais incidents.
            if (IsSelfInflictedRestart(incident.Kind, incident.Message))
            {
                _logger.LogInformation(
                    "[observability] Redémarrage dû à la mise à jour du service worker — " +
                    "incident {Code:l} compté sans e-mail.", incident.Code);
                return;
            }

            var now = DateTime.UtcNow;

            // ---- Plafond journalier -------------------------------------
            // Se faire limiter par Gmail ferait perdre AUSSI les e-mails métier
            // (identifiants d'un nouveau parent, factures d'abonnement) : le
            // plafond protège bien plus que notre confort.
            var sentToday = await db.ClientIncidents
                .CountAsync(i => i.AlertedAt != null && i.AlertedAt >= now.Date);
            if (sentToday >= _settings.MaxAlertEmailsPerDay)
            {
                _logger.LogWarning(
                    "[observability] Plafond d'alertes atteint ({Count}/jour) — incident {Code:l} non alerté. " +
                    "Un défaut touche vraisemblablement beaucoup d'appareils.",
                    sentToday, incident.Code);
                return;
            }

            // ---- Regroupement -------------------------------------------
            // Un même défaut = même écran + même type d'erreur + même nature.
            // Vingt e-mails identiques n'apprennent rien de plus que le premier,
            // et noieraient le suivant qui, lui, serait nouveau.
            var since = now.AddMinutes(-Math.Max(1, _settings.AlertGroupingMinutes));
            var alreadyAlerted = await db.ClientIncidents.AnyAsync(i =>
                i.Id != incident.Id &&
                i.AlertedAt != null &&
                i.AlertedAt >= since &&
                i.Kind == incident.Kind &&
                i.Route == incident.Route &&
                i.ExceptionType == incident.ExceptionType);
            if (alreadyAlerted)
            {
                _logger.LogInformation(
                    "[observability] Défaut déjà signalé dans les {Minutes} dernières minutes — " +
                    "incident {Code:l} regroupé, pas de second e-mail.",
                    _settings.AlertGroupingMinutes, incident.Code);
                return;
            }

            // ---- Destinataire -------------------------------------------
            var to = await ResolveRecipientAsync(db, superAdmin);
            if (to == null)
            {
                _logger.LogWarning("[observability] Aucun destinataire d'alerte — e-mail non envoyé.");
                return;
            }

            // Combien de personnes DISTINCTES touchées par le même défaut en 24 h :
            // « 1 » et « 14 » n'appellent pas la même réaction. Les redémarrages
            // dus au service worker en sont EXCLUS : ils partagent l'écran et le
            // type des vrais redémarrages et gonfleraient le « ×N » d'un e-mail
            // qui, lui, parle d'autre chose.
            var dayAgo = now.AddHours(-24);
            var similar = await db.ClientIncidents
                .Where(i => i.CreatedAt >= dayAgo &&
                            i.Kind == incident.Kind &&
                            i.Route == incident.Route &&
                            i.ExceptionType == incident.ExceptionType &&
                            !i.Message.Contains(SwReloadMarker))
                .Select(i => i.UserId)
                .Distinct()
                .CountAsync();

            var alert = new IncidentAlertEmail
            {
                Code = incident.Code,
                KindLabel = KindLabel(incident.Kind),
                CreatedAt = incident.CreatedAt,
                PersonName = incident.User?.FullName ?? "Compte inconnu",
                RoleLabel = RoleLabel(incident.Role),
                // Vide si absent : la ligne est alors simplement omise de l'e-mail.
                PhoneNumber = Common.Utilities.SenegalPhone.ToDisplay(incident.User?.PhoneNumber),
                SchoolName = incident.School?.Name ?? "—",
                Route = incident.Route,
                Message = incident.Message,
                ExceptionType = incident.ExceptionType,
                Platform = incident.Platform,
                AppVersion = incident.AppVersion,
                Device = incident.Device,
                LocaleCode = incident.LocaleCode,
                UserComment = incident.UserComment,
                RequestTrace = incident.RequestTrace,
                StackTrace = incident.StackTrace,
                SimilarLast24h = Math.Max(1, similar),
            };

            await email.SendIncidentAlertEmailAsync(to, alert);

            // Marqué APRÈS l'envoi réussi : si l'e-mail échoue, le prochain
            // incident du même défaut pourra retenter. Mise à jour SUIVIE et non
            // `ExecuteUpdateAsync` : le change tracker n'écrit que `AlertedAt`
            // (même effet), et la version relationnelle est intraduisible sur le
            // banc InMemory (§143) — ce chemin serait alors invérifiable.
            var row = await db.ClientIncidents
                .FirstOrDefaultAsync(i => i.Id == incident.Id);
            if (row != null)
            {
                row.AlertedAt = now;
                await db.SaveChangesAsync();
            }

            _logger.LogInformation(
                "[observability] Alerte envoyée à {To:l} pour l'incident {Code:l} ({Similar} personne(s) touchée(s)).",
                to, incident.Code, alert.SimilarLast24h);
        }

        /// <summary>
        /// Destinataire : le réglage explicite, sinon les comptes SuperAdmin de
        /// la base, sinon la configuration de démarrage. Aucune variable
        /// d'environnement n'est donc nécessaire pour que les alertes marchent.
        /// </summary>
        private async Task<string?> ResolveRecipientAsync(AppDbContext db, SuperAdminSettings superAdmin)
        {
            var configured = _settings.AlertEmail?.Trim();
            if (!string.IsNullOrEmpty(configured)) return configured;

            var fromDb = await db.Users
                .Where(u => u.Role == UserRoles.SuperAdmin && !u.IsDeleted && u.Email != null)
                .OrderBy(u => u.Id)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(fromDb)) return fromDb;

            return string.IsNullOrWhiteSpace(superAdmin.Email) ? null : superAdmin.Email;
        }

        private static string KindLabel(Enums.IncidentKind kind) => kind switch
        {
            Enums.IncidentKind.FlutterError => "L'écran a planté",
            Enums.IncidentKind.UserReport => "Signalé par l'utilisateur",
            Enums.IncidentKind.ApiError => "Le service n'a pas répondu",
            Enums.IncidentKind.UnexpectedRestart => "L'application a redémarré toute seule",
            _ => kind.ToString(),
        };

        private static string RoleLabel(string role) => role switch
        {
            UserRoles.SchoolAdmin => "Directeur",
            UserRoles.SchoolStaff => "Personnel",
            UserRoles.SchoolViewer => "Observateur",
            UserRoles.Teacher => "Enseignant",
            UserRoles.Surveillant => "Surveillant",
            UserRoles.Guardian => "Parent",
            UserRoles.Donor => "Donateur",
            UserRoles.SuperAdmin => "SuperAdmin",
            _ => role,
        };
    }
}
