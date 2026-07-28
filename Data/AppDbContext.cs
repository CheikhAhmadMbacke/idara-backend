using Microsoft.EntityFrameworkCore;
using Idara.API.Models;

namespace Idara.API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<School> Schools { get; set; }
        public DbSet<OtpRecord> OtpRecords { get; set; }
        public DbSet<Student> Students { get; set; }
        public DbSet<StudentGuardian> StudentGuardians { get; set; }
        public DbSet<StudentDocument> StudentDocuments { get; set; }
        public DbSet<Class> Classes { get; set; }

        public DbSet<AcademicYear> AcademicYears { get; set; }
        public DbSet<AcademicPeriod> AcademicPeriods { get; set; }
        public DbSet<Subject> Subjects { get; set; }
        public DbSet<ClassSubjectTeacher> ClassSubjectTeachers { get; set; }
        public DbSet<Attendance> Attendances { get; set; }
        public DbSet<StaffAttendance> StaffAttendances { get; set; }
        public DbSet<Grade> Grades { get; set; }
        public DbSet<CoranProgress> CoranProgresses { get; set; }
        public DbSet<CoranSession> CoranSessions { get; set; }
        public DbSet<CoranCycle> CoranCycles { get; set; }
        public DbSet<CoranDailyRecord> CoranDailyRecords { get; set; }
        public DbSet<CoranDailyPortion> CoranDailyPortions { get; set; }
        public DbSet<CoranEvaluation> CoranEvaluations { get; set; }
        public DbSet<CoranEvaluationIncident> CoranEvaluationIncidents { get; set; }
        public DbSet<TimetableSlot> TimetableSlots { get; set; }
        public DbSet<ReportCard> ReportCards { get; set; }
        public DbSet<ReportCardLine> ReportCardLines { get; set; }
        public DbSet<DailyJournalEntry> DailyJournalEntries { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<TrustedSchool> TrustedSchools { get; set; }

        // ----- Paiement (Phase 1) -----
        public DbSet<SchoolPaymentSettings> SchoolPaymentSettings { get; set; }
        public DbSet<ClassFee> ClassFees { get; set; }
        public DbSet<StudentFeeOverride> StudentFeeOverrides { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<PaymentInvoiceAllocation> PaymentInvoiceAllocations { get; set; }
        public DbSet<SchoolWallet> SchoolWallets { get; set; }
        public DbSet<WalletTransaction> WalletTransactions { get; set; }
        public DbSet<Withdrawal> Withdrawals { get; set; }
        public DbSet<TransferBeneficiary> TransferBeneficiaries { get; set; }
        public DbSet<WebhookEvent> WebhookEvents { get; set; }
        public DbSet<PlatformSettings> PlatformSettings { get; set; }
        public DbSet<PayoutAlert> PayoutAlerts { get; set; }
        public DbSet<PlatformOutflow> PlatformOutflows { get; set; }
        public DbSet<CashLedgerEntry> CashLedgerEntries { get; set; }
        public DbSet<CashCategory> CashCategories { get; set; }

        // ----- Abonnement plateforme (Phase 4) -----
        public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }
        public DbSet<SubscriptionInvoice> SubscriptionInvoices { get; set; }

        // ----- Notifications (Phase 2) -----
        public DbSet<NotificationLog> NotificationLogs { get; set; }
        public DbSet<PushDeviceToken> PushDeviceTokens { get; set; }

        // ----- Écritures rejouables depuis la file d'attente hors ligne -----
        public DbSet<IdempotencyRecord> IdempotencyRecords { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // --- PlatformSettings (singleton, Id figé = 1, pas de séquence) ---
            modelBuilder.Entity<PlatformSettings>()
                .Property(p => p.Id)
                .ValueGeneratedNever();

            // --- NotificationLog : index pour déduplication des rappels ---
            // (TemplateCode, RelatedEntityId) : « a-t-on déjà envoyé un rappel
            // OVERDUE pour cette facture ? ». + index temporel pour l'audit.
            modelBuilder.Entity<NotificationLog>()
                .HasIndex(n => new { n.TemplateCode, n.RelatedEntityId });
            modelBuilder.Entity<NotificationLog>()
                .HasIndex(n => n.CreatedAt);

            // --- PushDeviceToken : token unique (réaffectable), index par user ---
            modelBuilder.Entity<PushDeviceToken>()
                .HasIndex(t => t.Token)
                .IsUnique();
            modelBuilder.Entity<PushDeviceToken>()
                .HasIndex(t => t.UserId);
            modelBuilder.Entity<PushDeviceToken>()
                .HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- Idempotence des écritures rejouées depuis l'Outbox ---
            // Unique PAR UTILISATEUR : c'est cette contrainte qui fait tout le
            // travail. Deux rejeux simultanés de la même saisie se disputent
            // l'INSERT, le perdant reçoit une violation 23505 et se contente de
            // la réponse déjà enregistrée — même mécanique que l'idempotence des
            // webhooks (§50).
            modelBuilder.Entity<IdempotencyRecord>()
                .HasIndex(r => new { r.UserId, r.Key })
                .IsUnique();
            modelBuilder.Entity<IdempotencyRecord>()
                .HasIndex(r => r.CreatedAt);
            modelBuilder.Entity<IdempotencyRecord>()
                .Property(r => r.Key)
                .HasMaxLength(120);
            modelBuilder.Entity<IdempotencyRecord>()
                .Property(r => r.Endpoint)
                .HasMaxLength(200);

            modelBuilder.Entity<User>()
                .HasOne(u => u.School)
                .WithMany(s => s.Users)
                .HasForeignKey(u => u.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);

            // Unicité email/téléphone durcie de l'applicatif (cf. gotcha §91/§95)
            // vers une contrainte DB (Phase 2, post-lancement). Index UNIQUES FILTRÉS :
            //  - NULL multiples autorisés : SuperAdmin/SchoolAdmin sans téléphone,
            //    comptes école (Teacher/Staff/Guardian) sans email.
            //  - IsDeleted = FALSE : un compte anonymisé (email/tél scrubé en
            //    deleted-{id}@… ) ne bloque pas la réinscription au même identifiant.
            // L'email est normalisé en lowercase à l'écriture (register/invite/guardian,
            // §95) ET par une normalisation one-shot dans le Up() de la migration, donc
            // l'index sur la colonne brute garantit l'unicité insensible à la casse
            // sans recourir à un index fonctionnel lower() (hors snapshot EF).
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique()
                .HasFilter("\"Email\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            modelBuilder.Entity<User>()
                .HasIndex(u => u.PhoneNumber)
                .IsUnique()
                .HasFilter("\"PhoneNumber\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            modelBuilder.Entity<Student>()
                .HasIndex(s => new { s.SchoolId, s.IsDeleted });

            modelBuilder.Entity<Student>()
                .HasIndex(s => new { s.SchoolId, s.StudentNumber })
                .IsUnique()
                .HasFilter("\"StudentNumber\" IS NOT NULL");

            modelBuilder.Entity<StudentGuardian>()
                .HasIndex(sg => new { sg.StudentId, sg.GuardianId })
                .IsUnique();

            modelBuilder.Entity<Student>()
                .HasOne(s => s.School)
                .WithMany()
                .HasForeignKey(s => s.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Student>()
                .HasOne(s => s.Class)
                .WithMany(c => c.Students)
                .HasForeignKey(s => s.ClassId)
                .OnDelete(DeleteBehavior.SetNull);

            // Index FK pour accélérer les recherches "élèves d'une classe".
            modelBuilder.Entity<Student>()
                .HasIndex(s => s.ClassId);

            modelBuilder.Entity<StudentGuardian>()
                .HasOne(sg => sg.Student)
                .WithMany(s => s.StudentGuardians)
                .HasForeignKey(sg => sg.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StudentGuardian>()
                .HasOne(sg => sg.Guardian)
                .WithMany()
                .HasForeignKey(sg => sg.GuardianId)
                .OnDelete(DeleteBehavior.Restrict);

            // Index FK pour accélérer "tous les enfants d'un guardian".
            modelBuilder.Entity<StudentGuardian>()
                .HasIndex(sg => sg.GuardianId);

            // OTP : recherche par (email, purpose, état) à chaque vérification.
            modelBuilder.Entity<OtpRecord>()
                .HasIndex(o => new { o.Email, o.Purpose, o.IsUsed });

            modelBuilder.Entity<StudentDocument>()
                .HasOne(d => d.Student)
                .WithMany(s => s.Documents)
                .HasForeignKey(d => d.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StudentDocument>()
                .HasIndex(d => new { d.StudentId, d.Type });

            modelBuilder.Entity<Class>()
                .HasIndex(c => new { c.SchoolId, c.IsDeleted });

            // ----- Académique -----

            modelBuilder.Entity<AcademicYear>()
                .HasOne(y => y.School)
                .WithMany()
                .HasForeignKey(y => y.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);

            // Une seule année courante par école : index unique filtré (IsCurrent = TRUE).
            modelBuilder.Entity<AcademicYear>()
                .HasIndex(y => new { y.SchoolId, y.IsCurrent })
                .IsUnique()
                .HasFilter("\"IsCurrent\" = TRUE");

            modelBuilder.Entity<AcademicPeriod>()
                .HasOne(p => p.AcademicYear)
                .WithMany(y => y.Periods)
                .HasForeignKey(p => p.AcademicYearId)
                .OnDelete(DeleteBehavior.Cascade);

            // Une seule période courante par école : index unique filtré.
            modelBuilder.Entity<AcademicPeriod>()
                .HasIndex(p => new { p.SchoolId, p.IsCurrent })
                .IsUnique()
                .HasFilter("\"IsCurrent\" = TRUE");

            modelBuilder.Entity<Subject>()
                .HasOne(s => s.School)
                .WithMany()
                .HasForeignKey(s => s.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Subject>()
                .HasIndex(s => new { s.SchoolId, s.IsDeleted });

            modelBuilder.Entity<ClassSubjectTeacher>()
                .HasOne(a => a.Class)
                .WithMany()
                .HasForeignKey(a => a.ClassId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClassSubjectTeacher>()
                .HasOne(a => a.Subject)
                .WithMany()
                .HasForeignKey(a => a.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClassSubjectTeacher>()
                .HasOne(a => a.Teacher)
                .WithMany()
                .HasForeignKey(a => a.TeacherId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ClassSubjectTeacher>()
                .HasIndex(a => new { a.ClassId, a.SubjectId, a.TeacherId })
                .IsUnique();

            // ----- Opérations -----

            modelBuilder.Entity<Attendance>()
                .HasOne(a => a.Student)
                .WithMany()
                .HasForeignKey(a => a.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Attendance>()
                .HasIndex(a => new { a.SchoolId, a.Date });

            // Index unique partiel : un seul pointage actif (non supprimé)
            // par (élève, jour). Permet de soft-deleter et de re-créer.
            modelBuilder.Entity<Attendance>()
                .HasIndex(a => new { a.StudentId, a.Date })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = FALSE");

            // Soft-delete : les requêtes ignorent les Attendances supprimées
            // (utiliser .IgnoreQueryFilters() côté API pour l'audit).
            modelBuilder.Entity<Attendance>()
                .HasQueryFilter(a => !a.IsDeleted);

            // ----- Présence du personnel / enseignants (rôle Surveillant) -----
            modelBuilder.Entity<StaffAttendance>()
                .HasOne(a => a.Staff)
                .WithMany()
                .HasForeignKey(a => a.StaffId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StaffAttendance>()
                .HasIndex(a => new { a.SchoolId, a.Date });

            // Un seul pointage actif par (membre du personnel, jour).
            modelBuilder.Entity<StaffAttendance>()
                .HasIndex(a => new { a.StaffId, a.Date })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = FALSE");

            modelBuilder.Entity<StaffAttendance>()
                .HasQueryFilter(a => !a.IsDeleted);

            modelBuilder.Entity<Grade>()
                .HasOne(g => g.Student)
                .WithMany()
                .HasForeignKey(g => g.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Grade>()
                .HasOne(g => g.Subject)
                .WithMany()
                .HasForeignKey(g => g.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Grade>()
                .HasOne(g => g.Class)
                .WithMany()
                .HasForeignKey(g => g.ClassId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Grade>()
                .HasOne(g => g.AcademicPeriod)
                .WithMany()
                .HasForeignKey(g => g.AcademicPeriodId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Grade>()
                .HasIndex(g => new { g.StudentId, g.AcademicPeriodId, g.SubjectId });

            // Soft-delete : les requêtes ignorent les notes supprimées.
            modelBuilder.Entity<Grade>()
                .HasQueryFilter(g => !g.IsDeleted);

            modelBuilder.Entity<CoranProgress>()
                .HasOne(p => p.Student)
                .WithMany()
                .HasForeignKey(p => p.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CoranProgress>()
                .HasIndex(p => p.StudentId)
                .IsUnique();

            modelBuilder.Entity<CoranSession>()
                .HasOne(s => s.Student)
                .WithMany()
                .HasForeignKey(s => s.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CoranSession>()
                .HasOne(s => s.Teacher)
                .WithMany()
                .HasForeignKey(s => s.TeacherId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<CoranSession>()
                .HasIndex(s => new { s.StudentId, s.Date });

            // --- Suivi quotidien Coran (cycles 22 jours) ---
            modelBuilder.Entity<CoranCycle>()
                .HasOne(c => c.Student)
                .WithMany()
                .HasForeignKey(c => c.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CoranCycle>()
                .HasIndex(c => new { c.StudentId, c.Number })
                .IsUnique();

            // Au plus UN cycle ouvert par élève à la fois.
            modelBuilder.Entity<CoranCycle>()
                .HasIndex(c => c.StudentId)
                .IsUnique()
                .HasFilter("\"IsComplete\" = FALSE");

            modelBuilder.Entity<CoranDailyRecord>()
                .HasOne(r => r.Cycle)
                .WithMany(c => c.Records)
                .HasForeignKey(r => r.CycleId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CoranDailyRecord>()
                .HasOne(r => r.Student)
                .WithMany()
                .HasForeignKey(r => r.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Une seule ligne par élève par jour (ré-enregistrer = upsert).
            modelBuilder.Entity<CoranDailyRecord>()
                .HasIndex(r => new { r.StudentId, r.Date })
                .IsUnique();

            modelBuilder.Entity<CoranDailyPortion>()
                .HasOne(p => p.DailyRecord)
                .WithMany(r => r.Portions)
                .HasForeignKey(p => p.DailyRecordId)
                .OnDelete(DeleteBehavior.Cascade);

            // Une portion par type (nouvelle leçon / révision récente / ancienne) par jour.
            modelBuilder.Entity<CoranDailyPortion>()
                .HasIndex(p => new { p.DailyRecordId, p.Kind })
                .IsUnique();

            // --- Évaluation qualité (par hizb, administration) ---
            modelBuilder.Entity<CoranEvaluation>()
                .HasOne(e => e.Student)
                .WithMany()
                .HasForeignKey(e => e.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CoranEvaluation>()
                .HasOne(e => e.Evaluator)
                .WithMany()
                .HasForeignKey(e => e.EvaluatorId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<CoranEvaluation>()
                .HasIndex(e => new { e.StudentId, e.Date });

            modelBuilder.Entity<CoranEvaluationIncident>()
                .HasOne(i => i.Evaluation)
                .WithMany(e => e.Incidents)
                .HasForeignKey(i => i.EvaluationId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TimetableSlot>()
                .HasOne(t => t.Class)
                .WithMany()
                .HasForeignKey(t => t.ClassId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TimetableSlot>()
                .HasOne(t => t.Subject)
                .WithMany()
                .HasForeignKey(t => t.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TimetableSlot>()
                .HasOne(t => t.Teacher)
                .WithMany()
                .HasForeignKey(t => t.TeacherId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<TimetableSlot>()
                .HasIndex(t => new { t.ClassId, t.DayOfWeek });

            // Anti-conflit : pas deux créneaux exactement identiques pour la
            // même classe le même jour à la même heure. Les chevauchements
            // partiels restent vérifiés en code (TimetableController).
            modelBuilder.Entity<TimetableSlot>()
                .HasIndex(t => new { t.ClassId, t.DayOfWeek, t.StartTime, t.EndTime })
                .IsUnique();

            modelBuilder.Entity<ReportCard>()
                .HasOne(r => r.Student)
                .WithMany()
                .HasForeignKey(r => r.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ReportCard>()
                .HasOne(r => r.AcademicPeriod)
                .WithMany()
                .HasForeignKey(r => r.AcademicPeriodId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ReportCard>()
                .HasIndex(r => new { r.StudentId, r.AcademicPeriodId })
                .IsUnique();

            modelBuilder.Entity<ReportCardLine>()
                .HasOne(l => l.ReportCard)
                .WithMany(r => r.Lines)
                .HasForeignKey(l => l.ReportCardId)
                .OnDelete(DeleteBehavior.Cascade);

            // ----- Journal quotidien -----

            modelBuilder.Entity<DailyJournalEntry>()
                .HasOne(j => j.Student)
                .WithMany()
                .HasForeignKey(j => j.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DailyJournalEntry>()
                .HasOne(j => j.Teacher)
                .WithMany()
                .HasForeignKey(j => j.TeacherId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<DailyJournalEntry>()
                .HasOne(j => j.Subject)
                .WithMany()
                .HasForeignKey(j => j.SubjectId)
                .OnDelete(DeleteBehavior.SetNull);

            // Une entrée active par enseignant + matière + élève + jour.
            // SubjectId NULL est traité comme valeur distincte par SQLite.
            // Filtré sur IsDeleted = 0 pour pouvoir soft-deleter et recréer.
            modelBuilder.Entity<DailyJournalEntry>()
                .HasIndex(j => new { j.StudentId, j.Date, j.TeacherId, j.SubjectId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = FALSE");

            modelBuilder.Entity<DailyJournalEntry>()
                .HasIndex(j => new { j.SchoolId, j.Date });

            // Soft-delete : les requêtes ignorent les entrées supprimées.
            modelBuilder.Entity<DailyJournalEntry>()
                .HasQueryFilter(j => !j.IsDeleted);

            // ----- Refresh tokens -----

            modelBuilder.Entity<RefreshToken>()
                .HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index unique sur le hash : un même token ne peut exister qu'une fois.
            modelBuilder.Entity<RefreshToken>()
                .HasIndex(r => r.TokenHash)
                .IsUnique();

            // Index de recherche : "tokens actifs d'un user".
            modelBuilder.Entity<RefreshToken>()
                .HasIndex(r => new { r.UserId, r.RevokedAt });

            // ============================================================
            // ===== Paiement (Phase 1) — fondations payin =====
            // ============================================================

            // --- SchoolPaymentSettings (1-1 avec School, PK = SchoolId) ---
            modelBuilder.Entity<SchoolPaymentSettings>()
                .HasKey(s => s.SchoolId);

            modelBuilder.Entity<SchoolPaymentSettings>()
                .HasOne(s => s.School)
                .WithOne()
                .HasForeignKey<SchoolPaymentSettings>(s => s.SchoolId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- ClassFee (versionné par EffectiveFrom, append-only) ---
            modelBuilder.Entity<ClassFee>()
                .HasOne(f => f.Class)
                .WithMany()
                .HasForeignKey(f => f.ClassId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index principal : lookup du tarif courant pour une classe
            // (ORDER BY EffectiveFrom DESC LIMIT 1).
            modelBuilder.Entity<ClassFee>()
                .HasIndex(f => new { f.ClassId, f.EffectiveFrom });

            // Filtre multi-tenant : "tous les ClassFee d'une école".
            modelBuilder.Entity<ClassFee>()
                .HasIndex(f => f.SchoolId);

            // --- StudentFeeOverride (1-1 avec Student, PK = StudentId) ---
            modelBuilder.Entity<StudentFeeOverride>()
                .HasKey(o => o.StudentId);

            modelBuilder.Entity<StudentFeeOverride>()
                .HasOne(o => o.Student)
                .WithOne()
                .HasForeignKey<StudentFeeOverride>(o => o.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StudentFeeOverride>()
                .HasIndex(o => o.SchoolId);

            // --- Invoice ---
            modelBuilder.Entity<Invoice>()
                .HasOne(i => i.Student)
                .WithMany()
                .HasForeignKey(i => i.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Idempotence du cron de génération : un seul Invoice actif
            // (non Cancelled) par élève par période. On filtre sur Status pour
            // permettre une régénération si l'admin annule manuellement.
            modelBuilder.Entity<Invoice>()
                .HasIndex(i => new { i.StudentId, i.PeriodStart })
                .IsUnique()
                .HasFilter("\"Status\" <> 3"); // 3 = InvoiceStatus.Cancelled

            // Vues école : "factures en cours/en retard".
            modelBuilder.Entity<Invoice>()
                .HasIndex(i => new { i.SchoolId, i.Status });

            modelBuilder.Entity<Invoice>()
                .HasIndex(i => new { i.SchoolId, i.DueDate });

            // --- Payment ---
            // StudentId / GuardianId / InvoiceId tous nullables : un topup
            // wallet école (Phase 4) n'a aucun de ces 3 liens.
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Student)
                .WithMany()
                .HasForeignKey(p => p.StudentId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Guardian)
                .WithMany()
                .HasForeignKey(p => p.GuardianId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Invoice)
                .WithMany()
                .HasForeignKey(p => p.InvoiceId)
                .OnDelete(DeleteBehavior.SetNull);

            // FK vers le DONATEUR (Purpose=Donation). Restrict comme Guardian :
            // un don appartient à l'append-only financier, on ne supprime jamais
            // physiquement un donateur ayant donné (anonymisation §68 à la place).
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Donor)
                .WithMany()
                .HasForeignKey(p => p.DonorId)
                .OnDelete(DeleteBehavior.Restrict);

            // Vue Donateur : « mes dons » (récents d'abord).
            modelBuilder.Entity<Payment>()
                .HasIndex(p => new { p.DonorId, p.InitiatedAt });

            // Lookup par référence SenePay (webhook → Payment correspondant).
            // Filtré IS NOT NULL car le Payment est créé AVANT l'appel SenePay.
            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.SenePayTransactionId)
                .IsUnique()
                .HasFilter("\"SenePayTransactionId\" IS NOT NULL");

            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.SenePayInternalId)
                .HasFilter("\"SenePayInternalId\" IS NOT NULL");

            // Lookup public depuis la page HTML de résultat post-paiement
            // (auth = match du token, pas de JWT). Filtré NOT NULL parce que
            // les vieux Payments d'avant 1.10 n'en ont pas.
            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.PublicResultToken)
                .IsUnique()
                .HasFilter("\"PublicResultToken\" IS NOT NULL");

            // Vues école : "paiements récents", "paiements en attente".
            modelBuilder.Entity<Payment>()
                .HasIndex(p => new { p.SchoolId, p.Status, p.InitiatedAt });

            // Vue Guardian : "mes paiements".
            modelBuilder.Entity<Payment>()
                .HasIndex(p => new { p.GuardianId, p.InitiatedAt });

            // --- PaymentInvoiceAllocation (paiement consolidé → N factures) ---
            // Cascade depuis Payment (jamais supprimé hors purge d'école) et
            // depuis Invoice (une suppression d'élève cascade ses factures → et
            // donc ses allocations, sans bloquer la purge).
            modelBuilder.Entity<PaymentInvoiceAllocation>()
                .HasOne(a => a.Payment)
                .WithMany(p => p.InvoiceAllocations)
                .HasForeignKey(a => a.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PaymentInvoiceAllocation>()
                .HasOne(a => a.Invoice)
                .WithMany()
                .HasForeignKey(a => a.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Une facture ne peut être allouée qu'une fois par paiement.
            modelBuilder.Entity<PaymentInvoiceAllocation>()
                .HasIndex(a => new { a.PaymentId, a.InvoiceId })
                .IsUnique();

            // Lookup « quels paiements ont touché cette facture ».
            modelBuilder.Entity<PaymentInvoiceAllocation>()
                .HasIndex(a => a.InvoiceId);

            // --- SchoolWallet (1-1 avec School, PK = SchoolId) ---
            modelBuilder.Entity<SchoolWallet>()
                .HasKey(w => w.SchoolId);

            modelBuilder.Entity<SchoolWallet>()
                .HasOne(w => w.School)
                .WithOne()
                .HasForeignKey<SchoolWallet>(w => w.SchoolId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- WalletTransaction (append-only) ---
            modelBuilder.Entity<WalletTransaction>()
                .HasOne(t => t.Wallet)
                .WithMany()
                .HasForeignKey(t => t.SchoolId)
                .OnDelete(DeleteBehavior.Cascade);

            // Journal trié par date desc (vue "derniers mouvements").
            modelBuilder.Entity<WalletTransaction>()
                .HasIndex(t => new { t.SchoolId, t.OccurredAt });

            // Lookup "toutes les transactions liées à un Payment" (audit).
            modelBuilder.Entity<WalletTransaction>()
                .HasIndex(t => new { t.RelatedEntity, t.RelatedId });

            // --- Withdrawal (retraits école, append-only) ---
            modelBuilder.Entity<Withdrawal>()
                .HasOne(w => w.School)
                .WithMany()
                .HasForeignKey(w => w.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);

            // Vue école : historique des retraits (récents d'abord).
            modelBuilder.Entity<Withdrawal>()
                .HasIndex(w => new { w.SchoolId, w.CreatedAt });

            // Lookup webhook payout → Withdrawal correspondant. Rempli après
            // l'appel SenePay (le Withdrawal est créé AVANT), d'où le filtre
            // NOT NULL sur l'unique.
            modelBuilder.Entity<Withdrawal>()
                .HasIndex(w => w.SenePayDisbursementId)
                .IsUnique()
                .HasFilter("\"SenePayDisbursementId\" IS NOT NULL");

            // FK optionnelle vers le bénéficiaire du carnet. Restrict : on
            // n'efface jamais un bénéficiaire référencé par un transfert passé
            // (on l'archive), pour préserver l'historique financier.
            modelBuilder.Entity<Withdrawal>()
                .HasOne(w => w.Beneficiary)
                .WithMany()
                .HasForeignKey(w => w.BeneficiaryId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- TransferBeneficiary (carnet de bénéficiaires par école) ---
            modelBuilder.Entity<TransferBeneficiary>()
                .HasOne(b => b.School)
                .WithMany()
                .HasForeignKey(b => b.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);

            // Vue carnet : bénéficiaires actifs d'une école.
            modelBuilder.Entity<TransferBeneficiary>()
                .HasIndex(b => new { b.SchoolId, b.IsArchived });

            // --- WebhookEvent (idempotence stricte) ---
            // Cle de l'idempotence : INSERT (Provider, ExternalEventId) → si
            // unique violation, c'est un rejeu, on retourne 200 sans rejouer.
            modelBuilder.Entity<WebhookEvent>()
                .HasIndex(w => new { w.Provider, w.ExternalEventId })
                .IsUnique();

            // Vue admin : "derniers webhooks reçus" (debug, monitoring).
            modelBuilder.Entity<WebhookEvent>()
                .HasIndex(w => new { w.Provider, w.ReceivedAt });

            // jsonb natif PG pour requêtes ad hoc sur le payload SenePay.
            modelBuilder.Entity<WebhookEvent>()
                .Property(w => w.Payload)
                .HasColumnType("jsonb");

            // --- PayoutAlert (anomalies de décaissement, append-only) ---
            // Détails structurés en jsonb (montants, ids SenePay…) pour debug.
            modelBuilder.Entity<PayoutAlert>()
                .Property(a => a.Details)
                .HasColumnType("jsonb");

            // Vue dashboard SuperAdmin : alertes non résolues, récentes d'abord.
            modelBuilder.Entity<PayoutAlert>()
                .HasIndex(a => new { a.Resolved, a.CreatedAt });

            // Lookup "alertes d'un retrait donné" (dont la garde anti-spam
            // StuckUnderVerification dans PayoutVerificationJob).
            modelBuilder.Entity<PayoutAlert>()
                .HasIndex(a => a.WithdrawalId);

            // --- PlatformOutflow (sorties du solde de gains plateforme, append-only) ---
            // Somme requêtée à chaque réconciliation → index sur OccurredAt.
            modelBuilder.Entity<PlatformOutflow>()
                .HasIndex(o => o.OccurredAt);

            // Idempotence de l'auto-enregistrement des payouts hors Idara : un même
            // disbursement_id SenePay ne peut être consigné qu'une fois. Filtré
            // (les ajustements manuels ont SenePayReference NULL, non contraints).
            modelBuilder.Entity<PlatformOutflow>()
                .HasIndex(o => o.SenePayReference)
                .IsUnique()
                .HasFilter("\"SenePayReference\" IS NOT NULL");

            // ===== Livre de caisse (gestion financière daara, F1/F2) =====
            modelBuilder.Entity<CashLedgerEntry>()
                .HasIndex(e => new { e.SchoolId, e.OccurredAt });
            modelBuilder.Entity<CashLedgerEntry>()
                .HasOne(e => e.CategoryRef)
                .WithMany()
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Restrict); // catégorie archivée, jamais supprimée
            modelBuilder.Entity<CashCategory>()
                .HasIndex(c => new { c.SchoolId, c.Type });

            // ===== Abonnement plateforme (Phase 4) =====

            // Plan public : Code unique parmi les plans NON custom (les deals
            // custom peuvent réutiliser n'importe quel code, on filtre dessus).
            modelBuilder.Entity<SubscriptionPlan>()
                .HasIndex(p => p.Code)
                .IsUnique()
                .HasFilter("\"IsCustom\" = FALSE");

            modelBuilder.Entity<SubscriptionPlan>()
                .HasOne(p => p.School)
                .WithMany()
                .HasForeignKey(p => p.SchoolId)
                .OnDelete(DeleteBehavior.Cascade);

            // 1 abonnement par école.
            modelBuilder.Entity<Subscription>()
                .HasIndex(s => s.SchoolId)
                .IsUnique();

            modelBuilder.Entity<Subscription>()
                .HasOne(s => s.School)
                .WithMany()
                .HasForeignKey(s => s.SchoolId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict : ne pas supprimer un plan référencé par un abo (le
            // snapshot reste autoritatif, mais on garde le lien pour l'affichage).
            modelBuilder.Entity<Subscription>()
                .HasOne(s => s.Plan)
                .WithMany()
                .HasForeignKey(s => s.PlanId)
                .OnDelete(DeleteBehavior.SetNull);

            // Cron de facturation : prélever les abos échus.
            modelBuilder.Entity<Subscription>()
                .HasIndex(s => new { s.Status, s.NextBillingAt });

            modelBuilder.Entity<SubscriptionInvoice>()
                .HasOne(i => i.Subscription)
                .WithMany()
                .HasForeignKey(i => i.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Idempotence du cron : une seule facture par abo et par période
            // (hors annulées). Évite les doublons sur re-run du billing.
            modelBuilder.Entity<SubscriptionInvoice>()
                .HasIndex(i => new { i.SubscriptionId, i.PeriodStart })
                .IsUnique()
                .HasFilter("\"Status\" <> 3");

            modelBuilder.Entity<SubscriptionInvoice>()
                .HasIndex(i => i.SchoolId);
        }
    }
}
