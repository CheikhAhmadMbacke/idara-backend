using Idara.API.Models;

namespace Idara.API.Services
{
    public interface IRefreshTokenService
    {
        /// <summary>
        /// Crée un nouveau refresh token pour l'utilisateur, le persiste (hashé)
        /// et retourne le token brut (à envoyer une seule fois au client).
        /// </summary>
        Task<string> CreateAsync(int userId);

        /// <summary>
        /// Valide un refresh token, le révoque, et en crée un nouveau (rotation).
        /// Retourne (utilisateur, nouveau token) ou null si le token est invalide
        /// (inconnu, expiré ou révoqué).
        /// </summary>
        Task<(User user, string newToken)?> RotateAsync(string oldToken);

        /// <summary>Révoque un refresh token (logout). No-op si déjà révoqué.</summary>
        Task RevokeAsync(string token, string reason = "Logout");

        /// <summary>Révoque tous les refresh tokens actifs d'un utilisateur (logout from all devices).</summary>
        Task RevokeAllForUserAsync(int userId, string reason = "RevokeAll");

        /// <summary>
        /// Révoque la CHAÎNE de rotation (une connexion, sur un appareil). C'est
        /// la sanction d'un rejeu : elle ferme l'appareil concerné sans toucher
        /// aux autres sessions du compte. §223
        /// </summary>
        Task RevokeFamilyAsync(string familyId, string reason = "RevokeFamily");
    }
}
