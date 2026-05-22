namespace Idara.API.Models
{
    /// <summary>
    /// Stockage d'un refresh token. On stocke le HASH du token (jamais la valeur
    /// brute) pour que même un dump DB ne permette pas de l'utiliser.
    /// La rotation est tracée via <see cref="ReplacedByTokenHash"/>.
    /// </summary>
    public class RefreshToken
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        /// <summary>SHA-256 hex du token brut.</summary>
        public string TokenHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }

        public DateTime? RevokedAt { get; set; }
        public string? RevokedReason { get; set; }

        /// <summary>Hash du token qui a remplacé celui-ci (rotation).</summary>
        public string? ReplacedByTokenHash { get; set; }

        public bool IsActive => RevokedAt == null && DateTime.UtcNow < ExpiresAt;
    }
}
