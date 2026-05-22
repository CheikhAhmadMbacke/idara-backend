using Idara.API.Constants;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Data
{
    public class DbInitializer
    {
        private readonly AppDbContext _context;
        private readonly SuperAdminSettings _settings;
        private readonly ILogger<DbInitializer> _logger;

        public DbInitializer(AppDbContext context, IOptions<SuperAdminSettings> settings, ILogger<DbInitializer> logger)
        {
            _context = context;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            // MigrateAsync applique toutes les migrations EF Core en attente.
            // Si la DB n'existe pas, elle est creee. Si elle existe, seules les migrations
            // non encore appliquees sont jouees (idempotent).
            await _context.Database.MigrateAsync();
            await SeedSuperAdminAsync();
        }

        private async Task SeedSuperAdminAsync()
        {
            if (string.IsNullOrWhiteSpace(_settings.Email) || string.IsNullOrWhiteSpace(_settings.Password))
            {
                _logger.LogWarning("SuperAdmin non configuré (section 'SuperAdmin' manquante). Aucun seed exécuté.");
                return;
            }

            var existing = await _context.Users.FirstOrDefaultAsync(u => u.Email == _settings.Email);
            if (existing != null) return;

            var superAdmin = new User
            {
                Email = _settings.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(_settings.Password),
                Role = UserRoles.SuperAdmin,
                IsEmailVerified = true,
                AccountStatus = AccountStatus.Active,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(superAdmin);
            await _context.SaveChangesAsync();
            _logger.LogInformation("SuperAdmin créé : {Email}", _settings.Email);
        }
    }
}
