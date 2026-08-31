using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Admin;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.School;
using Idara.API.Enums;
using Idara.API.Options;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolModel = Idara.API.Models.School;

namespace Idara.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SchoolController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IUserDeletionService _userDeletion;
        private readonly IWebHostEnvironment _env;
        private readonly UploadSettings _uploads;
        private readonly ILogger<SchoolController> _logger;

        public SchoolController(
            AppDbContext context,
            IUserDeletionService userDeletion,
            IWebHostEnvironment env,
            IOptions<UploadSettings> uploads,
            ILogger<SchoolController> logger)
        {
            _context = context;
            _userDeletion = userDeletion;
            _env = env;
            _uploads = uploads.Value;
            _logger = logger;
        }

        [HttpGet("my-info")]
        public async Task<IActionResult> GetMySchoolInfo()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var user = await _context.Users
                .Include(u => u.School)
                    .ThenInclude(s => s!.Users)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user?.School == null)
                return NotFound(ApiResponse<bool>.Fail("Aucune école associée à cet utilisateur."));

            return Ok(MapToSchoolInfoResponse(user.School));
        }

        [Authorize(Roles = UserRoles.SuperAdmin)]
        [HttpGet("all")]
        public async Task<IActionResult> GetAllSchools([FromQuery] KycStatus? status = null)
        {
            var query = _context.Schools.Include(s => s.Users).AsQueryable();
            if (status.HasValue)
                query = query.Where(s => s.KycStatus == status.Value);

            var schools = await query.ToListAsync();
            return Ok(schools.Select(MapToSchoolInfoResponse));
        }

        [Authorize(Roles = UserRoles.SuperAdmin)]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetSchoolById(int id)
        {
            var school = await _context.Schools
                .Include(s => s.Users)
                .FirstOrDefaultAsync(s => s.Id == id);
            if (school == null)
                return NotFound(ApiResponse<bool>.Fail("École non trouvée."));

            return Ok(MapToSchoolInfoResponse(school));
        }

        /// <summary>
        /// Édite les informations d'une école (SuperAdmin only). Les écoles qui
        /// veulent modifier leurs infos contactent le support avec justificatif.
        /// </summary>
        [Authorize(Roles = UserRoles.SuperAdmin)]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateSchool(int id, [FromBody] UpdateSchoolDto dto)
        {
            var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == id);
            if (school == null)
                return NotFound(ApiResponse<bool>.Fail("École non trouvée."));

            ApplySchoolFields(school, dto);
            await _context.SaveChangesAsync();
            _logger.LogInformation("[school] École {SchoolId} éditée par SuperAdmin {AdminId}", id, User.GetUserId());

            var reloaded = await _context.Schools.Include(s => s.Users).FirstAsync(s => s.Id == id);
            return Ok(MapToSchoolInfoResponse(reloaded));
        }

        /// <summary>
        /// Édition de SA PROPRE école par le SchoolAdmin. L'adresse email de
        /// connexion (portée par le compte User, PAS par l'entité School) n'est
        /// VOLONTAIREMENT pas modifiable ici — sinon une école pourrait changer
        /// d'email pour tenter de recréer un compte/essai. Mêmes champs que
        /// l'édition SuperAdmin (nom, adresse, téléphone, représentant).
        /// </summary>
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        [HttpPut("my-info")]
        public async Task<IActionResult> UpdateMySchool([FromBody] UpdateSchoolDto dto)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == schoolId.Value);
            if (school == null)
                return NotFound(ApiResponse<bool>.Fail("Aucune école associée à cet utilisateur."));

            ApplySchoolFields(school, dto);
            await _context.SaveChangesAsync();
            _logger.LogInformation("[school] École {SchoolId} éditée par son SchoolAdmin {AdminId}", schoolId, User.GetUserId());

            var reloaded = await _context.Schools.Include(s => s.Users).FirstAsync(s => s.Id == schoolId.Value);
            return Ok(MapToSchoolInfoResponse(reloaded));
        }

        // ================================================================
        // ===== Personnalisation de l'espace (branding) =====
        // ================================================================

        /// <summary>
        /// Branding de MON école — accessible à TOUT utilisateur rattaché à une
        /// école (admin/personnel/enseignant/surveillant/observateur) pour afficher
        /// la carte d'accueil personnalisée. Renvoie le nom (= titre), le logo, le
        /// sous-titre, la couleur/l'image de couverture.
        /// </summary>
        [HttpGet("branding")]
        public async Task<ActionResult<SchoolBrandingDto>> GetMyBranding(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var school = await _context.Schools
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == schoolId.Value, ct);
            if (school == null) return NotFound(ApiResponse<bool>.Fail("École introuvable."));

            return Ok(MapToBrandingDto(school));
        }

        /// <summary>
        /// Branding d'un daara par son id — pour tout utilisateur AUTHENTIFIÉ, afin
        /// d'afficher la carte de bienvenue personnalisée dans un écran rattaché à
        /// ce daara précis alors que l'utilisateur n'y est pas rattaché par son JWT :
        /// parent (fiche enfant / accueil), donateur (écran de don), SuperAdmin
        /// (fiche école). Faible sensibilité : le nom est déjà public (liste des
        /// dons), logo/couleur/sous-titre sont cosmétiques. Ne renvoie que les
        /// écoles existantes.
        /// </summary>
        [HttpGet("{schoolId:int}/branding")]
        public async Task<ActionResult<SchoolBrandingDto>> GetBrandingById(int schoolId, CancellationToken ct)
        {
            var school = await _context.Schools
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == schoolId, ct);
            if (school == null) return NotFound(ApiResponse<bool>.Fail("École introuvable."));

            return Ok(MapToBrandingDto(school));
        }

        /// <summary>
        /// Met à jour le branding de MON école (SchoolAdmin only) : logo, sous-titre,
        /// couleur et/ou image de couverture. Le nom (titre) s'édite via my-info.
        /// </summary>
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        [HttpPut("branding")]
        public async Task<ActionResult<SchoolBrandingDto>> UpdateMyBranding(
            [FromBody] UpdateSchoolBrandingDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == schoolId.Value, ct);
            if (school == null) return NotFound(ApiResponse<bool>.Fail("École introuvable."));

            // Sous-titre : null = inchangé ; "" = revenir au défaut (stocké null).
            if (dto.WelcomeSubtitle != null)
                school.WelcomeSubtitle = string.IsNullOrWhiteSpace(dto.WelcomeSubtitle)
                    ? null : dto.WelcomeSubtitle.Trim();

            // Couleur : null = inchangé ; "" = défaut (dégradé). Sinon hex validé par le DTO.
            if (dto.CoverColor != null)
                school.CoverColor = string.IsNullOrWhiteSpace(dto.CoverColor) ? null : dto.CoverColor;

            // Logo : remove prioritaire, sinon nouveau base64 remplace l'ancien.
            if (dto.RemoveLogo)
            {
                DeleteBrandingFile(school.LogoUrl);
                school.LogoUrl = null;
            }
            else if (!string.IsNullOrWhiteSpace(dto.LogoBase64))
            {
                var saved = await SaveBrandingImageAsync(dto.LogoBase64);
                if (saved == null)
                    return BadRequest(ApiResponse<bool>.Fail("Logo invalide (format ou taille non supportés)."));
                DeleteBrandingFile(school.LogoUrl);
                school.LogoUrl = saved;
            }

            // Image de couverture : idem.
            if (dto.RemoveCoverImage)
            {
                DeleteBrandingFile(school.CoverImageUrl);
                school.CoverImageUrl = null;
            }
            else if (!string.IsNullOrWhiteSpace(dto.CoverImageBase64))
            {
                var saved = await SaveBrandingImageAsync(dto.CoverImageBase64);
                if (saved == null)
                    return BadRequest(ApiResponse<bool>.Fail("Image de couverture invalide (format ou taille non supportés)."));
                DeleteBrandingFile(school.CoverImageUrl);
                school.CoverImageUrl = saved;
            }

            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("[school] Branding de l'école {SchoolId} mis à jour par {AdminId}", schoolId, User.GetUserId());
            return Ok(MapToBrandingDto(school));
        }

        private static SchoolBrandingDto MapToBrandingDto(SchoolModel s) => new()
        {
            SchoolId = s.Id,
            Name = s.Name ?? string.Empty,
            NameAr = s.NameAr,
            LogoUrl = s.LogoUrl,
            WelcomeSubtitle = s.WelcomeSubtitle,
            CoverColor = s.CoverColor,
            CoverImageUrl = s.CoverImageUrl
        };

        /// <summary>Décode + valide + sauvegarde une image de branding dans /uploads/school-branding/.</summary>
        private async Task<string?> SaveBrandingImageAsync(string base64)
        {
            var decoded = FileUploadValidator.DecodeAndValidate(
                base64, _uploads.MaxPhotoSizeMb, _uploads.AllowedPhotoMimeTypes);
            if (decoded == null) return null;

            var folder = Path.Combine(_env.WebRootPath, "uploads", "school-branding");
            Directory.CreateDirectory(folder);
            var fileName = $"{Guid.NewGuid():N}{decoded.Extension}";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), decoded.Bytes);
            return $"/uploads/school-branding/{fileName}";
        }

        /// <summary>Supprime un fichier de branding du disque (best-effort, défense path-traversal).</summary>
        private void DeleteBrandingFile(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                var root = Path.GetFullPath(_env.WebRootPath);
                var full = Path.GetFullPath(Path.Combine(_env.WebRootPath, url.TrimStart('/')));
                if (full.StartsWith(root) && System.IO.File.Exists(full))
                    System.IO.File.Delete(full);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[school] Échec suppression fichier branding {Url} (non bloquant)", url);
            }
        }

        /// <summary>Applique les champs éditables d'une école depuis le DTO (jamais l'email).</summary>
        private static void ApplySchoolFields(SchoolModel school, UpdateSchoolDto dto)
        {
            // Nom : au moins l'un des deux est garanti non vide par la validation du
            // DTO (SchoolNameRule) — on peut donc les normaliser tous les deux sans
            // risque de laisser le daara sans aucun nom.
            school.Name = string.IsNullOrWhiteSpace(dto.Name) ? null : dto.Name.Trim();
            school.NameAr = string.IsNullOrWhiteSpace(dto.NameAr) ? null : dto.NameAr.Trim();
            school.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();
            school.PhoneNumber = string.IsNullOrWhiteSpace(dto.PhoneNumber) ? null : dto.PhoneNumber.Trim();
            school.RepresentativeFirstName = string.IsNullOrWhiteSpace(dto.RepresentativeFirstName) ? null : dto.RepresentativeFirstName.Trim();
            school.RepresentativeLastName = string.IsNullOrWhiteSpace(dto.RepresentativeLastName) ? null : dto.RepresentativeLastName.Trim();
            school.RepresentativePhone = string.IsNullOrWhiteSpace(dto.RepresentativePhone) ? null : dto.RepresentativePhone.Trim();
            if (dto.QuranRiwaya.HasValue) school.QuranRiwaya = dto.QuranRiwaya.Value;
            // NULL = inchangé : une app antérieure au champ ne doit rien effacer (§140).
            if (dto.SchoolType.HasValue) school.Type = dto.SchoolType.Value;

        }

        /// <summary>
        /// Supprime DÉFINITIVEMENT une école et TOUTES ses données (utilisateurs,
        /// élèves, classes, pédagogie, paiements, factures, wallet…). Irréversible.
        /// SuperAdmin only. Implémenté en cascade explicite dans l'ordre des FK,
        /// au sein d'une transaction (tout ou rien).
        /// </summary>
        [Authorize(Roles = UserRoles.SuperAdmin)]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSchool(int id, CancellationToken ct)
        {
            var exists = await _context.Schools.AnyAsync(s => s.Id == id, ct);
            if (!exists) return NotFound(ApiResponse<bool>.Fail("École non trouvée."));

            await DeleteSchoolCascadeAsync(id, ct);
            _logger.LogWarning("[school] École {SchoolId} SUPPRIMÉE définitivement par SuperAdmin {AdminId}", id, User.GetUserId());
            return Ok(ApiResponse<bool>.Ok(true, "École et toutes ses données supprimées définitivement."));
        }

        /// <summary>
        /// Cascade hard-delete : supprime toutes les données d'une école dans
        /// l'ordre des dépendances FK (enfants → parents), via ExecuteDelete
        /// (SQL direct, bypass change tracker), dans une transaction atomique.
        /// Les guardians (SchoolId = null, rattachés par liens StudentGuardian)
        /// sont supprimés s'ils ne référencent plus rien après le nettoyage.
        /// </summary>
        private async Task DeleteSchoolCascadeAsync(int id, CancellationToken ct)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            // Guardians rattachés à cette école (capturés AVANT de couper les liens).
            var guardianIds = await _context.StudentGuardians
                .Where(sg => sg.Student.SchoolId == id)
                .Select(sg => sg.GuardianId)
                .Distinct()
                .ToListAsync(ct);

            // 1) Finance (enfants d'abord)
            await _context.WalletTransactions.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.Withdrawals.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);        // avant TransferBeneficiaries
            await _context.TransferBeneficiaries.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            // ⚠️ AVANT les Payments : depuis l'encaissement en espèces, une écriture
            // de caisse référence le paiement qui l'a produite (FK Restrict).
            await _context.CashLedgerEntries.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct); // avant Payments ET CashCategories (FK Restrict)
            await _context.PaymentInvoiceAllocations.Where(a => a.Payment.SchoolId == id).ExecuteDeleteAsync(ct); // avant Payments/Invoices
            await _context.PaymentStudentAllocations.Where(a => a.Payment.SchoolId == id).ExecuteDeleteAsync(ct); // avant Payments/Students
            await _context.Payments.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);           // avant Invoice/Student/Guardian
            await _context.PaymentLinks.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);       // après Payments (FK SetNull), avant Users
            await _context.Invoices.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.StudentFeeOverrides.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.ClassFees.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.SchoolWallets.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.SchoolPaymentSettings.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.PayoutAlerts.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.CashCategories.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            // Abonnement plateforme (factures → abo → deals custom de l'école).
            await _context.SubscriptionInvoices.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.Subscriptions.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.SubscriptionPlans.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct); // plans custom (SchoolId non-null)

            // 2) Pédagogie (enfants d'abord)
            await _context.ReportCardLines.Where(l => l.ReportCard.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.ReportCards.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.Grades.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.Attendances.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.DailyJournalEntries.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.CoranSessions.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.CoranProgresses.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            // Suivi quotidien + évaluation Coran (enfants d'abord).
            await _context.CoranEvaluationIncidents.Where(x => x.Evaluation.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.CoranEvaluations.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.CoranDailyPortions.Where(x => x.DailyRecord.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.CoranDailyRecords.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.CoranCycles.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.StaffAttendances.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            // Étapes de démarrage écartées. Sa clé étrangère est en cascade,
            // donc PostgreSQL s'en chargerait — on la liste quand même : cette
            // méthode est la liste de référence, et le jour où quelqu'un passe
            // la FK en Restrict, l'oubli se paierait par un 500 au milieu d'une
            // suppression déjà entamée (§77/§103).
            await _context.SchoolSetupDismissals.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            // Journal du daara (photos avant événements). IgnoreQueryFilters :
            // sans lui, le filtre global !IsDeleted laisserait les événements
            // effacés en base, et la suppression de l'école échouerait sur la
            // clé étrangère — après avoir déjà tout supprimé du reste.
            await _context.DaaraEventPhotos.IgnoreQueryFilters()
                .Where(p => p.DaaraEvent.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.DaaraEvents.IgnoreQueryFilters()
                .Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            // Objectifs APRÈS les événements : un événement porte une clé
            // étrangère vers son objectif.
            await _context.DaaraObjectiveSteps.IgnoreQueryFilters()
                .Where(s => s.DaaraObjective.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.DaaraObjectives.IgnoreQueryFilters()
                .Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.TimetableSlots.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.ClassSubjectTeachers.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.StudentDocuments.Where(d => d.Student.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.StudentGuardians.Where(sg => sg.Student.SchoolId == id).ExecuteDeleteAsync(ct);

            // 3) Entités cœur
            await _context.Students.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);   // avant Classes (Student.ClassId)
            await _context.AcademicPeriods.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct); // avant AcademicYears
            await _context.AcademicYears.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.Subjects.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.Classes.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);

            // 4) Utilisateurs : logs de notif (audit) + sessions + tokens push puis comptes.
            // NotificationLogs n'a pas de FK → nettoyage explicite (avant de perdre
            // les UserId), pour ne pas laisser d'orphelins d'une école supprimée.
            await _context.NotificationLogs
                .Where(n => n.UserId != null
                            && (_context.Users.Any(u => u.Id == n.UserId && u.SchoolId == id)
                                || guardianIds.Contains(n.UserId.Value)))
                .ExecuteDeleteAsync(ct);
            await _context.RefreshTokens.Where(t => t.User!.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.PushDeviceTokens.Where(t => t.User.SchoolId == id).ExecuteDeleteAsync(ct);
            if (guardianIds.Count > 0)
            {
                await _context.RefreshTokens.Where(t => guardianIds.Contains(t.UserId)).ExecuteDeleteAsync(ct);
                await _context.PushDeviceTokens.Where(t => guardianIds.Contains(t.UserId)).ExecuteDeleteAsync(ct);
            }

            await _context.Users.Where(u => u.SchoolId == id).ExecuteDeleteAsync(ct); // admin/staff/teacher

            // Guardians (SchoolId null) : supprimés s'ils ne référencent plus rien
            // (liens et paiements de cette école déjà effacés ci-dessus).
            if (guardianIds.Count > 0)
            {
                await _context.Users
                    .Where(u => guardianIds.Contains(u.Id)
                                && !_context.StudentGuardians.Any(sg => sg.GuardianId == u.Id)
                                && !_context.Payments.Any(p => p.GuardianId == u.Id))
                    .ExecuteDeleteAsync(ct);
            }

            // 5) Vitrine publique : le daara disparaît de « Ils nous font confiance ».
            // La FK est déjà en ON DELETE CASCADE, mais on le fait explicitement pour
            // rester sur la discipline de ce cascade (toute entité rattachée y figure).
            await _context.TrustedSchools.Where(t => t.SchoolId == id).ExecuteDeleteAsync(ct);

            // 6) L'école elle-même
            await _context.Schools.Where(s => s.Id == id).ExecuteDeleteAsync(ct);

            await tx.CommitAsync(ct);
        }

        /// <summary>
        /// Résumé actionnable de l'accueil : qui n'a pas payé, si la présence
        /// du jour est faite, ce qu'il y a en caisse et sur le compte.
        /// </summary>
        /// <remarks>
        /// UN seul appel plutôt que trois : sur un forfait sénégalais, trois
        /// allers-retours coûtent plus d'une seconde, et trois réponses
        /// arrivées séparément peuvent se contredire à l'écran.
        ///
        /// ⚠️ Réponse porteuse de SOLDES : jamais de mise en cache hors ligne
        /// côté application.
        /// </remarks>
        [HttpGet("home-summary")]
        // Ce résumé porte des SOLDES : réservé à la direction et au personnel.
        // Le contrôleur n'exige que `[Authorize]`, or un enseignant ou un
        // surveillant a lui aussi un SchoolId — sans cet attribut, il lirait la
        // caisse et le compte du daara. Le SchoolViewer passe par ses claims de
        // rôle secondaires (§150).
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<SchoolHomeSummaryDto>> GetHomeSummary(
            CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var now = DateTime.UtcNow;
            var today = now.Date;
            var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            // Effectif du mois. EnrolledDuring et non Enrolled : un élève parti
            // en cours de mois devait quand même sa mensualité — c'est la même
            // règle que le suivi des paiements, et deux règles différentes pour
            // le même chiffre finiraient par se contredire à l'écran.
            // Nom et classe remontés avec l'identifiant : ils servent à NOMMER
            // les élèves concernés et les classes non pointées. Une seule
            // requête au lieu de trois — c'est le principe de cet endpoint.
            var enrolledRows = await _context.Students
                .Where(s => s.SchoolId == schoolId.Value)
                .EnrolledDuring(periodStart)
                .Select(s => new
                {
                    s.Id,
                    s.ClassId,
                    Name = (s.FirstName + " " + s.LastName).Trim()
                })
                .ToListAsync(ct);
            var enrolledIds = enrolledRows.Select(r => r.Id).ToList();

            // MENSUALITÉS seulement (comme le suivi des paiements, §158) : une
            // facture d'inscription impayée ne doit pas faire apparaître un
            // élève « en retard sur son mois ».
            var invoices = await _context.Invoices
                .Where(i => i.SchoolId == schoolId.Value
                            && i.PeriodStart == periodStart
                            && i.Type == InvoiceType.MonthlyFee
                            && i.Status != InvoiceStatus.Cancelled)
                .Select(i => new { i.StudentId, i.AmountDueFcfa, i.AmountPaidFcfa, i.DueDate })
                .ToListAsync(ct);

            var enrolled = enrolledIds.ToHashSet();
            var nameById = enrolledRows.ToDictionary(r => r.Id, r => r.Name);
            var overdue = 0;
            var pending = 0;
            var overdueNames = new List<string>();
            foreach (var i in invoices)
            {
                if (!enrolled.Contains(i.StudentId)) continue;
                if (i.AmountPaidFcfa >= i.AmountDueFcfa) continue;
                if (i.DueDate.Date < today)
                {
                    overdue++;
                    // Échantillon : le total, lui, continue de s'incrémenter.
                    if (overdueNames.Count < SchoolHomeSummaryDto.NameSampleSize
                        && nameById.TryGetValue(i.StudentId, out var n)
                        && !string.IsNullOrWhiteSpace(n))
                        overdueNames.Add(n);
                }
                else pending++;
            }

            // Pointage du jour : on compte les élèves pointés, pas les lignes —
            // « 42 sur 87 » est ce qu'on veut pouvoir dire quand le pointage
            // est commencé sans être fini.
            var attendedIds = await _context.Attendances
                .Where(a => a.SchoolId == schoolId.Value && a.Date == today && !a.IsDeleted)
                .Select(a => a.StudentId)
                .Distinct()
                .ToListAsync(ct);
            var attendanceCount = attendedIds.Count;

            // OÙ reste-t-il à pointer ? « La présence n'est pas pointée » fait
            // ouvrir l'écran pour découvrir quelle classe manque ; nommer la
            // classe évite ce détour.
            //
            // ⚠️ Un élève SANS classe ne donne aucun nom (il n'y en a pas à
            // donner) : c'est le décompte « 42 sur 87 » qui empêche cette liste
            // de mentir par omission.
            var attended = attendedIds.ToHashSet();
            var pendingClassIds = enrolledRows
                .Where(r => r.ClassId != null && !attended.Contains(r.Id))
                .Select(r => r.ClassId!.Value)
                .Distinct()
                .ToList();
            var classesWithoutAttendance = pendingClassIds.Count == 0
                ? new List<string>()
                : await _context.Classes
                    .Where(c => pendingClassIds.Contains(c.Id))
                    .OrderBy(c => c.Name)
                    .Select(c => c.Name)
                    .Take(SchoolHomeSummaryDto.NameSampleSize)
                    .ToListAsync(ct);

            var cashIncome = await _context.CashLedgerEntries
                .Where(e => e.SchoolId == schoolId.Value && !e.IsDeleted
                            && e.Type == CashEntryType.Income)
                .SumAsync(e => (long?)e.AmountFcfa, ct) ?? 0;
            var cashExpense = await _context.CashLedgerEntries
                .Where(e => e.SchoolId == schoolId.Value && !e.IsDeleted
                            && e.Type == CashEntryType.Expense)
                .SumAsync(e => (long?)e.AmountFcfa, ct) ?? 0;

            var onlineBalance = await _context.SchoolWallets
                .Where(w => w.SchoolId == schoolId.Value)
                .Select(w => (long?)w.AvailableBalance)
                .FirstOrDefaultAsync(ct) ?? 0;

            // Parcours de démarrage : un daara qui vient d'être validé n'a rien,
            // et c'est le pire moment de tout le parcours. Ces quatre drapeaux
            // permettent de lui dire par où commencer.
            var hasClasses = await _context.Classes
                .AnyAsync(c => c.SchoolId == schoolId.Value && !c.IsDeleted, ct);
            var settings = await _context.SchoolPaymentSettings
                .Where(p => p.SchoolId == schoolId.Value)
                .Select(p => new
                {
                    p.GeneralMonthlyFeeFcfa,
                    p.BoardingMonthlyFeeFcfa,
                    p.HalfBoardingMonthlyFeeFcfa,
                    p.DayMonthlyFeeFcfa
                })
                .FirstOrDefaultAsync(ct);
            var hasClassFee = await _context.ClassFees
                .AnyAsync(f => f.SchoolId == schoolId.Value && f.AmountFcfa > 0, ct);
            var hasFees = hasClassFee
                || (settings != null && (
                       settings.GeneralMonthlyFeeFcfa > 0
                    || settings.BoardingMonthlyFeeFcfa > 0
                    || settings.HalfBoardingMonthlyFeeFcfa > 0
                    || settings.DayMonthlyFeeFcfa > 0));
            var hasInvitedUsers = await _context.Users
                .AnyAsync(u => u.SchoolId == schoolId.Value && !u.IsDeleted
                               && u.Role != UserRoles.SchoolAdmin, ct);

            var hasSubjects = await _context.Subjects
                .AnyAsync(s => s.SchoolId == schoolId.Value && !s.IsDeleted && s.IsActive, ct);

            // ⚠️ Une année scolaire SANS période ne sert à rien : ni note ni
            // bulletin ne peuvent s'y accrocher. L'étape n'est donc « faite »
            // que quand l'année porte au moins une période — sinon le daara
            // cocherait la ligne et resterait bloqué au même endroit.
            var hasAcademicPeriod = await _context.AcademicPeriods
                .AnyAsync(p => p.SchoolId == schoolId.Value, ct);

            // ⚠️ Mais une année CRÉÉE sans période doit se voir. Le daara Jazbul
            // xuloob avait fait exactement cela (vérifié en production le
            // 2026-08-23 : 1 année, 0 période) : l'accueil lui répétait
            // « Ouvrir l'année scolaire » alors qu'il l'avait ouverte, sans
            // jamais lui dire que les périodes manquaient encore. L'étape reste
            // à faire — c'est son libellé qui doit le dire.
            var hasAcademicYear = hasAcademicPeriod
                || await _context.AcademicYears
                    .AnyAsync(y => y.SchoolId == schoolId.Value, ct);

            var hasAssignments = await _context.ClassSubjectTeachers
                .AnyAsync(a => a.SchoolId == schoolId.Value, ct);

            var hasTimetable = await _context.TimetableSlots
                .AnyAsync(t => t.SchoolId == schoolId.Value, ct);

            // Enseignants sans aucune classe : ils ouvrent leur espace sur un
            // écran vide dont eux ne peuvent rien faire.
            var teachersWithoutClassQuery = _context.Users
                .Where(u => u.SchoolId == schoolId.Value && !u.IsDeleted
                            && u.Role == UserRoles.Teacher
                            && !_context.ClassSubjectTeachers
                                  .Any(a => a.TeacherId == u.Id));
            var teachersWithoutClass = await teachersWithoutClassQuery.CountAsync(ct);
            // Nommer plutôt que compter : « Ousmane Fall n'a aucune classe » se
            // règle sans quitter l'accueil, « un enseignant » oblige à chercher.
            var teachersWithoutClassNames = teachersWithoutClass == 0
                ? new List<string>()
                // Composé EN MÉMOIRE : un nom d'affichage se construit à partir
                // de trois champs dont deux peuvent être vides, et le faire en
                // SQL rendrait la nullabilité illisible pour un gain nul sur
                // cinq lignes.
                : (await teachersWithoutClassQuery
                        .OrderBy(u => u.Id)
                        .Select(u => new { u.FirstName, u.LastName, u.FullName })
                        .Take(SchoolHomeSummaryDto.NameSampleSize)
                        .ToListAsync(ct))
                    .Select(u =>
                    {
                        var composed = $"{u.FirstName} {u.LastName}".Trim();
                        return string.IsNullOrWhiteSpace(composed)
                            ? (u.FullName ?? string.Empty)
                            : composed;
                    })
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();

            var dismissed = await _context.SchoolSetupDismissals
                .Where(d => d.SchoolId == schoolId.Value)
                .Select(d => d.Step)
                .ToListAsync(ct);

            var setupSteps = BuildSetupSteps(
                hasClasses: hasClasses,
                hasSubjects: hasSubjects,
                hasAcademicPeriod: hasAcademicPeriod,
                hasAcademicYear: hasAcademicYear,
                hasStudents: enrolledIds.Count > 0,
                hasFees: hasFees,
                hasInvitedUsers: hasInvitedUsers,
                hasAssignments: hasAssignments,
                hasTimetable: hasTimetable,
                dismissed: dismissed);

            return Ok(new SchoolHomeSummaryDto
            {
                SetupSteps = setupSteps,
                TeachersWithoutClass = teachersWithoutClass,
                TeachersWithoutClassNames = teachersWithoutClassNames,
                OverdueStudentNames = overdueNames,
                ClassesWithoutAttendance = classesWithoutAttendance,
                PeriodStart = periodStart,
                OverdueStudents = overdue,
                PendingStudents = pending,
                EnrolledStudents = enrolledIds.Count,
                AttendanceTakenToday = attendanceCount > 0,
                AttendanceCountToday = attendanceCount,
                CashBalanceFcfa = cashIncome - cashExpense,
                OnlineBalanceFcfa = onlineBalance,
                HasClasses = hasClasses,
                HasStudents = enrolledIds.Count > 0,
                HasFees = hasFees,
                HasInvitedUsers = hasInvitedUsers
            });
        }

        /// <summary>
        /// Les étapes que l'application ne peut pas faire fonctionner sans
        /// elles. Elles occupent l'accueil, et ne peuvent pas être écartées :
        /// un daara sans classe, sans élève ou sans tarif n'a rien à faire
        /// tourner, et l'écarter reviendrait à couper le guidage au moment où
        /// il est le plus nécessaire.
        /// </summary>
        public static readonly IReadOnlySet<SchoolSetupStep> BlockingSetupSteps =
            new HashSet<SchoolSetupStep>
            {
                SchoolSetupStep.Classes,
                SchoolSetupStep.Students,
                SchoolSetupStep.Fees,
            };

        /// <summary>
        /// Assemble le parcours de démarrage : ordre, « fait », « écarté »,
        /// « bloquant ».
        /// </summary>
        /// <remarks>
        /// <b>Publique et PURE à dessein</b> : c'est ici que vivent les seules
        /// règles qui comptent, et une règle qu'on ne peut vérifier qu'en
        /// montant une base entière n'est jamais vérifiée (§133).
        ///
        /// <para>⚠️ Une étape bloquante n'est <b>jamais</b> rendue comme
        /// écartée, même si une ligne traînait en base : le drapeau est calculé
        /// ici, pas recopié. Sans cette garde, une ligne posée par erreur (ou
        /// par une future migration) ferait disparaître en silence le guidage
        /// d'un daara qui n'a pas encore de classe.</para>
        /// </remarks>
        public static List<SchoolSetupStepDto> BuildSetupSteps(
            bool hasClasses,
            bool hasSubjects,
            bool hasAcademicPeriod,
            bool hasStudents,
            bool hasFees,
            bool hasInvitedUsers,
            bool hasAssignments,
            bool hasTimetable,
            IEnumerable<SchoolSetupStep> dismissed,
            bool hasAcademicYear = false)
        {
            var skipped = dismissed.ToHashSet();

            // L'ordre de cette table EST l'ordre affiché : il suit les
            // dépendances réelles (pas d'affectation sans classe, sans matière
            // et sans compte ; pas de note sans période).
            //
            // `Started` = « commencé, pas terminé ». Seule l'année scolaire en
            // a besoin aujourd'hui : c'est la seule étape qui demande DEUX
            // gestes (ouvrir l'année, puis y découper les périodes), et un daara
            // qui n'avait fait que le premier lisait un accueil qui lui
            // réclamait ce qu'il venait de faire.
            var done = new (SchoolSetupStep Step, bool Done, bool Started)[]
            {
                (SchoolSetupStep.Classes, hasClasses, false),
                (SchoolSetupStep.Subjects, hasSubjects, false),
                (SchoolSetupStep.AcademicYear, hasAcademicPeriod, hasAcademicYear),
                (SchoolSetupStep.Students, hasStudents, false),
                (SchoolSetupStep.Fees, hasFees, false),
                (SchoolSetupStep.Users, hasInvitedUsers, false),
                (SchoolSetupStep.Assignments, hasAssignments, false),
                (SchoolSetupStep.Timetable, hasTimetable, false),
            };

            return done.Select(d =>
            {
                var blocking = BlockingSetupSteps.Contains(d.Step);
                return new SchoolSetupStepDto
                {
                    Step = d.Step,
                    Done = d.Done,
                    Blocking = blocking,
                    Dismissed = !blocking && skipped.Contains(d.Step),
                    // Une étape terminée n'est pas « en cours » : sans cette
                    // garde, l'année scolaire complète porterait les deux
                    // drapeaux et un futur affichage pourrait s'y tromper.
                    Started = !d.Done && d.Started,
                };
            }).ToList();
        }

        /// <summary>
        /// `POST /api/school/setup/{step}/dismiss` — « Pas pour mon daara ».
        /// </summary>
        /// <remarks>
        /// Réservé au SchoolAdmin : c'est une décision sur la configuration de
        /// l'établissement, pas un réglage d'écran.
        /// </remarks>
        [HttpPost("setup/{step}/dismiss")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<IActionResult> DismissSetupStep(
            SchoolSetupStep step, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            if (!Enum.IsDefined(step))
                return BadRequest(ApiResponse<object>.Fail("Étape inconnue."));

            if (BlockingSetupSteps.Contains(step))
                return BadRequest(ApiResponse<object>.Fail(
                    "Cette étape est indispensable au fonctionnement du daara : "
                    + "elle ne peut pas être écartée."));

            var already = await _context.SchoolSetupDismissals
                .AnyAsync(d => d.SchoolId == schoolId.Value && d.Step == step, ct);
            if (already) return NoContent();

            // Nom complet : `Idara.API.Models` n'est pas importé ici (le type
            // `School` entrerait en collision avec le namespace des DTO).
            _context.SchoolSetupDismissals.Add(new Idara.API.Models.SchoolSetupDismissal
            {
                SchoolId = schoolId.Value,
                Step = step,
                DismissedAt = DateTime.UtcNow,
                DismissedById = User.GetUserId(),
            });

            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Deux appuis rapides : l'unique a fait son travail, l'étape
                // est écartée. Rien à signaler à l'utilisateur.
            }
            return NoContent();
        }

        /// <summary>
        /// `DELETE /api/school/setup/{step}/dismiss` — remettre l'étape dans la
        /// liste. Écarter n'est jamais définitif.
        /// </summary>
        [HttpDelete("setup/{step}/dismiss")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<IActionResult> RestoreSetupStep(
            SchoolSetupStep step, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var row = await _context.SchoolSetupDismissals
                .FirstOrDefaultAsync(d => d.SchoolId == schoolId.Value && d.Step == step, ct);
            if (row != null)
            {
                _context.SchoolSetupDismissals.Remove(row);
                await _context.SaveChangesAsync(ct);
            }
            return NoContent();
        }

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505";

        [HttpGet("stats")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<SchoolStatsDto>> GetStats()
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            // Enrolled() : l'effectif affiché = l'effectif facturé (même
            // périmètre que le palier d'abonnement).
            var totalStudents = await _context.Students
                .Where(s => s.SchoolId == schoolId.Value).Enrolled()
                .CountAsync();
            var totalTeachers = await _context.Users
                .CountAsync(u => u.SchoolId == schoolId.Value && u.Role == UserRoles.Teacher && !u.IsDeleted);
            // Parents de l'EFFECTIF (élèves supprimés — oubli préexistant corrigé
            // le 2026-08-17 — et sortis exclus). Périmètre Enrolled écrit en ligne :
            // l'extension ne s'applique qu'à IQueryable<Student>.
            var today = DateTime.UtcNow.Date;
            var totalGuardians = await _context.StudentGuardians
                .Where(sg => sg.Student.SchoolId == schoolId.Value
                             && !sg.Student.IsDeleted
                             && (sg.Student.ExitDate == null || sg.Student.ExitDate > today))
                .Select(sg => sg.GuardianId)
                .Distinct()
                .CountAsync();
            var totalClasses = await _context.Classes
                .CountAsync(c => c.SchoolId == schoolId.Value && !c.IsDeleted);
            var totalSubjects = await _context.Subjects
                .CountAsync(s => s.SchoolId == schoolId.Value && !s.IsDeleted);
            var currentYear = await _context.AcademicYears
                .Where(y => y.SchoolId == schoolId.Value && y.IsCurrent)
                .Select(y => y.Name)
                .FirstOrDefaultAsync();
            var currentPeriod = await _context.AcademicPeriods
                .Where(p => p.SchoolId == schoolId.Value && p.IsCurrent)
                .Select(p => p.Name)
                .FirstOrDefaultAsync();

            return Ok(new SchoolStatsDto
            {
                TotalStudents = totalStudents,
                TotalTeachers = totalTeachers,
                TotalGuardians = totalGuardians,
                TotalClasses = totalClasses,
                TotalSubjects = totalSubjects,
                CurrentAcademicYearName = currentYear,
                CurrentAcademicPeriodName = currentPeriod
            });
        }

        // ================================================================
        // ===== Gestion des utilisateurs côté école (SchoolAdmin) =====
        // ================================================================

        /// <summary>
        /// Liste les utilisateurs rattachés à mon école : enseignants + personnel
        /// (SchoolId) ET parents liés via leurs enfants. Pour les parents,
        /// renvoie la liste des enfants liés (pour la dissociation ciblée).
        /// </summary>
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        [HttpGet("users")]
        public async Task<ActionResult<IEnumerable<SchoolUserDto>>> GetSchoolUsers(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();
            return Ok(await BuildSchoolUsersAsync(schoolId.Value, ct));
        }

        /// <summary>
        /// Liste les utilisateurs d'une école donnée (SuperAdmin). Même contenu
        /// que <see cref="GetSchoolUsers"/> mais scopé par le paramètre
        /// <paramref name="schoolId"/> au lieu du JWT (le SuperAdmin a l'autorité
        /// globale, il n'est rattaché à aucune école).
        /// </summary>
        [Authorize(Roles = UserRoles.SuperAdmin)]
        [HttpGet("{schoolId:int}/users")]
        public async Task<ActionResult<IEnumerable<SchoolUserDto>>> GetSchoolUsersBySuperAdmin(
            int schoolId, CancellationToken ct)
        {
            var exists = await _context.Schools.AnyAsync(s => s.Id == schoolId, ct);
            if (!exists) return NotFound(ApiResponse<bool>.Fail("École non trouvée."));
            return Ok(await BuildSchoolUsersAsync(schoolId, ct));
        }

        /// <summary>
        /// Construit la liste des utilisateurs d'une école : enseignants +
        /// personnel + admin (via SchoolId) ET parents (via StudentGuardian →
        /// élèves de l'école, dédupliqués, avec enfants liés).
        /// </summary>
        private async Task<List<SchoolUserDto>> BuildSchoolUsersAsync(int schoolId, CancellationToken ct)
        {
            // Enseignants + personnel + admin (SchoolId direct, hors supprimés).
            var staff = await _context.Users
                .Where(u => u.SchoolId == schoolId && !u.IsDeleted)
                .Select(u => new SchoolUserDto
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    Role = u.Role,
                    AccountStatus = u.AccountStatus,
                    PhoneNumber = u.PhoneNumber,
                    CanLogin = u.CanLogin,
                    JobTitle = u.JobTitle,
                    CreatedAt = u.CreatedAt,
                    LastLoginAt = u.LastLoginAt
                })
                .ToListAsync(ct);

            // Parents liés via leurs enfants de cette école.
            var guardianLinks = await _context.StudentGuardians
                .Where(sg => sg.Student.SchoolId == schoolId && !sg.Guardian.IsDeleted && !sg.Student.IsDeleted)
                .Select(sg => new
                {
                    sg.GuardianId,
                    sg.Guardian.FullName,
                    sg.Guardian.Email,
                    sg.Guardian.AccountStatus,
                    sg.Guardian.PhoneNumber,
                    sg.Guardian.CreatedAt,
                    sg.Guardian.LastLoginAt,
                    sg.StudentId,
                    StudentName = (sg.Student.FirstName ?? "") + " " + (sg.Student.LastName ?? ""),
                    sg.Relationship
                })
                .ToListAsync(ct);

            var guardians = guardianLinks
                .GroupBy(g => g.GuardianId)
                .Select(grp =>
                {
                    var first = grp.First();
                    return new SchoolUserDto
                    {
                        Id = grp.Key,
                        FullName = first.FullName,
                        Email = first.Email,
                        Role = UserRoles.Guardian,
                        AccountStatus = first.AccountStatus,
                        PhoneNumber = first.PhoneNumber,
                        CreatedAt = first.CreatedAt,
                        LastLoginAt = first.LastLoginAt,
                        Children = grp.Select(x => new SchoolUserChildDto
                        {
                            StudentId = x.StudentId,
                            StudentName = x.StudentName.Trim(),
                            Relationship = x.Relationship
                        }).ToList()
                    };
                })
                .ToList();

            return staff.Concat(guardians).OrderBy(u => u.Role).ThenBy(u => u.FullName).ToList();
        }

        /// <summary>
        /// Supprime un utilisateur de mon école (SchoolAdmin only). Suppression
        /// hybride (hard-delete si aucun historique, sinon anonymisation), scopée
        /// strictement à l'école : on ne peut toucher qu'un Teacher/SchoolStaff
        /// de SON école, ou un parent lié à un de SES élèves. Interdit : se
        /// supprimer soi-même, supprimer un SchoolAdmin, supprimer un SuperAdmin.
        /// </summary>
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        [HttpDelete("users/{userId}")]
        public async Task<ActionResult<ApiResponse<UserDeletionResultDto>>> DeleteSchoolUser(
            int userId, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var currentUserId = User.GetUserId();
            if (schoolId == null || currentUserId == null) return Unauthorized();

            if (userId == currentUserId.Value)
                return BadRequest(ApiResponse<UserDeletionResultDto>.Fail(
                    "Vous ne pouvez pas supprimer votre propre compte."));

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
            if (user == null)
                return NotFound(ApiResponse<UserDeletionResultDto>.Fail("Utilisateur introuvable."));

            if (user.Role == UserRoles.SuperAdmin || user.Role == UserRoles.SchoolAdmin)
                return BadRequest(ApiResponse<UserDeletionResultDto>.Fail(
                    "Impossible de supprimer un administrateur depuis cet espace."));

            // Scoping strict : l'utilisateur doit appartenir à mon école.
            // ⚠️ VOLONTAIREMENT sans !Student.IsDeleted ni filtre « sorti »
            // (décision 2026-08-17) : un flux de SUPPRESSION doit voir tous les
            // liens — filtrer rendrait insupprimable un parent dont le seul
            // enfant a été supprimé (orphelin permanent). Même logique que la
            // cascade d'école (§77).
            var belongsToSchool = user.Role == UserRoles.Guardian
                ? await _context.StudentGuardians.AnyAsync(
                    sg => sg.GuardianId == userId && sg.Student.SchoolId == schoolId.Value, ct)
                : user.SchoolId == schoolId.Value;

            if (!belongsToSchool)
                return BadRequest(ApiResponse<UserDeletionResultDto>.Fail(
                    "Cet utilisateur n'appartient pas à votre école."));

            // Cloisonnement multi-tenant : si le parent est AUSSI lié à des élèves
            // d'une AUTRE école, on ne touche PAS au compte global (le service
            // hybride retirerait tous ses liens + anonymiserait le compte, ce qui
            // affecterait l'autre école). On se contente de le dissocier de NOS
            // élèves. Le SuperAdmin, lui, garde l'autorité globale (UsersController).
            if (user.Role == UserRoles.Guardian)
            {
                var hasOtherSchoolLinks = await _context.StudentGuardians.AnyAsync(
                    sg => sg.GuardianId == userId && sg.Student.SchoolId != schoolId.Value, ct);
                if (hasOtherSchoolLinks)
                {
                    var myLinks = _context.StudentGuardians.Where(
                        sg => sg.GuardianId == userId && sg.Student.SchoolId == schoolId.Value);
                    _context.StudentGuardians.RemoveRange(myLinks);
                    await _context.SaveChangesAsync(ct);

                    _logger.LogInformation(
                        "[school] Parent {UserId} retiré de l'école {SchoolId} par dissociation (lié à une autre école, compte conservé)",
                        userId, schoolId);

                    return Ok(ApiResponse<UserDeletionResultDto>.Ok(
                        new UserDeletionResultDto
                        {
                            UserId = userId,
                            Action = "Unlinked",
                            Message = "Parent retiré de votre école (compte conservé car lié à une autre école)."
                        },
                        "Parent retiré de votre école (compte conservé car lié à une autre école)."));
                }
            }

            var result = await _userDeletion.DeleteOrAnonymizeAsync(user, currentUserId.Value, ct);
            _logger.LogInformation(
                "[school] User {UserId} ({Role}) retiré de l'école {SchoolId} par SchoolAdmin {AdminId} → {Action}",
                userId, user.Role, schoolId, currentUserId, result.Action);
            return Ok(ApiResponse<UserDeletionResultDto>.Ok(result, result.Message));
        }

        /// <summary>
        /// Dissocie un parent d'un élève (retire le lien StudentGuardian) sans
        /// supprimer le compte parent. SchoolAdmin only, scopé à l'école.
        /// </summary>
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        [HttpDelete("students/{studentId}/guardians/{guardianId}")]
        public async Task<ActionResult<ApiResponse<bool>>> UnlinkGuardian(
            int studentId, int guardianId, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            // L'élève doit appartenir à mon école.
            var studentOk = await _context.Students.AnyAsync(
                s => s.Id == studentId && s.SchoolId == schoolId.Value && !s.IsDeleted, ct);
            if (!studentOk)
                return NotFound(ApiResponse<bool>.Fail("Élève introuvable dans votre école."));

            var link = await _context.StudentGuardians.FirstOrDefaultAsync(
                sg => sg.StudentId == studentId && sg.GuardianId == guardianId, ct);
            if (link == null)
                return NotFound(ApiResponse<bool>.Fail("Lien parent-élève introuvable."));

            _context.StudentGuardians.Remove(link);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[school] Parent {GuardianId} dissocié de l'élève {StudentId} (école {SchoolId})",
                guardianId, studentId, schoolId);
            return Ok(ApiResponse<bool>.Ok(true, "Parent dissocié de l'élève."));
        }

        private static SchoolInfoResponse MapToSchoolInfoResponse(SchoolModel school) => new()
        {
            Id = school.Id,
            Name = school.Name ?? string.Empty,
            NameAr = school.NameAr,
            Address = school.Address ?? string.Empty,
            PhoneNumber = school.PhoneNumber ?? string.Empty,
            LegalDocumentsUrls = school.LegalDocumentsUrl?
                .Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new(),
            KycStatus = school.KycStatus,
            RejectionReason = school.RejectionReason,
            SubmittedAt = school.SubmittedAt,
            ValidatedAt = school.ValidatedAt,
            RepresentativeFirstName = school.RepresentativeFirstName ?? string.Empty,
            RepresentativeLastName = school.RepresentativeLastName ?? string.Empty,
            RepresentativePhone = school.RepresentativePhone ?? string.Empty,
            RepresentativeIdDocumentUrls = school.RepresentativeIdDocumentUrl?
                .Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new(),
            Type = school.Type,
            QuranRiwaya = school.QuranRiwaya,
            Users = school.Users.Where(u => !u.IsDeleted).Select(u => new UserInfoDto
            {
                Id = u.Id,
                Email = u.Email,
                FullName = u.FullName,
                FirstName = u.FirstName,
                LastName = u.LastName,
                PhoneNumber = u.PhoneNumber,
                Role = u.Role,
                AccountStatus = u.AccountStatus,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt
            }).ToList()
        };
    }
}
