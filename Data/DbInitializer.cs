using Idara.API.Common.Utilities;
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
            await SeedSubscriptionPlansAsync();
            await SeedSubscriptionsAsync();
            await NormalizeUserPhonesAsync();
            await SeedDemoSchoolAsync();
        }

        // Identifiants du compte de DÉMONSTRATION fournis à Google Play (App access).
        // Le reviewer s'en sert pour ouvrir l'app ; sans login fonctionnel = rejet.
        // Ne JAMAIS supprimer ces comptes tant que l'app est publiée (re-review à
        // chaque mise à jour). Données 100 % fictives.
        // Source unique des identifiants démo : Constants/DemoAccounts.cs
        // (partagé avec le back-office SuperAdmin qui les affiche).
        private const string DemoSchoolAdminEmail = DemoAccounts.SchoolAdminEmail;
        private const string DemoSchoolAdminPassword = DemoAccounts.SchoolAdminPassword;
        private const string DemoGuardianPhone = DemoAccounts.GuardianPhone;
        private const string DemoGuardianCode = DemoAccounts.GuardianCode;

        /// <summary>
        /// Seed d'une école de démonstration complète (école validée + SchoolAdmin
        /// + classes + élèves fictifs + un parent lié) destinée au reviewer Google
        /// Play (section « App access », login obligatoire). Idempotent : ne fait
        /// rien si le compte admin de démo existe déjà. Autonome : crée aussi
        /// settings/wallet/abonnement pour ne dépendre d'aucun ordre de seed, et
        /// l'abonnement est posé en Active avec échéance lointaine pour que la démo
        /// ne soit jamais dégradée (Trial→ReadOnly) même des mois plus tard.
        /// </summary>
        private async Task SeedDemoSchoolAsync()
        {
            var exists = await _context.Users.AnyAsync(u => u.Email == DemoSchoolAdminEmail);
            if (exists) return;

            var now = DateTime.UtcNow;

            // 1) École validée
            var school = new School
            {
                Name = "École de démonstration",
                Address = "Dakar, Sénégal",
                PhoneNumber = "+221770000000",
                KycStatus = KycStatus.Validated,
                RepresentativeFirstName = "Directeur",
                RepresentativeLastName = "Démo",
                RepresentativePhone = "+221770000000",
                SubmittedAt = now,
                ValidatedAt = now,
                CreatedAt = now
            };
            _context.Schools.Add(school);
            await _context.SaveChangesAsync();

            // 2) SchoolAdmin actif (login email + mot de passe)
            _context.Users.Add(new User
            {
                Email = DemoSchoolAdminEmail,
                FullName = "Directeur Démo",
                FirstName = "Directeur",
                LastName = "Démo",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(DemoSchoolAdminPassword),
                Role = UserRoles.SchoolAdmin,
                IsEmailVerified = true,
                AccountStatus = AccountStatus.Active,
                SchoolId = school.Id,
                PreferredLanguage = "fr",
                CreatedAt = now
            });

            // Fondations paiement + abonnement Active à échéance lointaine (jamais dégradé)
            _context.SchoolPaymentSettings.Add(new SchoolPaymentSettings
            {
                SchoolId = school.Id,
                BillingMode = BillingMode.FixedAmount,
                FeesPayer = FeesPayer.Parent,
                MonthlyDueDay = 5,
                BillingPeriod = BillingPeriod.Monthly,
                GeneralMonthlyFeeFcfa = 5000,
                CreatedAt = now
            });
            _context.SchoolWallets.Add(new SchoolWallet
            {
                SchoolId = school.Id,
                AvailableBalance = 0,
                PendingBalance = 0,
                TotalCreditedLifetime = 0,
                TotalWithdrawnLifetime = 0,
                CreatedAt = now,
                UpdatedAt = now
            });

            var daara = await _context.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.Code == "daara" && !p.IsCustom);
            _context.Subscriptions.Add(new Subscription
            {
                SchoolId = school.Id,
                PlanId = daara?.Id,
                BillingCycle = BillingCycle.Monthly,
                Status = SubscriptionStatus.Active,
                AmountFcfa = daara?.MonthlyPriceFcfa ?? 5000,
                NotificationQuota = daara?.NotificationQuota ?? 150,
                NotificationUsedThisCycle = 0,
                TrialEndsAt = now,
                NextBillingAt = now.AddYears(50),
                CreatedAt = now,
                UpdatedAt = now
            });
            await _context.SaveChangesAsync();

            // 3) Deux classes
            var classA = new Class { Name = "Halaqa A", Level = "Débutant", Capacity = 30, SchoolId = school.Id, CreatedAt = now };
            var classB = new Class { Name = "Halaqa B", Level = "Intermédiaire", Capacity = 30, SchoolId = school.Id, CreatedAt = now };
            _context.Classes.AddRange(classA, classB);
            await _context.SaveChangesAsync();

            // 4) Quelques élèves fictifs
            var dob = new DateTime(2015, 5, 10, 0, 0, 0, DateTimeKind.Utc);
            var students = new List<Student>
            {
                new() { FirstName = "Aïcha", LastName = "Ndiaye", Gender = Gender.Female, DateOfBirth = dob, ClassId = classA.Id, SchoolId = school.Id, EnrollmentDate = now, CreatedAt = now },
                new() { FirstName = "Moussa", LastName = "Diop",  Gender = Gender.Male,   DateOfBirth = dob, ClassId = classA.Id, SchoolId = school.Id, EnrollmentDate = now, CreatedAt = now },
                new() { FirstName = "Fatou",  LastName = "Fall",  Gender = Gender.Female, DateOfBirth = dob, ClassId = classB.Id, SchoolId = school.Id, EnrollmentDate = now, CreatedAt = now },
            };
            _context.Students.AddRange(students);
            await _context.SaveChangesAsync();

            foreach (var s in students)
                s.StudentNumber = $"E{school.Id:D3}-{s.Id:D5}";
            await _context.SaveChangesAsync();

            // 5) Parent de démo (login téléphone + code) lié à 2 élèves
            var guardian = new User
            {
                FullName = "Parent Démo",
                FirstName = "Parent",
                LastName = "Démo",
                PhoneNumber = DemoGuardianPhone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(DemoGuardianCode),
                Role = UserRoles.Guardian,
                IsEmailVerified = false,
                AccountStatus = AccountStatus.Active,
                PreferredLanguage = "fr",
                CreatedAt = now
            };
            _context.Users.Add(guardian);
            await _context.SaveChangesAsync();

            _context.StudentGuardians.AddRange(
                new StudentGuardian { StudentId = students[0].Id, GuardianId = guardian.Id, Relationship = "Père", IsPrimaryGuardian = true },
                new StudentGuardian { StudentId = students[1].Id, GuardianId = guardian.Id, Relationship = "Père", IsPrimaryGuardian = true });
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Seed démo : école '{School}' (id {Id}) + SchoolAdmin {Email} + parent {Phone} créés pour la review Play Store.",
                school.Name, school.Id, DemoSchoolAdminEmail, DemoGuardianPhone);
        }

        /// <summary>
        /// Seed des 4 plans d'abonnement publics (spec §2.2). Idempotent par
        /// Code : n'ajoute que les plans manquants, ne touche pas aux prix déjà
        /// édités par le SuperAdmin.
        /// </summary>
        private async Task SeedSubscriptionPlansAsync()
        {
            // (code, nom, élèves min, élèves max, mensuel, annuel, quota notif)
            var defaults = new (string Code, string Name, int Min, int? Max, long Monthly, long Annual, int Quota)[]
            {
                ("daara",    "Daara",    1,   30,   5000,  50000,  150),
                ("standard", "Standard", 31,  100,  12000, 120000, 500),
                ("pro",      "Pro",      101, 300,  25000, 250000, 1500),
                ("grand",    "Grand",    301, null, 50000, 500000, 3000),
            };

            var existingCodes = await _context.SubscriptionPlans
                .Where(p => !p.IsCustom)
                .Select(p => p.Code)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var toAdd = defaults
                .Where(d => !existingCodes.Contains(d.Code))
                .Select(d => new SubscriptionPlan
                {
                    Code = d.Code,
                    Name = d.Name,
                    StudentMin = d.Min,
                    StudentMax = d.Max,
                    MonthlyPriceFcfa = d.Monthly,
                    AnnualPriceFcfa = d.Annual,
                    NotificationQuota = d.Quota,
                    IsActive = true,
                    IsCustom = false,
                    CreatedAt = now
                })
                .ToList();

            if (toAdd.Count > 0)
            {
                _context.SubscriptionPlans.AddRange(toAdd);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Seed abonnement : {Count} plan(s) public(s) créé(s).", toAdd.Count);
            }
        }

        /// <summary>
        /// Backfill : crée un abonnement Trial 30j pour chaque école qui n'en a
        /// pas encore (écoles validées avant le déploiement de la Phase 4).
        /// Idempotent — n'invente rien pour les écoles déjà abonnées.
        /// </summary>
        private async Task SeedSubscriptionsAsync()
        {
            var schoolIds = await _context.Schools.Select(s => s.Id).ToListAsync();
            if (schoolIds.Count == 0) return;

            var subscribedIds = await _context.Subscriptions
                .Select(s => s.SchoolId)
                .ToListAsync();

            var missing = schoolIds.Except(subscribedIds).ToList();
            if (missing.Count == 0) return;

            var plan = await _context.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.Code == Common.Extensions.SubscriptionExtensions.DefaultPlanCode && !p.IsCustom);

            var now = DateTime.UtcNow;
            var trialEnd = now.AddDays(Common.Extensions.SubscriptionExtensions.TrialDays);

            var toAdd = missing.Select(id => new Subscription
            {
                SchoolId = id,
                PlanId = plan?.Id,
                BillingCycle = BillingCycle.Monthly,
                Status = SubscriptionStatus.Trial,
                AmountFcfa = plan?.MonthlyPriceFcfa ?? 5000,
                NotificationQuota = plan?.NotificationQuota ?? 150,
                NotificationUsedThisCycle = 0,
                TrialEndsAt = trialEnd,
                NextBillingAt = trialEnd,
                CreatedAt = now,
                UpdatedAt = now
            }).ToList();

            _context.Subscriptions.AddRange(toAdd);
            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Seed abonnement : {Count} abonnement(s) Trial 30j créé(s) (backfill).", toAdd.Count);
            }
            catch (DbUpdateException ex)
            {
                // Boot multi-instance simultané : une autre instance a déjà inséré
                // (unique violation sur SchoolId). Idempotent → on ignore.
                _logger.LogWarning(ex, "Seed abonnement : conflit concurrent ignoré (backfill déjà fait ailleurs).");
            }
        }

        /// <summary>
        /// Normalise les numéros de téléphone existants en E.164 (+221…) pour
        /// qu'ils soient envoyables par SMS. Idempotent : ne touche que les
        /// numéros qui ne commencent pas déjà par "+221" et que
        /// <see cref="SenegalPhone"/> sait convertir (les autres restent en
        /// l'état, à corriger manuellement par l'école). N'INVENTE aucun numéro.
        /// </summary>
        private async Task NormalizeUserPhonesAsync()
        {
            var users = await _context.Users
                .Where(u => u.PhoneNumber != null
                            && u.PhoneNumber != ""
                            && !u.PhoneNumber.StartsWith("+221"))
                .ToListAsync();
            if (users.Count == 0) return;

            int fixedCount = 0;
            foreach (var u in users)
            {
                var norm = SenegalPhone.Normalize(u.PhoneNumber);
                if (norm != null && norm != u.PhoneNumber)
                {
                    u.PhoneNumber = norm;
                    fixedCount++;
                }
            }

            if (fixedCount > 0)
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation(
                    "Normalisation téléphones : {Count} numéro(s) convertis en E.164 (+221).",
                    fixedCount);
            }
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
