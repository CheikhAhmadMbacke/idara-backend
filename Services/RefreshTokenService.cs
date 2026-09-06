using System.Security.Cryptography;
using System.Text;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.Models;
using Idara.API.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    /// <summary>
    /// Cycle de vie des refresh tokens : création à la connexion, rotation à
    /// chaque expiration de l'access token, révocation.
    ///
    /// <para><b>Objectif produit : ne JAMAIS redemander de se connecter.</b> Le
    /// public d'Idara, ce sont des parents qui ouvrent l'application une fois par
    /// mois pour payer. Une session qui expire, c'est un paiement qui n'arrive
    /// pas. La session glisse donc à chaque usage (§223).</para>
    /// </summary>
    public class RefreshTokenService : IRefreshTokenService
    {
        private readonly AppDbContext _context;
        private readonly JwtSettings _settings;
        private readonly ILogger<RefreshTokenService> _logger;

        public RefreshTokenService(
            AppDbContext context,
            IOptions<JwtSettings> settings,
            ILogger<RefreshTokenService> logger)
        {
            _context = context;
            _settings = settings.Value;
            _logger = logger;
        }

        // ----- Public API -----

        public async Task<string> CreateAsync(int userId)
        {
            var role = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.Role)
                .FirstOrDefaultAsync();

            var raw = GenerateRawToken();
            _context.RefreshTokens.Add(new RefreshToken
            {
                UserId = userId,
                TokenHash = Hash(raw),
                FamilyId = NewFamilyId(),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(LifetimeDaysFor(role)),
            });
            await _context.SaveChangesAsync();
            return raw;
        }

        public async Task<(User user, string newToken)?> RotateAsync(string oldToken)
        {
            if (string.IsNullOrWhiteSpace(oldToken)) return null;

            var hash = Hash(oldToken);
            var record = await _context.RefreshTokens
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.TokenHash == hash);

            // Token inconnu (jamais émis OU purgé).
            if (record == null) return null;

            var now = DateTime.UtcNow;

            if (record.RevokedAt.HasValue)
            {
                // Jeton déjà tourné il y a quelques secondes : c'est la RÉPONSE
                // qui s'est perdue, pas un rejeu. Cas réel et fréquent sur un
                // téléphone d'entrée de gamme — Android tue l'application entre
                // la réponse HTTP et l'écriture dans le coffre-fort, ou le réseau
                // coupe pile là. Punir ce cas, c'est déconnecter des familles
                // pour une coupure de réseau.
                if (IsRotation(record.RevokedReason)
                    && now - record.RevokedAt.Value <= GraceWindow)
                {
                    return await ReissueInFamilyAsync(record, now);
                }

                // Rejeu véritable : on brûle LA CHAÎNE (cette connexion, sur cet
                // appareil), jamais toutes les sessions du compte.
                _logger.LogWarning(
                    "[refresh] Rejeu détecté sur la famille {Family} (user {UserId}, révoqué le {RevokedAt} pour {Reason}) → chaîne révoquée",
                    record.FamilyId, record.UserId, record.RevokedAt, record.RevokedReason);
                await RevokeFamilyAsync(record.FamilyId, "Replay-detected");
                return null;
            }

            if (now >= record.ExpiresAt) return null;

            var newRaw = GenerateRawToken();
            var newHash = Hash(newRaw);

            using var tx = await _context.Database.BeginTransactionAsync();

            // Révocation CONDITIONNELLE : « passe de non-révoqué à révoqué ».
            // Deux rafraîchissements simultanés (deux onglets, deux requêtes
            // parties ensemble) ne peuvent donc pas produire deux jetons vivants
            // dans la même famille — le perdant repart par la fenêtre de grâce.
            var affected = await _context.RefreshTokens
                .Where(r => r.Id == record.Id && r.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.RevokedAt, now)
                    .SetProperty(r => r.RevokedReason, "Rotated")
                    .SetProperty(r => r.ReplacedByTokenHash, newHash)
                    .SetProperty(r => r.LastUsedAt, now));

            if (affected == 0)
            {
                await tx.RollbackAsync();
                // §52 : après un rollback, le change tracker ment. On relit.
                _context.ChangeTracker.Clear();
                var fresh = await _context.RefreshTokens
                    .Include(r => r.User)
                    .FirstOrDefaultAsync(r => r.TokenHash == hash);
                if (fresh == null) return null;
                return await ReissueInFamilyAsync(fresh, DateTime.UtcNow);
            }

            var owner = record.User ?? await _context.Users.FirstAsync(u => u.Id == record.UserId);

            _context.RefreshTokens.Add(new RefreshToken
            {
                UserId = record.UserId,
                TokenHash = newHash,
                FamilyId = record.FamilyId,
                CreatedAt = now,
                ExpiresAt = now.AddDays(LifetimeDaysFor(owner.Role)),
            });
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return (owner, newRaw);
        }

        public async Task RevokeAsync(string token, string reason = "Logout")
        {
            if (string.IsNullOrWhiteSpace(token)) return;
            var hash = Hash(token);
            var record = await _context.RefreshTokens
                .FirstOrDefaultAsync(r => r.TokenHash == hash);
            if (record == null || record.RevokedAt.HasValue) return;

            record.RevokedAt = DateTime.UtcNow;
            record.RevokedReason = reason;
            await _context.SaveChangesAsync();
        }

        public async Task RevokeAllForUserAsync(int userId, string reason = "RevokeAll")
        {
            var now = DateTime.UtcNow;
            await _context.RefreshTokens
                .Where(r => r.UserId == userId && r.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.RevokedAt, now)
                    .SetProperty(r => r.RevokedReason, reason));
        }

        public async Task RevokeFamilyAsync(string familyId, string reason = "RevokeFamily")
        {
            if (string.IsNullOrWhiteSpace(familyId)) return;
            var now = DateTime.UtcNow;
            await _context.RefreshTokens
                .Where(r => r.FamilyId == familyId && r.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.RevokedAt, now)
                    .SetProperty(r => r.RevokedReason, reason));
        }

        // ----- Interne -----

        private TimeSpan GraceWindow =>
            TimeSpan.FromSeconds(Math.Max(0, _settings.RefreshRotationGraceSeconds));

        private static bool IsRotation(string? reason) =>
            reason is "Rotated" or "GraceReissue";

        /// <summary>
        /// Réémet un jeton dans une famille dont la dernière rotation n'a pas
        /// abouti côté client. Le successeur orphelin — que le client n'a jamais
        /// reçu — est révoqué : la famille garde exactement un jeton vivant.
        /// </summary>
        private async Task<(User user, string newToken)?> ReissueInFamilyAsync(
            RefreshToken record, DateTime now)
        {
            var owner = record.User ?? await _context.Users.FirstAsync(u => u.Id == record.UserId);
            var newRaw = GenerateRawToken();
            var newHash = Hash(newRaw);

            using var tx = await _context.Database.BeginTransactionAsync();

            // Le successeur né de la rotation perdue n'a jamais atteint le
            // client : il ne doit pas rester vivant à côté du jeton réémis.
            await _context.RefreshTokens
                .Where(r => r.FamilyId == record.FamilyId && r.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.RevokedAt, now)
                    .SetProperty(r => r.RevokedReason, "GraceSuperseded"));

            await _context.RefreshTokens
                .Where(r => r.Id == record.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.ReplacedByTokenHash, newHash)
                    .SetProperty(r => r.RevokedReason, "GraceReissue")
                    .SetProperty(r => r.LastUsedAt, now));

            _context.RefreshTokens.Add(new RefreshToken
            {
                UserId = record.UserId,
                TokenHash = newHash,
                FamilyId = record.FamilyId,
                CreatedAt = now,
                ExpiresAt = now.AddDays(LifetimeDaysFor(owner.Role)),
            });
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "[refresh] Réémission dans la fenêtre de grâce (famille {Family}, user {UserId})",
                record.FamilyId, record.UserId);

            return (owner, newRaw);
        }

        private int LifetimeDaysFor(string? role) =>
            role == UserRoles.SuperAdmin
                ? _settings.PrivilegedRefreshTokenExpirationDays
                : _settings.RefreshTokenExpirationDays;

        /// <summary>Génère un token cryptographiquement fort (256 bits, base64url).</summary>
        private static string GenerateRawToken()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static string NewFamilyId() => Guid.NewGuid().ToString("N");

        private static string Hash(string raw)
        {
            var bytes = Encoding.UTF8.GetBytes(raw);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }
    }
}
