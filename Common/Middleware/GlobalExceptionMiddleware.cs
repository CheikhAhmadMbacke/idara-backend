using System.Net;
using System.Text.Json;
using Idara.API.Common.Extensions;

namespace Idara.API.Common.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;
        private readonly IHostEnvironment _env;

        // Aligné avec la convention camelCase utilisée par les contrôleurs MVC
        // (System.Text.Json default depuis .NET 6). Sans ça, le middleware
        // sortait `{"Success":..,"Message":..}` (PascalCase) et le client ne
        // savait pas où chercher le message d'erreur.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IHostEnvironment env)
        {
            _next = next;
            _logger = logger;
            _env = env;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Accès non autorisé");
                await WriteErrorAsync(context, HttpStatusCode.Unauthorized, "Accès non autorisé.");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Opération invalide");
                await WriteErrorAsync(context, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                // Le code de corrélation figure DANS le message de journal en plus
                // d'être une propriété structurée : c'est lui qu'on colle dans la
                // page SuperAdmin quand un directeur nous dicte son code, et on
                // veut pouvoir le retrouver même en lisant le fichier à l'œil.
                // Le suffixe `:l` demande à Serilog d'écrire la valeur SANS
                // guillemets : sans lui, le message rendu donne
                // `Erreur non gérée ["IDR-7K2MQ4"] sur "POST" "/api/students"`,
                // pénible à relire dans la page SuperAdmin.
                _logger.LogError(ex, "Erreur non gérée [{Trace:l}] sur {Method:l} {Path:l}",
                    context.GetTraceCode(), context.Request.Method, context.Request.Path);

                var message = _env.IsDevelopment()
                    ? $"{ex.Message}\n{ex.StackTrace}"
                    : "Une erreur interne est survenue. Veuillez réessayer plus tard.";
                await WriteErrorAsync(context, HttpStatusCode.InternalServerError, message);
            }
        }

        private static Task WriteErrorAsync(HttpContext context, HttpStatusCode statusCode, string message)
        {
            // Une réponse déjà commencée (streaming, PDF, fichier statique) ne peut
            // plus être remplacée : réécrire son statut lèverait à son tour, et le
            // client recevrait un corps tronqué illisible. On laisse la connexion
            // se terminer — l'erreur est de toute façon journalisée avec son code.
            if (context.Response.HasStarted) return Task.CompletedTask;

            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";

            // Corps volontairement construit ici plutôt que via ApiResponse<T> :
            // le code de corrélation n'a de sens que sur une réponse d'erreur, et
            // l'ajouter au DTO partagé le ferait apparaître dans les centaines de
            // réponses de succès de l'API. Les clients ignorent les champs
            // inconnus, donc `traceCode` est purement additif.
            var payload = new
            {
                success = false,
                message,
                data = (object?)null,
                traceCode = context.GetTraceCode(),
            };
            return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
        }
    }
}
