using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Idara.API.Options;
using Microsoft.Extensions.Options;

namespace Idara.API.Services.Push
{
    /// <summary>
    /// Service de notifications push via Firebase Cloud Messaging (SDK officiel
    /// FirebaseAdmin, API HTTP v1 — l'OAuth et le rafraîchissement de token sont
    /// gérés par le SDK). Calqué sur <see cref="OrangeSmsService"/> :
    /// NE LÈVE JAMAIS, et NO-OP propre si aucun compte de service n'est configuré
    /// (déploiement avant configuration sans risque).
    ///
    /// Enregistré en singleton : <see cref="FirebaseApp"/> est coûteux à créer et
    /// ne doit l'être qu'une fois. L'initialisation est paresseuse et thread-safe.
    /// </summary>
    public class FcmPushService : IPushService
    {
        private const string AppName = "idara-fcm";

        private readonly FcmSettings _settings;
        private readonly ILogger<FcmPushService> _logger;

        private readonly object _lock = new();
        private bool _initAttempted;
        private FirebaseMessaging? _messaging;

        public FcmPushService(IOptions<FcmSettings> settings, ILogger<FcmPushService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_settings.ServiceAccountPath)
            || !string.IsNullOrWhiteSpace(_settings.ServiceAccountJson);

        public async Task<PushSendResult> SendAsync(
            string token, string title, string body,
            IReadOnlyDictionary<string, string>? data = null,
            string? link = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(token))
                return new PushSendResult(false, Error: "Empty token");

            var messaging = GetMessaging();
            if (messaging == null)
            {
                _logger.LogWarning("[fcm] push non envoyé : compte de service FCM non configuré (no-op).");
                return new PushSendResult(false, Error: "Push provider not configured");
            }

            var message = new Message
            {
                Token = token,
                Notification = new Notification { Title = title, Body = body },
                Data = data?.ToDictionary(kv => kv.Key, kv => kv.Value),
                Android = new AndroidConfig { Priority = Priority.High },
                Webpush = string.IsNullOrWhiteSpace(link)
                    ? null
                    : new WebpushConfig { FcmOptions = new WebpushFcmOptions { Link = link } }
            };

            try
            {
                var id = await messaging.SendAsync(message, ct);
                return new PushSendResult(true, MessageId: id);
            }
            catch (FirebaseMessagingException ex)
            {
                // Token mort (app désinstallée / token expiré ou émetteur changé) :
                // signale à l'appelant de le purger. JAMAIS purger sur une autre
                // erreur (transport, quota, message invalide) pour ne pas perdre un
                // bon token à cause d'un bug temporaire.
                var invalid = ex.MessagingErrorCode is MessagingErrorCode.Unregistered
                                                     or MessagingErrorCode.SenderIdMismatch;
                _logger.LogWarning(
                    "[fcm] envoi {Result} code={Code} : {Msg}",
                    invalid ? "TOKEN_INVALID" : "REJECTED", ex.MessagingErrorCode, ex.Message);
                return new PushSendResult(false, Error: ex.MessagingErrorCode?.ToString() ?? "FcmError",
                    TokenInvalid: invalid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[fcm] exception inattendue à l'envoi push.");
                return new PushSendResult(false, Error: "Unexpected");
            }
        }

        /// <summary>
        /// Initialise (une seule fois) l'app Firebase et renvoie le client de
        /// messagerie, ou null si non configuré / échec d'init. Thread-safe.
        /// </summary>
        private FirebaseMessaging? GetMessaging()
        {
            if (_messaging != null) return _messaging;
            if (!IsConfigured) return null;

            lock (_lock)
            {
                if (_messaging != null) return _messaging;
                if (_initAttempted) return _messaging; // échec déjà tenté : ne pas réessayer en boucle
                _initAttempted = true;

                try
                {
                    // FromFile/FromJson sont marqués obsolètes dans Google.Apis.Auth
                    // récent, mais restent les constructeurs attendus par
                    // FirebaseAdmin (AppOptions.Credential veut un GoogleCredential).
                    // Suppression locale du warning de dépréciation.
#pragma warning disable CS0618
                    GoogleCredential credential =
                        !string.IsNullOrWhiteSpace(_settings.ServiceAccountPath)
                            ? GoogleCredential.FromFile(_settings.ServiceAccountPath)
                            : GoogleCredential.FromJson(_settings.ServiceAccountJson);
#pragma warning restore CS0618

                    // App nommée (pas la default) pour éviter tout conflit.
                    // ⚠️ FirebaseApp.GetInstance(name) RENVOIE null si l'app n'existe
                    // pas encore (il ne lève PAS) — d'où la création explicite quand
                    // c'est le cas. Le try/catch couvre les variantes de version.
                    FirebaseApp? app;
                    try { app = FirebaseApp.GetInstance(AppName); }
                    catch { app = null; }

                    if (app == null)
                    {
                        try
                        {
                            app = FirebaseApp.Create(new AppOptions { Credential = credential }, AppName);
                        }
                        catch
                        {
                            // Course rare (app créée entre-temps) : on la récupère.
                            app = FirebaseApp.GetInstance(AppName);
                        }
                    }

                    _messaging = FirebaseMessaging.GetMessaging(app);
                    _logger.LogInformation("[fcm] FirebaseApp initialisée, push activé.");
                    return _messaging;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[fcm] échec d'initialisation FirebaseApp — push désactivé.");
                    return null;
                }
            }
        }
    }
}
