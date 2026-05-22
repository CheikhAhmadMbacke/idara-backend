using System.Security.Cryptography;
using System.Text;
using Idara.API.Data;
using Idara.API.Models;
using Idara.API.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    public class RefreshTokenService : IRefreshTokenService
    {
        private readonly AppDbContext _context;
        private readonly JwtSettings _settings;

        public RefreshTokenService(AppDbContext context, IOptions<JwtSettings> settings)
        {
            _context = context;
            _settings = settings.Value;
        }

        // ----- Public API -----

        public async Task<string> CreateAsync(int userId)
        {
            var raw = GenerateRawToken();
            var hash = Hash(raw);

            _context.RefreshTokens.Add(new RefreshToken
            {
                UserId = userId,
                TokenHash = hash,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(_settings.RefreshTokenExpirationDays),
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

            // Token inconnu (jamais émis OU déjà supprimé).
            if (record == null) return null;

            // Token déjà révoqué : possible attaque par replay → révoquer
            // toute la chaîne de l'utilisateur (best practice OWASP).
            if (!record.IsActive)
            {
                if (record.RevokedAt.HasValue)
                {
                    await RevokeAllForUserAsync(record.UserId, reason: "Replay-detected");
                }
                return null;
            }

            using var tx = await _context.Database.BeginTransactionAsync();

            // Génère le nouveau, lie et révoque l'ancien (rotation atomique).
            var newRaw = GenerateRawToken();
            var newHash = Hash(newRaw);

            var newRecord = new RefreshToken
            {
                UserId = record.UserId,
                TokenHash = newHash,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(_settings.RefreshTokenExpirationDays),
            };
            _context.RefreshTokens.Add(newRecord);

            record.RevokedAt = DateTime.UtcNow;
            record.RevokedReason = "Rotated";
            record.ReplacedByTokenHash = newHash;

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return (record.User, newRaw);
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
            var actives = await _context.RefreshTokens
                .Where(r => r.UserId == userId && r.RevokedAt == null)
                .ToListAsync();
            if (actives.Count == 0) return;

            foreach (var t in actives)
            {
                t.RevokedAt = DateTime.UtcNow;
                t.RevokedReason = reason;
            }
            await _context.SaveChangesAsync();
        }

        // ----- Helpers -----

        /// <summary>Génère un token cryptographiquement fort (256 bits, base64url).</summary>
        private static string GenerateRawToken()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static string Hash(string raw)
        {
            var bytes = Encoding.UTF8.GetBytes(raw);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }
    }
}
