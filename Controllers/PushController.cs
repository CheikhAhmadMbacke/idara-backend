using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Push;
using Idara.API.Models;
using Idara.API.Services.Push;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Gestion des jetons d'appareil pour les notifications push (FCM).
    /// Tout utilisateur authentifié peut enregistrer/désenregistrer l'appareil
    /// courant. Le push lui-même part via <c>NotificationService</c> (aucun envoi
    /// déclenché ici).
    /// </summary>
    [ApiController]
    [Route("api/push")]
    [Authorize]
    public class PushController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IPushService _push;
        private readonly ILogger<PushController> _logger;

        public PushController(AppDbContext context, IPushService push, ILogger<PushController> logger)
        {
            _context = context;
            _push = push;
            _logger = logger;
        }

        /// <summary>
        /// Enregistre (ou rafraîchit) le jeton FCM de l'appareil courant pour
        /// l'utilisateur connecté. Idempotent : si le jeton existe déjà, il est
        /// réaffecté au propriétaire courant (appareil partagé / re-login).
        /// </summary>
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterPushTokenDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var platform = string.IsNullOrWhiteSpace(dto.Platform) ? "unknown" : dto.Platform.Trim().ToLowerInvariant();

            var existing = await _context.PushDeviceTokens
                .FirstOrDefaultAsync(t => t.Token == dto.Token, ct);

            if (existing == null)
            {
                _context.PushDeviceTokens.Add(new PushDeviceToken
                {
                    UserId = userId.Value,
                    Token = dto.Token,
                    Platform = platform,
                    CreatedAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.UserId = userId.Value; // réaffectation au propriétaire courant
                existing.Platform = platform;
                existing.LastSeenAt = DateTime.UtcNow;
            }

            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Course possible (deux register simultanés du même token) : inoffensif.
                _logger.LogWarning(ex, "[push] register : conflit d'insertion token (probable course), ignoré.");
            }

            return Ok(ApiResponse<object?>.Ok(null, "Appareil enregistré."));
        }

        /// <summary>
        /// Envoie une notification de TEST aux appareils de l'utilisateur connecté.
        /// Sert à vérifier en un clic que le push fonctionne pour ce compte (test
        /// E2E + diagnostic support), sans devoir déclencher un vrai paiement.
        /// Purge les tokens morts au passage.
        /// </summary>
        [HttpPost("test")]
        public async Task<IActionResult> SendTest(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            if (!_push.IsConfigured)
                return Ok(ApiResponse<object?>.Fail("Le push n'est pas encore configuré côté serveur."));

            var tokens = await _context.PushDeviceTokens
                .Where(t => t.UserId == userId.Value)
                .ToListAsync(ct);
            if (tokens.Count == 0)
                return Ok(ApiResponse<object?>.Fail(
                    "Aucun appareil enregistré. Ouvrez l'app et acceptez les notifications, puis réessayez."));

            var data = new Dictionary<string, string> { ["templateCode"] = "TEST" };
            var ok = 0;
            var stale = new List<PushDeviceToken>();
            foreach (var t in tokens)
            {
                var r = await _push.SendAsync(
                    t.Token, "Idara",
                    "Test de notification — tout fonctionne !",
                    data, link: "https://idara.sn", ct);
                if (r.Success) ok++;
                else if (r.TokenInvalid) stale.Add(t);
            }
            if (stale.Count > 0)
            {
                _context.PushDeviceTokens.RemoveRange(stale);
                await _context.SaveChangesAsync(ct);
            }

            _logger.LogInformation("[push] test envoyé : {Ok}/{Total} appareil(s) pour user {UserId}",
                ok, tokens.Count, userId);

            return ok > 0
                ? Ok(ApiResponse<object?>.Ok(null, $"Notification de test envoyée vers {ok} appareil(s)."))
                : Ok(ApiResponse<object?>.Fail("Aucun appareil n'a pu être notifié (jetons expirés ?)."));
        }

        /// <summary>Désenregistre le jeton de l'appareil courant (au logout).</summary>
        [HttpPost("unregister")]
        public async Task<IActionResult> Unregister([FromBody] UnregisterPushTokenDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            await _context.PushDeviceTokens
                .Where(t => t.Token == dto.Token && t.UserId == userId.Value)
                .ExecuteDeleteAsync(ct);

            return Ok(ApiResponse<object?>.Ok(null, "Appareil désenregistré."));
        }
    }
}
