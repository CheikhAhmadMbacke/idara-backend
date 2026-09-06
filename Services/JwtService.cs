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
                // Le rôle réel reste le 1er claim → User.GetRole() le renvoie.
                new(ClaimTypes.Role, user.Role)
            };

            // Observateur (lecture seule) : on lui donne EN PLUS les rôles de
            // lecture du directeur (SchoolAdmin + SchoolStaff) pour qu'il passe
            // tous les [Authorize(Roles=...)] existants SANS toucher aux ~88
            // attributs. Les écritures sont bloquées séparément par
            // ReadOnlyRoleMiddleware. Ce dernier s'appuie sur le claim DÉDIÉ
            // "readonly" (et non sur l'ordre des claims de rôle, qui n'est pas
            // contractuellement garanti) → détection fail-CLOSED et robuste à
            // tout réordonnancement futur des claims.
            if (user.Role == Constants.UserRoles.SchoolViewer)
            {
                claims.Add(new Claim("readonly", "true"));
                claims.Add(new Claim(ClaimTypes.Role, Constants.UserRoles.SchoolAdmin));
                claims.Add(new Claim(ClaimTypes.Role, Constants.UserRoles.SchoolStaff));
            }

            if (user.SchoolId.HasValue)
                claims.Add(new Claim("SchoolId", user.SchoolId.Value.ToString()));

            // Durée COURTE, sans aucune exception. La session longue est portée
            // par le refresh token, qui glisse à chaque rotation. Un access token
            // long paraissait confortable ; il rendait en réalité la rotation
            // impossible (le refresh expirait le même jour) et retardait d'autant
            // la prise d'effet d'une suspension de compte. §223
            var expires = DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpirationMinutes);

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
