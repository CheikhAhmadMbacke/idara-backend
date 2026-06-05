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
            await SeedPaymentFoundationsAsync();
            await SeedPlatformSettingsAsync();
        }

        /// <summary>
        /// Crée la ligne singleton de PlatformSettings (réglages globaux : mins
        /// + frais %) avec les valeurs par défaut si elle n'existe pas encore.
        /// </summary>
        private async Task SeedPlatformSettingsAsync()
        {
            var exists = await _context.PlatformSettings
                .AnyAsync(p => p.Id == Models.PlatformSettings.SingletonId);
            if (exists) return;

            _context.PlatformSettings.Add(new Models.PlatformSettings
            {
                Id = Models.PlatformSettings.SingletonId
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("PlatformSettings : ligne singleton créée avec valeurs par défaut.");
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

        /// <summary>
        /// Crée pour chaque école existante (a) un SchoolPaymentSettings avec
        /// valeurs par défaut, (b) un SchoolWallet à zéro. Idempotent : ne touche
        /// que les écoles qui n'en ont pas encore. Appelé à chaque démarrage —
        /// rattrape les écoles créées avant ou pendant le déploiement de la
        /// Phase 1.2.
        /// </summary>
        private async Task SeedPaymentFoundationsAsync()
        {
            var schoolIds = await _context.Schools.Select(s => s.Id).ToListAsync();
            if (schoolIds.Count == 0) return;

            var existingSettingsIds = await _context.SchoolPaymentSettings
                .Select(s => s.SchoolId)
                .ToListAsync();
            var existingWalletIds = await _context.SchoolWallets
                .Select(w => w.SchoolId)
                .ToListAsync();

            var now = DateTime.UtcNow;

            var missingSettings = schoolIds.Except(existingSettingsIds)
                .Select(id => new SchoolPaymentSettings
                {
                    SchoolId = id,
                    BillingMode = BillingMode.FixedAmount,
                    FeesPayer = FeesPayer.Parent,
                    MonthlyDueDay = 5,
                    BillingPeriod = BillingPeriod.Monthly,
                    CreatedAt = now
                })
                .ToList();

            var missingWallets = schoolIds.Except(existingWalletIds)
                .Select(id => new SchoolWallet
                {
                    SchoolId = id,
                    AvailableBalance = 0,
                    PendingBalance = 0,
                    TotalCreditedLifetime = 0,
                    TotalWithdrawnLifetime = 0,
                    CreatedAt = now,
                    UpdatedAt = now
                })
                .ToList();

            if (missingSettings.Count > 0) _context.SchoolPaymentSettings.AddRange(missingSettings);
            if (missingWallets.Count > 0) _context.SchoolWallets.AddRange(missingWallets);

            if (missingSettings.Count > 0 || missingWallets.Count > 0)
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation(
                    "Seed paiement : {Settings} SchoolPaymentSettings et {Wallets} SchoolWallet créés.",
                    missingSettings.Count, missingWallets.Count);
            }
        }
    }
}
