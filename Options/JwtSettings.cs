namespace Idara.API.Options
{
    public class JwtSettings
    {
        public const string SectionName = "Jwt";

        public string Key { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;

        /// <summary>
        /// Durée de vie de l'access token JWT. Doit rester courte (15 min par
        /// défaut) — la session longue est portée par le refresh token.
        /// </summary>
        public int AccessTokenExpirationMinutes { get; set; } = 15;

        /// <summary>Durée de vie du refresh token (par défaut 30 jours).</summary>
        public int RefreshTokenExpirationDays { get; set; } = 30;

        /// <summary>
        /// Compat ascendante : ancienne clé. Si présent, prend le pas sur
        /// AccessTokenExpirationMinutes (converti). Permet de migrer sans casser
        /// la config existante.
        /// </summary>
        public int? ExpirationDays { get; set; }
    }
}
