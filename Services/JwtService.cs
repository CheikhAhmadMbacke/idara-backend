using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Idara.API.Models;
using Idara.API.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    public class JwtService : IJwtService
    {
        private readonly JwtSettings _settings;

        public JwtService(IOptions<JwtSettings> settings)
        {
            _settings = settings.Value;
        }

        public string GenerateToken(User user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Email, user.Email ?? string.Empty),
                new(ClaimTypes.Role, user.Role)
            };
            if (user.SchoolId.HasValue)
                claims.Add(new Claim("SchoolId", user.SchoolId.Value.ToString()));

            // Compat : si l'ancienne clé ExpirationDays est définie, on l'utilise
            // (en jours). Sinon on prend la nouvelle clé en minutes.
            var expires = _settings.ExpirationDays.HasValue
                ? DateTime.UtcNow.AddDays(_settings.ExpirationDays.Value)
                : DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpirationMinutes);

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                expires: expires,
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
