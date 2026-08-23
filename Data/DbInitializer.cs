using Idara.API.Common.Extensions;
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
        private readonly ObservabilitySettings _observability;
        private readonly ILogger<DbInitializer> _logger;

        public DbInitializer(
            AppDbContext context,
            IOptions<SuperAdminSettings> settings,
            IOptions<ObservabilitySettings> observability,
            ILogger<DbInitializer> logger)
        {
            _context = context;
            _settings = settings.Value;
            _observability = observability.Value;
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
            await RetypeQuranSubjectsAsync();
            await SeedDemoSchoolAsync();
            await SeedDemoTeacherAsync();
            await TrimDemoTeacherAssignmentsAsync();
            await PurgeStaleIdempotencyRecordsAsync();
            await PurgeStaleIncidentsAsync();
        }

        /// <summary>
        /// 📖 Remet sur le bon type les matières de Coran créées en « Matière
        /// générale ».
        ///
        /// <para><b>Le défaut réparé.</b> Le type d'une matière commande la forme
        /// de la fiche du Cahier de suivi (relevé structuré pour le Coran, texte
        /// libre sinon), mais le formulaire n'en disait rien et proposait
        /// « Matière générale » par défaut. Un daara pilote a donc créé
        /// « Al quran » en matière générale : ses enseignants tombaient sur la
        /// saisie en texte libre au lieu de leur fiche « nouvelle leçon /
        /// révision récente / ancienne révision ».</para>
        ///
        /// <para><b>Bornée et prouvable</b> (§178). On ne touche qu'aux matières
        /// dont le nom EST un nom du Coran — égalité sur la forme normalisée,
        /// jamais « contient » : « Histoire du Coran » et « Éducation coranique »
        /// restent des matières classiques. La règle vit dans
        /// <see cref="QuranSubjectNaming"/>, publique et pure, donc vérifiable
        /// sans base.</para>
        ///
        /// <para>🔴 <b>Jouée UNE SEULE FOIS</b>, marqueur en base à l'appui :
        /// c'est une reprise de données, pas une règle permanente. Rejouée à
        /// chaque démarrage, elle écraserait le choix d'une école qui repasse
        /// volontairement sa matière sur un autre type — elle perdrait toujours,
        /// sans jamais comprendre pourquoi (discipline du §74, transposée : la
        /// normalisation étant en C#, la conversion ne pouvait pas vivre dans le
        /// <c>Up()</c> d'une migration).</para>
        /// </summary>
        /// <remarks>Publique à dessein : une reprise de données qu'on ne peut
        /// vérifier qu'en production ne se vérifie jamais (§133/§178).</remarks>
        public async Task RetypeQuranSubjectsAsync()
        {
            try
            {
                // 🔴 UNE SEULE FOIS, jamais à chaque démarrage. Sans ce marqueur,
                // une école qui repasse volontairement sa matière sur un autre
                // type la verrait rebasculer au déploiement suivant : elle
                // perdrait toujours, sans comprendre pourquoi.
                var platform = await _context.GetPlatformSettingsAsync();
                if (platform.QuranSubjectsRetypedAt != null) return;

                // Volume minuscule (quelques dizaines de matières par école) :
                // la détection se fait en mémoire, la normalisation Unicode
                // n'étant pas traduisible en SQL.
                var candidates = await _context.Subjects
                    .Where(s => !s.IsDeleted && s.Kind != SubjectKind.Coran)
                    .ToListAsync();

                var fixedSubjects = candidates
                    .Where(s => QuranSubjectNaming.LooksLikeQuran(s.Name, s.NameAr))
                    .ToList();

                // Le marqueur est posé même sans rien à corriger : la reprise a
                // bel et bien eu lieu, et une base neuve n'a rien à rattraper.
                platform.QuranSubjectsRetypedAt = DateTime.UtcNow;

                foreach (var s in fixedSubjects)
                {
                    _logger.LogInformation(
                        "[subjects] Matière {Id} « {Name} » (école {SchoolId}) : {From} → Coran.",
                        s.Id, s.Name, s.SchoolId, s.Kind);
                    s.Kind = SubjectKind.Coran;
                }
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "[subjects] Reprise jouée : {Count} matière(s) de Coran remise(s) sur le bon type.",
                    fixedSubjects.Count);
            }
            catch (Exception ex)
            {
                // Un défaut de reprise ne doit jamais empêcher l'API de démarrer.
                _logger.LogWarning(ex, "[subjects] Reprise du type des matières de Coran impossible.");
            }
        }

        /// <summary>
        /// Purge les incidents de plus de 30 jours (durée annoncée dans la
        /// politique de confidentialité). C'est de la donnée de diagnostic : passé
        /// un mois, la version de l'application a changé et la trace ne sert plus.
        ///
        /// <para>Au démarrage plutôt que par un cron dédié, comme pour les traces
        /// d'idempotence : le volume est minuscule et l'API redémarre à chaque
        /// déploiement.</para>
        /// </summary>
        private async Task PurgeStaleIncidentsAsync()
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-_observability.IncidentRetentionDays);
                var removed = await _context.ClientIncidents
                    .Where(i => i.CreatedAt < cutoff)
                    .ExecuteDeleteAsync();
                if (removed > 0)
                {
                    _logger.LogInformation(
                        "[observability] {Count} incident(s) de plus de {Days} jours purgé(s).",
                        removed, _observability.IncidentRetentionDays);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[observability] Purge des incidents impossible.");
            }
        }

        /// <summary>
        /// Purge les traces d'idempotence anciennes.
        ///
        /// Une saisie mise en file d'attente sur un téléphone est rejouée dans
        /// les heures qui suivent, jamais des semaines plus tard : au-delà de
        /// 30 jours, la trace ne protège plus rien et ne fait que grossir. On le
        /// fait au démarrage plutôt que via un nouveau cron — le volume est
        /// minuscule et l'API redémarre à chaque déploiement.
        /// </summary>
        private async Task PurgeStaleIdempotencyRecordsAsync()
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-30);
                var removed = await _context.IdempotencyRecords
                    .Where(r => r.CreatedAt < cutoff)
                    .ExecuteDeleteAsync();
                if (removed > 0)
                {
                    _logger.LogInformation(
                        "[idempotency] {Count} traces de plus de 30 jours purgées.", removed);
                }
            }
            catch (Exception ex)
            {
                // Best-effort : un ménage raté ne doit jamais empêcher l'API de démarrer.
                _logger.LogWarning(ex, "[idempotency] Purge impossible.");
            }
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
        private const string DemoTeacherPhone = DemoAccounts.TeacherPhone;
        private const string DemoTeacherCode = DemoAccounts.TeacherCode;

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
                PaymentDeadlineDay = 15,
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
        /// Enseignant de démonstration, ajouté à l'école de démo.
        ///
        /// <para>⚠️ Méthode SÉPARÉE de <see cref="SeedDemoSchoolAsync"/>, et c'est
        /// tout l'intérêt : cette dernière s'arrête net si le SchoolAdmin de démo
        /// existe déjà — donc en production, où l'école a été créée en juin, un
        /// ajout écrit à l'intérieur ne serait JAMAIS joué. Ici la garde porte sur
        /// le compte enseignant lui-même : le seed rattrape une école existante
        /// comme il équipe une base neuve.</para>
        ///
        /// <para>L'enseignant est affecté à <b>toutes</b> les classes de l'école
        /// de démo. Sans affectation il ne verrait rien du tout — le cloisonnement
        /// (§150) borne un enseignant à ses classes, et un compte sans classe
        /// n'affiche qu'un « aucune classe ne vous est affectée » parfaitement
        /// correct mais intestable.</para>
        /// </summary>
        /// <remarks>
        /// <b>Publique à dessein</b> : un seed qu'on ne peut vérifier qu'en
        /// démarrant l'API contre une vraie base est un seed qui n'est jamais
        /// vérifié (même raisonnement qu'au §133 pour le rendu d'un e-mail).
        /// Elle est exercée par un banc EF InMemory sur les deux cas qui
        /// comptent : base neuve et école de démo DÉJÀ existante.
        /// </remarks>
        public async Task SeedDemoTeacherAsync()
        {
            var admin = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == DemoSchoolAdminEmail && !u.IsDeleted);
            if (admin?.SchoolId == null) return; // pas d'école de démo : rien à faire

            var schoolId = admin.SchoolId.Value;

            // Garde d'idempotence : le numéro est l'identité d'un compte école.
            var already = await _context.Users
                .AnyAsync(u => u.PhoneNumber == DemoTeacherPhone && !u.IsDeleted);
            if (already) return;

            var now = DateTime.UtcNow;

            var teacher = new User
            {
                FullName = "Enseignant Démo",
                FirstName = "Enseignant",
                LastName = "Démo",
                PhoneNumber = DemoTeacherPhone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(DemoTeacherCode),
                Role = UserRoles.Teacher,
                IsEmailVerified = false,
                AccountStatus = AccountStatus.Active,
                SchoolId = schoolId,
                PreferredLanguage = "fr",
                CreatedAt = now
            };
            _context.Users.Add(teacher);
            await _context.SaveChangesAsync();

            // Une matière est indispensable : une affectation, c'est un triplet
            // (classe, matière, enseignant). On réutilise celle de l'école si
            // elle en a déjà une, sinon on crée « Coran ».
            var subject = await _context.Subjects
                .Where(s => s.SchoolId == schoolId && !s.IsDeleted && s.IsActive)
                .OrderBy(s => s.Id)
                .FirstOrDefaultAsync();

            if (subject == null)
            {
                subject = new Subject
                {
                    SchoolId = schoolId,
                    Name = "Coran",
                    NameAr = "القرآن",
                    Kind = SubjectKind.Coran,
                    DefaultCoefficient = 2.0,
                    DefaultMaxValue = 20.0,
                    OrderIndex = 0,
                    IsActive = true,
                    CreatedAt = now
                };
                _context.Subjects.Add(subject);
                await _context.SaveChangesAsync();
            }

            // ⚠️ UNE SEULE classe, et c'est tout le sujet.
            //
            // Le seed l'affectait d'abord à TOUTES les classes du daara de démo.
            // Résultat au premier test réel : un sélecteur de classe apparaissait
            // dans son espace, avec une entrée « Tous » — ce qui donne l'exacte
            // impression d'un cloisonnement absent, alors que le serveur ne lui
            // renvoyait bien que SES classes (vérifié : 30 contrôles, 0 fuite).
            //
            // Un enseignant de daara tient en général une halaqa. Avec une seule
            // classe, le sélecteur disparaît de lui-même (`ClassScope.showPicker`)
            // et le compte de démonstration montre le comportement réel — celui
            // qu'on veut pouvoir constater d'un coup d'œil.
            var classId = await _context.Classes
                .Where(c => c.SchoolId == schoolId && !c.IsDeleted)
                .OrderBy(c => c.Id)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync();

            if (classId != null)
            {
                _context.ClassSubjectTeachers.Add(new ClassSubjectTeacher
                {
                    SchoolId = schoolId,
                    ClassId = classId.Value,
                    SubjectId = subject.Id,
                    TeacherId = teacher.Id,
                    AssignedAt = now
                });
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation(
                "Seed démo : enseignant {Phone} ajouté à l'école {SchoolId}, affecté à la classe {ClassId} pour la matière « {Subject} ».",
                DemoTeacherPhone, schoolId, classId, subject.Name);
        }

        /// <summary>
        /// Ramène l'enseignant de démonstration à UNE seule classe.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Reprise ponctuelle : le premier seed l'avait affecté à toutes les
        /// classes du daara de démo, et son compte existe déjà en production —
        /// la garde d'idempotence de <see cref="SeedDemoTeacherAsync"/> ne le
        /// touchera donc plus jamais.
        /// </para>
        /// <para>
        /// ⚠️ Bornée au **seul** compte de démonstration, reconnu par son
        /// numéro : aucune école réelle ne peut être atteinte par cette reprise.
        /// Et elle est réversible d'un geste depuis l'écran des affectations.
        /// </para>
        /// </remarks>
        /// <remarks>
        /// <b>Publique à dessein</b> : cette méthode MODIFIE des données
        /// existantes en production. C'est précisément ce qui doit être prouvé
        /// avant d'être lâché — qu'elle ramène bien le compte de démo à une
        /// classe, et surtout qu'elle ne touche AUCUN enseignant réel.
        /// </remarks>
        public async Task TrimDemoTeacherAssignmentsAsync()
        {
            var teacherId = await _context.Users
                .Where(u => u.PhoneNumber == DemoTeacherPhone && !u.IsDeleted)
                .Select(u => (int?)u.Id)
                .FirstOrDefaultAsync();
            if (teacherId == null) return;

            var assignments = await _context.ClassSubjectTeachers
                .Where(a => a.TeacherId == teacherId.Value)
                .OrderBy(a => a.Id)
                .ToListAsync();
            if (assignments.Count <= 1) return;

            _context.ClassSubjectTeachers.RemoveRange(assignments.Skip(1));
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Seed démo : enseignant {Phone} ramené à 1 classe ({Removed} affectation(s) retirée(s)).",
                DemoTeacherPhone, assignments.Count - 1);
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
                    PaymentDeadlineDay = 15,
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
