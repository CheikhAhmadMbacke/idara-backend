using System.Text.Json;
using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Idara.API.Common.Filters
{
    /// <summary>
    /// Rend rejouable sans risque toute écriture accompagnée d'un en-tête
    /// <c>Idempotency-Key</c>.
    ///
    /// <para><b>Pourquoi (lot 3 du chantier performance, 2026-07-28).</b> Une
    /// saisie faite sans réseau est mise en file d'attente sur le téléphone et
    /// renvoyée plus tard. Mais quand une requête échoue par coupure, le client
    /// ne sait pas si le serveur l'a traitée : la demande est peut-être arrivée
    /// et c'est la réponse qui s'est perdue. Sans garde-fou, le rejeu créerait
    /// un doublon — une écriture de caisse comptée deux fois, par exemple.</para>
    ///
    /// <para><b>Fonctionnement.</b> Le client génère un identifiant par saisie et
    /// le répète à chaque tentative. La première fois, on exécute et on mémorise
    /// la réponse ; ensuite, on la renvoie sans rien réexécuter.</para>
    ///
    /// <para><b>Aucun effet sans l'en-tête.</b> Le filtre est global mais
    /// totalement inerte tant que le client n'envoie pas la clé : le
    /// comportement de l'API est donc strictement inchangé pour tout l'existant,
    /// y compris le site web et les anciennes versions de l'application.</para>
    /// </summary>
    public class IdempotencyFilter : IAsyncActionFilter
    {
        public const string HeaderName = "Idempotency-Key";

        /// Au-delà, on refuse : une clé est un identifiant, pas un fourre-tout.
        private const int MaxKeyLength = 120;

        /// Un corps de réponse démesuré n'a pas à être conservé pour un rejeu ;
        /// on préfère alors laisser la requête se réexécuter (les endpoints
        /// concernés sont de toute façon en upsert par clé naturelle).
        private const int MaxStoredBodyBytes = 64 * 1024;

        private readonly AppDbContext _db;
        private readonly ILogger<IdempotencyFilter> _logger;
        private readonly JsonSerializerOptions _json;

        public IdempotencyFilter(
            AppDbContext db,
            ILogger<IdempotencyFilter> logger,
            Microsoft.Extensions.Options.IOptions<MvcJsonOptions> mvcJson)
        {
            _db = db;
            _logger = logger;
            // On réutilise les options de sérialisation DE MVC, et surtout on ne
            // les redevine pas : le corps rejoué doit être identique, octet pour
            // octet, à celui de la première réponse.
            //
            // ⚠️ Piège : `Program.cs` configure `Microsoft.AspNetCore.Http.Json`
            // avec `PropertyNamingPolicy = null` — ces options-là ne concernent
            // QUE les API minimales. Les contrôleurs MVC, eux, sérialisent en
            // camelCase (le défaut), ce que confirment les DTO côté Dart qui
            // lisent `balanceAfter` et non `BalanceAfter`.
            _json = mvcJson.Value.JsonSerializerOptions;
        }

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var http = context.HttpContext;
            var method = http.Request.Method;

            // Seules les écritures sont concernées : un GET est déjà idempotent.
            var isWrite = HttpMethods.IsPost(method)
                || HttpMethods.IsPut(method)
                || HttpMethods.IsPatch(method)
                || HttpMethods.IsDelete(method);
            if (!isWrite || !http.Request.Headers.TryGetValue(HeaderName, out var raw))
            {
                await next();
                return;
            }

            var key = raw.ToString().Trim();
            var userId = http.User.GetUserId();
            if (string.IsNullOrWhiteSpace(key) || userId == null)
            {
                // Pas d'utilisateur identifié : on ne saurait pas à qui
                // rattacher la clé, donc on n'active pas le mécanisme.
                await next();
                return;
            }
            if (key.Length > MaxKeyLength)
            {
                context.Result = new BadRequestObjectResult(
                    DTOs.Common.ApiResponse<bool>.Fail(
                        $"En-tête {HeaderName} trop long ({MaxKeyLength} caractères maximum)."));
                return;
            }

            var endpoint = $"{method} {http.Request.Path}";

            var existing = await _db.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == userId.Value && r.Key == key,
                    http.RequestAborted);

            if (existing != null)
            {
                if (!string.Equals(existing.Endpoint, endpoint, StringComparison.Ordinal))
                {
                    // Même clé sur un autre endpoint : c'est un bug du client, pas
                    // une reprise. Renvoyer la réponse mémorisée serait pire que
                    // tout — elle n'aurait aucun rapport avec ce qui est demandé.
                    _logger.LogWarning(
                        "[idempotency] Clé {Key} réutilisée sur {New} (d'origine {Old})",
                        key, endpoint, existing.Endpoint);
                    context.Result = new ConflictObjectResult(
                        DTOs.Common.ApiResponse<bool>.Fail(
                            "Cette clé d'idempotence a déjà servi pour une autre opération."));
                    return;
                }

                _logger.LogInformation("[idempotency] Rejeu de {Endpoint} (clé {Key})", endpoint, key);
                context.Result = new ContentResult
                {
                    StatusCode = existing.StatusCode,
                    ContentType = "application/json; charset=utf-8",
                    Content = existing.ResponseBody,
                };
                return;
            }

            var executed = await next();

            // ⚠️ Le statut se lit sur le RÉSULTAT, pas sur `Response.StatusCode`.
            // Un filtre d'action s'exécute AVANT que le résultat ne soit écrit
            // dans la réponse : à cet instant `Response.StatusCode` vaut encore
            // 200 par défaut, même quand l'action renvoie un BadRequest. S'y
            // fier ferait mémoriser un échec comme un succès, et le rejeu
            // renverrait éternellement cette erreur.
            var status = executed.Result switch
            {
                ObjectResult o => o.StatusCode ?? StatusCodes.Status200OK,
                StatusCodeResult s => s.StatusCode,
                null => StatusCodes.Status200OK,
                _ => StatusCodes.Status200OK,
            };

            // On ne mémorise que les succès : un échec doit pouvoir être retenté
            // pour de bon, pas renvoyer éternellement la même erreur.
            if (executed.Exception != null || status < 200 || status >= 300) return;

            string body;
            try
            {
                body = executed.Result switch
                {
                    ObjectResult o when o.Value != null =>
                        JsonSerializer.Serialize(o.Value, _json),
                    ContentResult c => c.Content ?? string.Empty,
                    _ => string.Empty,
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[idempotency] Réponse non sérialisable pour {Endpoint}", endpoint);
                return;
            }

            if (body.Length > MaxStoredBodyBytes) return;

            _db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Key = key,
                UserId = userId.Value,
                Endpoint = endpoint,
                StatusCode = status,
                ResponseBody = body,
            });
            try
            {
                await _db.SaveChangesAsync(http.RequestAborted);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Deux rejeux simultanés de la même saisie : le premier a gagné,
                // les deux réponses sont équivalentes. Rien à corriger.
                _logger.LogInformation("[idempotency] Clé {Key} déjà enregistrée (course)", key);
            }
            catch (Exception ex)
            {
                // Best-effort : l'écriture métier a réussi et a été renvoyée au
                // client. Ne pas pouvoir mémoriser la trace ne doit surtout pas
                // transformer ce succès en erreur.
                _logger.LogError(ex, "[idempotency] Enregistrement impossible pour {Endpoint}", endpoint);
            }
        }

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505";
    }
}
