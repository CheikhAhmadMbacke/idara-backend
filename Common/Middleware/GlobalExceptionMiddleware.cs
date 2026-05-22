using System.Net;
using System.Text.Json;
using Idara.API.DTOs.Common;

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
                _logger.LogError(ex, "Erreur non gérée");
                var message = _env.IsDevelopment()
                    ? $"{ex.Message}\n{ex.StackTrace}"
                    : "Une erreur interne est survenue. Veuillez réessayer plus tard.";
                await WriteErrorAsync(context, HttpStatusCode.InternalServerError, message);
            }
        }

        private static Task WriteErrorAsync(HttpContext context, HttpStatusCode statusCode, string message)
        {
            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";
            var payload = new ApiResponse<object> { Success = false, Message = message };
            return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
        }
    }
}
