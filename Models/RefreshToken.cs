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

        /// <summary>
        /// Chaîne de rotation à laquelle ce jeton appartient — en pratique : UNE
        /// CONNEXION, sur UN appareil. Créée au login, héritée à chaque rotation.
        ///
        /// <para><b>Pourquoi elle existe.</b> Un rejeu détecté doit brûler la
        /// chaîne compromise, et elle seule. Avant le 2026-09-06 il révoquait
        /// TOUS les jetons du compte : le téléphone d'un parent, sa tablette et
        /// sa session web tombaient ensemble, sur un simple jeton dupliqué (une
        /// restauration de sauvegarde suffit). Avec la rotation devenue horaire,
        /// c'était une déconnexion de masse programmée. §223</para>
        /// </summary>
        public string FamilyId { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }

        /// <summary>Dernière présentation de ce jeton (rotation). Sert au support :
        /// « depuis quand ce compte n'a-t-il plus ouvert l'application ? »</summary>
        public DateTime? LastUsedAt { get; set; }

        public DateTime? RevokedAt { get; set; }
        public string? RevokedReason { get; set; }

        /// <summary>Hash du token qui a remplacé celui-ci (rotation).</summary>
        public string? ReplacedByTokenHash { get; set; }

        public bool IsActive => RevokedAt == null && DateTime.UtcNow < ExpiresAt;
    }
}
