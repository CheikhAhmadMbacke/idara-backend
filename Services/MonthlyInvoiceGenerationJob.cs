using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>
    /// Cron quotidien (02:00 UTC) qui génère les Invoices mensuelles pour
    /// chaque école configurée en <see cref="BillingMode.FixedAmount"/>, le
    /// jour où <c>today.Day == settings.MonthlyDueDay</c>.
    ///
    /// Idempotent : UNIQUE filtré <c>(StudentId, PeriodStart) WHERE Status &lt;&gt; Cancelled</c>
    /// en DB. Un crash du job en plein milieu, ou un redémarrage du conteneur
    /// le même jour, ne génère pas de doublons (l'INSERT du 2e passage tombe
    /// sur la violation d'unicité, on l'ignore silencieusement).
    ///
    /// Montant snapshoté à la génération :
    ///   StudentFeeOverride.AmountFcfa  >  ClassFee courant pour la classe.
    /// Si ni l'un ni l'autre n'est défini, l'élève est skippé avec un warning
    /// (l'admin doit configurer un tarif avant la prochaine échéance).
    ///
    /// 02:00 UTC = 02:00 GMT = 02:00 Dakar (Sénégal n'a pas de DST). Choix
    /// délibéré pour rester loin du créneau backup 03:00 UTC.
    /// </summary>
    public class MonthlyInvoiceGenerationJob : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MonthlyInvoiceGenerationJob> _logger;

        // 02:00 UTC quotidien.
        private static readonly TimeSpan RunAtUtc = new(hours: 2, minutes: 0, seconds: 0);

        public MonthlyInvoiceGenerationJob(
            IServiceScopeFactory scopeFactory,
            ILogger<MonthlyInvoiceGenerationJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "[invoice-cron] Démarré. Prochain tick prévu à {Next:yyyy-MM-dd HH:mm} UTC",
                NextFireUtc(DateTime.UtcNow));

            while (!stoppingToken.IsCancellationRequested)
            {
                var nextFire = NextFireUtc(DateTime.UtcNow);
                var delay = nextFire - DateTime.UtcNow;

                if (delay > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(delay, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Shutdown propre, on sort.
                        return;
                    }
                }

                try
                {
                    await RunOnceAsync(DateTime.UtcNow, stoppingToken);
                }
                catch (Exception ex)
                {
                    // Catch global obligatoire : un throw remonté ici tuerait
                    // le BackgroundService pour de bon (plus de tick demain).
                    _logger.LogError(ex, "[invoice-cron] Échec exécution du tick");
                }
            }
        }

        /// <summary>
        /// Logique métier extraite (pas <see langword="private"/>) pour pouvoir
        /// être déclenchée manuellement depuis un endpoint admin (à venir en
        /// Phase 1.5 si besoin de rejouer un jour raté).
        ///
        /// <para><paramref name="forceDay"/> : si fourni (1-28), simule un
        /// jour du mois différent du jour réel. Pratique pour tester sans
        /// devoir attendre la vraie date. Le mois et l'année restent ceux
        /// de <paramref name="nowUtc"/>.</para>
        /// </summary>
        public async Task<InvoiceGenerationReport> RunOnceAsync(
            DateTime nowUtc, CancellationToken ct, int? forceDay = null)
        {
            var today = nowUtc.Date;
            var dayOfMonth = forceDay ?? today.Day;
            var report = new InvoiceGenerationReport { RunAtUtc = nowUtc, DayOfMonth = dayOfMonth };

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notif = scope.ServiceProvider.GetRequiredService<INotificationService>();

            // Mode SMS (bilingue ou non) — lu une seule fois pour tout le batch.
            var platform = await db.GetPlatformSettingsAsync(ct);
            var bilingual = platform.SmsBilingual;
            var periodeLabel = FrenchMonthYear(new DateTime(today.Year, today.Month, 1));

            // 1) Écoles éligibles : FixedAmount + Monthly.
            //    - Run quotidien réel (forceDay == null) : AUTO-RATTRAPAGE →
            //      MonthlyDueDay <= jour courant. Le jour de l'échéance OU tout
            //      jour suivant du mois, on (re)tente la génération si elle n'a
            //      pas encore eu lieu. Couvre : changement du jour d'échéance en
            //      cours de mois (cas du frère), élève ajouté après coup, tick
            //      raté. Garde anti-recharge : en rattrapage, on saute tout élève
            //      ayant DÉJÀ une facture pour la période (même ANNULÉE) — cf.
            //      paramètre skipStudentsWithPeriodInvoice de GenerateForSchoolAsync.
            //    - forceDay (SuperAdmin) : jour EXACT (ciblage / rejeu), et la
            //      régénération d'une facture annulée reste possible (§67).
            var catchUp = !forceDay.HasValue;
            var settingsQuery = db.SchoolPaymentSettings
                .Where(s =>
                    s.BillingMode == BillingMode.FixedAmount &&
                    s.BillingPeriod == BillingPeriod.Monthly);
            settingsQuery = catchUp
                ? settingsQuery.Where(s => s.MonthlyDueDay <= dayOfMonth)
                : settingsQuery.Where(s => s.MonthlyDueDay == dayOfMonth);
            var eligibleSettings = await settingsQuery.ToListAsync(ct);

            if (eligibleSettings.Count == 0)
            {
                _logger.LogInformation(
                    "[invoice-cron] Tick {Date:yyyy-MM-dd}, aucune école avec MonthlyDueDay={Day}",
                    today, dayOfMonth);
                return report;
            }

            _logger.LogInformation(
                "[invoice-cron] Tick {Date:yyyy-MM-dd}, {Count} école(s) éligible(s) (MonthlyDueDay={Day})",
                today, eligibleSettings.Count, dayOfMonth);

            // Période = mois civil courant. Si on génère le 5 mai, PeriodStart
            // = 1er mai 00:00 UTC. C'est cette valeur qui joue l'idempotence
            // via l'UNIQUE (StudentId, PeriodStart).
            var periodStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var periodEnd = periodStart.AddMonths(1).AddDays(-1);
            var dueDate = new DateTime(today.Year, today.Month, today.Day, 0, 0, 0, DateTimeKind.Utc);

            foreach (var settings in eligibleSettings)
            {
                try
                {
                    var perSchool = await GenerateForSchoolAsync(
                        db, settings.SchoolId, settings,
                        periodStart, periodEnd, dueDate, today, notif, bilingual,
                        periodeLabel, skipStudentsWithPeriodInvoice: catchUp, ct);
                    report.SchoolsProcessed++;
                    report.InvoicesCreated += perSchool.Created;
                    report.InvoicesSkipped += perSchool.Skipped;
                    report.InvoicesAlreadyExisting += perSchool.AlreadyExisting;
                    report.StudentsWithoutFee += perSchool.WithoutFee;
                }
                catch (Exception ex)
                {
                    // On isole les écoles pour qu'une seule en erreur ne casse
                    // pas la génération des autres.
                    report.SchoolsFailed++;
                    _logger.LogError(ex,
                        "[invoice-cron] Échec génération pour SchoolId={SchoolId}",
                        settings.SchoolId);
                }
            }

            _logger.LogInformation(
                "[invoice-cron] Terminé. Écoles OK={Ok} ÉchecsÉcoles={Fail} Factures créées={Created} déjà existantes={Existing} skipped={Skipped} sansTarif={NoFee}",
                report.SchoolsProcessed, report.SchoolsFailed,
                report.InvoicesCreated, report.InvoicesAlreadyExisting,
                report.InvoicesSkipped, report.StudentsWithoutFee);

            return report;
        }

        /// <summary>
        /// Génération À LA DEMANDE pour UNE école, sur un mois donné,
        /// INDÉPENDAMMENT du <c>MonthlyDueDay</c> (contrairement au cron
        /// quotidien). Permet à une école de générer ses factures quand elle
        /// veut, sans attendre l'échéance NI le SuperAdmin. Idempotent (les
        /// élèves déjà facturés pour la période sont sautés via l'UNIQUE filtré).
        /// </summary>
        public async Task<InvoiceGenerationReport> GenerateForSchoolNowAsync(
            int schoolId, int year, int month, DateTime nowUtc, CancellationToken ct)
        {
            var report = new InvoiceGenerationReport { RunAtUtc = nowUtc, DayOfMonth = nowUtc.Day };

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notif = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var settings = await db.SchoolPaymentSettings
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);
            if (settings == null)
            {
                _logger.LogWarning(
                    "[invoice-ondemand] SchoolId={SchoolId} sans SchoolPaymentSettings — rien à générer", schoolId);
                return report;
            }
            if (settings.BillingMode != BillingMode.FixedAmount)
            {
                // Seul le mode FixedAmount produit des factures mensuelles.
                _logger.LogInformation(
                    "[invoice-ondemand] SchoolId={SchoolId} pas en FixedAmount — rien à générer", schoolId);
                return report;
            }

            var today = nowUtc.Date;
            var platform = await db.GetPlatformSettingsAsync(ct);
            var bilingual = platform.SmsBilingual;

            var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var periodEnd = periodStart.AddMonths(1).AddDays(-1);
            // Échéance = jour configuré, borné au dernier jour du mois, et jamais
            // AVANT aujourd'hui (une facture générée à la demande ne naît pas
            // « en retard » si l'échéance du mois est déjà passée).
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var dueDay = Math.Min(Math.Max(settings.MonthlyDueDay, 1), daysInMonth);
            var dueDate = new DateTime(year, month, dueDay, 0, 0, 0, DateTimeKind.Utc);
            if (dueDate < today) dueDate = today;
            var periodeLabel = FrenchMonthYear(periodStart);

            _logger.LogInformation(
                "[invoice-ondemand] SchoolId={SchoolId} génération à la demande pour {Period:yyyy-MM} (échéance {Due:yyyy-MM-dd})",
                schoolId, periodStart, dueDate);

            // À la demande : on n'impose PAS le saut des élèves déjà facturés
            // (skip=false) → l'unicité (élève, période) hors-annulées suffit, ce
            // qui autorise la régénération explicite d'une facture annulée (§67).
            var stats = await GenerateForSchoolAsync(
                db, schoolId, settings,
                periodStart, periodEnd, dueDate, today, notif, bilingual,
                periodeLabel, skipStudentsWithPeriodInvoice: false, ct);

            report.SchoolsProcessed = 1;
            report.InvoicesCreated = stats.Created;
            report.InvoicesAlreadyExisting = stats.AlreadyExisting;
            report.InvoicesSkipped = stats.Skipped;
            report.StudentsWithoutFee = stats.WithoutFee;
            return report;
        }

        private async Task<PerSchoolStats> GenerateForSchoolAsync(
            AppDbContext db,
            int schoolId,
            SchoolPaymentSettings settings,
            DateTime periodStart,
            DateTime periodEnd,
            DateTime dueDate,
            DateTime today,
            INotificationService notif,
            bool bilingual,
            string periodeLabel,
            bool skipStudentsWithPeriodInvoice,
            CancellationToken ct)
        {
            var stats = new PerSchoolStats();

            // 2) Tous les élèves actifs avec leur ClassId (pour résoudre le tarif).
            var students = await db.Students
                .Where(s => s.SchoolId == schoolId && !s.IsDeleted)
                .Select(s => new { s.Id, s.ClassId, s.BoardingStatus, s.FirstName, s.LastName })
                .ToListAsync(ct);

            if (students.Count == 0) return stats;

            // Mode rattrapage : on saute tout élève ayant DÉJÀ une facture pour
            // la période (TOUT statut, ANNULÉE comprise) → jamais de recréation
            // d'une facture annulée volontairement, jamais de double facturation.
            // Garantit AU PLUS une tentative de génération par élève et par mois.
            HashSet<int> alreadyInvoiced = new();
            if (skipStudentsWithPeriodInvoice)
            {
                alreadyInvoiced = (await db.Invoices
                    .Where(i => i.SchoolId == schoolId && i.PeriodStart == periodStart)
                    .Select(i => i.StudentId)
                    .Distinct()
                    .ToListAsync(ct)).ToHashSet();
            }

            // 3) Tous les overrides étudiants de cette école en une requête.
            var studentIds = students.Select(s => s.Id).ToList();

            // 3-bis) Responsables (Guardian) joignables par SMS, groupés par élève.
            //        Sert à envoyer le SMS « facture due » après création.
            var guardiansByStudent = (await db.StudentGuardians
                    .Where(sg => studentIds.Contains(sg.StudentId)
                                 && !sg.Guardian.IsDeleted
                                 && sg.Guardian.PhoneNumber != null)
                    .Select(sg => new
                    {
                        sg.StudentId,
                        sg.GuardianId,
                        sg.Guardian.PhoneNumber,
                        sg.Guardian.PreferredLanguage
                    })
                    .ToListAsync(ct))
                .GroupBy(g => g.StudentId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 4) Tarif résolu par élève (tarif élève > statut > classe > général).
            //    Logique PARTAGÉE avec la re-tarification des factures impayées
            //    (InvoiceRepricingService) via FeeResolver → jamais de divergence
            //    entre une facture générée et une facture re-tarifée.
            var fees = await FeeResolver.ResolveAsync(
                db, schoolId, settings,
                students.Select(s => new FeeTarget(s.Id, s.ClassId, s.BoardingStatus)).ToList(),
                today, ct);

            // 5) Pour chaque élève, résout son montant et insère l'Invoice.
            //    INSERT individuel (pas AddRange + un seul SaveChanges) : on
            //    veut catch l'unique violation par élève sans casser le batch.
            foreach (var s in students)
            {
                // Rattrapage : élève déjà facturé ce mois (même annulé) → on saute.
                if (alreadyInvoiced.Contains(s.Id))
                {
                    stats.AlreadyExisting++;
                    continue;
                }

                // Tarif résolu (élève > statut > classe > général) via FeeResolver.
                long? amount = fees.TryGetValue(s.Id, out var f) ? f : null;

                if (amount is null or <= 0)
                {
                    stats.WithoutFee++;
                    _logger.LogWarning(
                        "[invoice-cron] SchoolId={SchoolId} StudentId={StudentId} ({Name}) sans tarif (ni tarif personnalisé, ni tarif de statut, ni ClassFee, ni tarif général) — skip",
                        schoolId, s.Id, $"{s.FirstName} {s.LastName}");
                    continue;
                }

                var invoice = new Invoice
                {
                    SchoolId = schoolId,
                    StudentId = s.Id,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    DueDate = dueDate,
                    AmountDueFcfa = amount.Value,
                    AmountPaidFcfa = 0,
                    Status = InvoiceStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };
                db.Invoices.Add(invoice);

                try
                {
                    await db.SaveChangesAsync(ct);
                    stats.Created++;

                    // SMS « facture due » aux responsables joignables (best-effort,
                    // post-commit). Uniquement sur facture NOUVELLEMENT créée — pas
                    // sur un re-run du cron (AlreadyExisting) → pas de spam.
                    if (guardiansByStudent.TryGetValue(s.Id, out var guardians))
                    {
                        var eleve = $"{s.FirstName} {s.LastName}".Trim();
                        var msg = NotificationTemplates.InvoiceDue(eleve, amount.Value, periodeLabel);
                        foreach (var g in guardians)
                        {
                            await notif.SendSmsAsync(new NotificationSmsRequest(
                                UserId: g.GuardianId,
                                RawPhone: g.PhoneNumber,
                                PreferredLanguage: g.PreferredLanguage ?? "fr",
                                Message: msg,
                                Bilingual: bilingual,
                                TemplateCode: "INVOICE_DUE",
                                RelatedEntityId: invoice.Id,
                                PushRoute: "/guardian/invoices"), ct);
                        }
                    }
                }
                catch (DbUpdateException dbex) when (IsUniqueViolation(dbex))
                {
                    // Déjà généré (par un tick précédent ou un retry). On
                    // détache l'entité pour ne pas polluer le change tracker
                    // et on continue avec le prochain élève.
                    db.Entry(invoice).State = EntityState.Detached;
                    stats.AlreadyExisting++;
                }
                catch (Exception ex)
                {
                    db.Entry(invoice).State = EntityState.Detached;
                    stats.Skipped++;
                    _logger.LogError(ex,
                        "[invoice-cron] Échec INSERT Invoice SchoolId={SchoolId} StudentId={StudentId}",
                        schoolId, s.Id);
                }
            }

            return stats;
        }

        // ---- Helpers ----

        private static DateTime NextFireUtc(DateTime nowUtc)
        {
            var todayRun = nowUtc.Date + RunAtUtc;
            return nowUtc < todayRun ? todayRun : todayRun.AddDays(1);
        }

        private static bool IsUniqueViolation(DbUpdateException ex)
        {
            return ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505";
        }

        private static readonly string[] FrMonths =
        {
            "janvier", "fevrier", "mars", "avril", "mai", "juin",
            "juillet", "aout", "septembre", "octobre", "novembre", "decembre"
        };

        /// <summary>"juin 2026" — sans accent (GSM-7) ni dépendance culture.</summary>
        private static string FrenchMonthYear(DateTime d) => $"{FrMonths[d.Month - 1]} {d.Year}";

        private record struct PerSchoolStats(
            int Created,
            int AlreadyExisting,
            int Skipped,
            int WithoutFee);
    }

    /// <summary>
    /// Résumé d'un run du job — exposé pour pouvoir le retourner depuis un
    /// endpoint admin de rejeu manuel si besoin.
    /// </summary>
    public class InvoiceGenerationReport
    {
        public DateTime RunAtUtc { get; set; }
        public int DayOfMonth { get; set; }
        public int SchoolsProcessed { get; set; }
        public int SchoolsFailed { get; set; }
        public int InvoicesCreated { get; set; }
        public int InvoicesAlreadyExisting { get; set; }
        public int InvoicesSkipped { get; set; }
        public int StudentsWithoutFee { get; set; }
    }
}
