using Idara.API.Common.Extensions;
using Serilog.Core;
using Serilog.Events;

namespace Idara.API.Common.Observability
{
    /// <summary>
    /// Attache à chaque ligne de journal le code de corrélation et l'identité de
    /// l'appelant (utilisateur, école, rôle).
    ///
    /// <para><b>C'est le trou n°2 du diagnostic</b> : jusqu'ici une erreur
    /// serveur atterrissait dans <c>journalctl</c> sans le moindre moyen de la
    /// rattacher à un utilisateur, à une école ni à un geste. Retrouver
    /// « l'erreur de 14 h 30 chez le daara de Touba » supposait de lire à l'œil
    /// un journal texte mêlant toutes les écoles.</para>
    ///
    /// <para><b>Pourquoi un enrichisseur et non un <c>LogContext</c> poussé par le
    /// middleware.</b> L'identité n'est connue qu'après l'authentification, alors
    /// que le code de corrélation doit être posé tout au début du pipeline (le
    /// middleware d'exceptions en a besoin). Un enrichisseur lit le contexte **au
    /// moment où la ligne est écrite** : le problème d'ordre des middlewares
    /// disparaît, et une ligne émise dans un contrôleur porte bien l'identité même
    /// si le middleware de corrélation, lui, s'exécute avant l'authentification.</para>
    ///
    /// <para>Coût : quelques lectures de dictionnaire par ligne écrite, sur un
    /// <c>AsyncLocal</c>. À 0,005 requête/seconde, c'est hors sujet.</para>
    /// </summary>
    public class IdaraLogEnricher : ILogEventEnricher
    {
        private readonly IHttpContextAccessor _accessor;

        public IdaraLogEnricher(IHttpContextAccessor accessor) => _accessor = accessor;

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
        {
            var http = _accessor.HttpContext;
            if (http == null) return; // Ligne émise par un cron : rien à corréler.

            var trace = http.GetTraceCode();
            if (!string.IsNullOrEmpty(trace))
            {
                logEvent.AddPropertyIfAbsent(factory.CreateProperty("Trace", trace));
            }

            var user = http.User;
            if (user?.Identity?.IsAuthenticated != true) return;

            var userId = user.GetUserId();
            if (userId != null)
            {
                logEvent.AddPropertyIfAbsent(factory.CreateProperty("UserId", userId.Value));
            }

            var schoolId = user.GetSchoolId();
            if (schoolId != null)
            {
                logEvent.AddPropertyIfAbsent(factory.CreateProperty("SchoolId", schoolId.Value));
            }

            var role = user.GetRole();
            if (!string.IsNullOrEmpty(role))
            {
                logEvent.AddPropertyIfAbsent(factory.CreateProperty("Role", role));
            }
        }
    }
}
