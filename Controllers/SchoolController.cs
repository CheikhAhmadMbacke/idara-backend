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
            school.Name = dto.Name.Trim();
            school.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();
            school.PhoneNumber = string.IsNullOrWhiteSpace(dto.PhoneNumber) ? null : dto.PhoneNumber.Trim();
            school.RepresentativeFirstName = string.IsNullOrWhiteSpace(dto.RepresentativeFirstName) ? null : dto.RepresentativeFirstName.Trim();
            school.RepresentativeLastName = string.IsNullOrWhiteSpace(dto.RepresentativeLastName) ? null : dto.RepresentativeLastName.Trim();
            school.RepresentativePhone = string.IsNullOrWhiteSpace(dto.RepresentativePhone) ? null : dto.RepresentativePhone.Trim();
            if (dto.QuranRiwaya.HasValue) school.QuranRiwaya = dto.QuranRiwaya.Value;
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
            await _context.PaymentInvoiceAllocations.Where(a => a.Payment.SchoolId == id).ExecuteDeleteAsync(ct); // avant Payments/Invoices
            await _context.Payments.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);           // avant Invoice/Student/Guardian
            await _context.Invoices.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.StudentFeeOverrides.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.ClassFees.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.SchoolWallets.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.SchoolPaymentSettings.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.PayoutAlerts.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
            await _context.CashLedgerEntries.Where(x => x.SchoolId == id).ExecuteDeleteAsync(ct);
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

            // 5) L'école elle-même
            await _context.Schools.Where(s => s.Id == id).ExecuteDeleteAsync(ct);

            await tx.CommitAsync(ct);
        }

        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        [HttpGet("stats")]
        public async Task<ActionResult<SchoolStatsDto>> GetStats()
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var totalStudents = await _context.Students
                .CountAsync(s => s.SchoolId == schoolId.Value && !s.IsDeleted);
            var totalTeachers = await _context.Users
                .CountAsync(u => u.SchoolId == schoolId.Value && u.Role == UserRoles.Teacher && !u.IsDeleted);
            var totalGuardians = await _context.StudentGuardians
                .Where(sg => sg.Student.SchoolId == schoolId.Value)
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
