using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.TrustedSchool;
using Idara.API.Enums;
using Idara.API.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Écoles mises en avant (« Ils nous font confiance ») de la landing page.
    /// Lecture publique, CRUD SuperAdmin.
    ///
    /// Un partenaire peut être <b>rattaché</b> à un daara Idara : son nom (FR/AR) et
    /// son logo sont alors résolus À LA LECTURE depuis la fiche du daara, jamais
    /// recopiés — sinon la landing figerait une identité que le daara a changée
    /// depuis. Les colonnes locales ne servent que de repli.
    /// </summary>
    [ApiController]
    [Route("api/trusted-schools")]
    public class TrustedSchoolsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly UploadSettings _uploads;
        private readonly ILogger<TrustedSchoolsController> _logger;

        public TrustedSchoolsController(
            AppDbContext context,
            IWebHostEnvironment env,
            IOptions<UploadSettings> uploads,
            ILogger<TrustedSchoolsController> logger)
        {
            _context = context;
            _env = env;
            _uploads = uploads.Value;
            _logger = logger;
        }

        /// <summary>Liste publique : écoles actives, triées.</summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<IEnumerable<TrustedSchoolDto>>> GetPublic(CancellationToken ct)
        {
            var items = await Project(_context.TrustedSchools.Where(t => t.IsActive))
                .ToListAsync(ct);
            return Ok(items);
        }

        /// <summary>Liste complète (SuperAdmin), inclut les inactives.</summary>
        [HttpGet("all")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<IEnumerable<TrustedSchoolDto>>> GetAll(CancellationToken ct)
        {
            var items = await Project(_context.TrustedSchools).ToListAsync(ct);
            return Ok(items);
        }

        /// <summary>
        /// Daara Idara ajoutables en un appui : validés et pas déjà partenaires.
        /// Leur nom et leur logo sont déjà en base — le SuperAdmin n'a rien à ressaisir.
        /// </summary>
        [HttpGet("candidates")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<IEnumerable<TrustedSchoolCandidateDto>>> GetCandidates(
            CancellationToken ct)
        {
            var items = await ProjectCandidates(_context).ToListAsync(ct);
            return Ok(items);
        }

        [HttpPost]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<ApiResponse<TrustedSchoolDto>>> Create(
            [FromBody] CreateTrustedSchoolDto dto, CancellationToken ct)
        {
            var entity = new Models.TrustedSchool
            {
                DisplayOrder = dto.DisplayOrder,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            Models.School? school = null;

            if (dto.SchoolId.HasValue)
            {
                school = await _context.Schools
                    .FirstOrDefaultAsync(s => s.Id == dto.SchoolId.Value, ct);

                if (school == null)
                    return NotFound(ApiResponse<TrustedSchoolDto>.Fail("Daara introuvable."));

                if (school.KycStatus != KycStatus.Validated)
                    return BadRequest(ApiResponse<TrustedSchoolDto>.Fail(
                        "Seul un daara validé peut figurer parmi les partenaires."));

                var already = await _context.TrustedSchools
                    .AnyAsync(t => t.SchoolId == school.Id, ct);
                if (already)
                    return BadRequest(ApiResponse<TrustedSchoolDto>.Fail(
                        "Ce daara figure déjà parmi les partenaires."));

                entity.SchoolId = school.Id;
                // On ne recopie NI le nom NI le logo : ils sont lus sur la fiche du
                // daara à chaque affichage. Les colonnes locales restent vides et ne
                // serviraient de repli que si sa fiche devenait incomplète.
                entity.Name = string.Empty;
            }
            else
            {
                entity.Name = dto.Name!.Trim();
                entity.NameAr = NullIfBlank(dto.NameAr);

                if (!string.IsNullOrWhiteSpace(dto.LogoBase64))
                {
                    var saved = await SaveLogoAsync(dto.LogoBase64);
                    if (saved == null)
                        return BadRequest(ApiResponse<TrustedSchoolDto>.Fail(
                            $"Logo invalide (formats : JPEG/PNG/WEBP, max {_uploads.MaxPhotoSizeMb} Mo)."));
                    entity.LogoUrl = saved;
                }
            }

            _context.TrustedSchools.Add(entity);
            await _context.SaveChangesAsync(ct);

            return Ok(ApiResponse<TrustedSchoolDto>.Ok(ToDto(entity, school), "École ajoutée."));
        }

        [HttpPut("{id}")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<ApiResponse<TrustedSchoolDto>>> Update(
            int id, [FromBody] UpdateTrustedSchoolDto dto, CancellationToken ct)
        {
            var entity = await _context.TrustedSchools
                .Include(t => t.School)
                .FirstOrDefaultAsync(t => t.Id == id, ct);
            if (entity == null) return NotFound(ApiResponse<TrustedSchoolDto>.Fail("École introuvable."));

            if (dto.DisplayOrder.HasValue) entity.DisplayOrder = dto.DisplayOrder.Value;
            if (dto.IsActive.HasValue) entity.IsActive = dto.IsActive.Value;

            // Sur un partenaire rattaché, l'identité appartient à la fiche du daara :
            // l'éditer ici créerait deux sources concurrentes pour le même nom.
            var linked = entity.SchoolId.HasValue;

            if (!linked)
            {
                if (!string.IsNullOrWhiteSpace(dto.Name)) entity.Name = dto.Name.Trim();
                if (dto.NameAr != null) entity.NameAr = NullIfBlank(dto.NameAr);

                if (!string.IsNullOrWhiteSpace(dto.LogoBase64))
                {
                    var saved = await SaveLogoAsync(dto.LogoBase64);
                    if (saved == null)
                        return BadRequest(ApiResponse<TrustedSchoolDto>.Fail(
                            $"Logo invalide (formats : JPEG/PNG/WEBP, max {_uploads.MaxPhotoSizeMb} Mo)."));
                    DeleteLogoFile(entity.LogoUrl);
                    entity.LogoUrl = saved;
                }
            }

            await _context.SaveChangesAsync(ct);

            var message = linked
                ? "Partenaire mis à jour. Le nom et le logo viennent de la fiche du daara."
                : "École mise à jour.";
            return Ok(ApiResponse<TrustedSchoolDto>.Ok(ToDto(entity, entity.School), message));
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var entity = await _context.TrustedSchools.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (entity == null) return NotFound();

            // entity.LogoUrl ne porte que le logo PROPRE au partenaire (/uploads/partners/).
            // Le logo d'un daara rattaché vit ailleurs (/uploads/school-branding/) et
            // n'est jamais stocké ici — aucun risque de le supprimer.
            DeleteLogoFile(entity.LogoUrl);
            _context.TrustedSchools.Remove(entity);
            await _context.SaveChangesAsync(ct);
            return NoContent();
        }

        // ----- Helpers -----

        /// <summary>
        /// Projection commune aux deux listes. Le <c>??</c> se traduit en COALESCE :
        /// pour un partenaire non rattaché la jointure est vide, donc la valeur locale
        /// est prise — un seul chemin pour les deux formes.
        ///
        /// <b>Publique et statique exprès</b> : une requête qu'on ne peut vérifier
        /// qu'en interrogeant une base ne se vérifie jamais. Ainsi un banc d'essai
        /// jetable peut lire le SQL réellement produit (<c>ToQueryString()</c>) et
        /// contrôler que la résolution du nom part bien en COALESCE côté serveur, au
        /// lieu de tomber en évaluation cliente. Même raisonnement que
        /// <c>EmailService.BuildIncidentAlert</c> (§133).
        /// </summary>
        public static IQueryable<TrustedSchoolDto> Project(IQueryable<Models.TrustedSchool> q) =>
            q.OrderBy(t => t.DisplayOrder).ThenBy(t => t.Id)
             .Select(t => new TrustedSchoolDto
             {
                 Id = t.Id,
                 Name = t.School!.Name ?? t.Name,
                 NameAr = t.School!.NameAr ?? t.NameAr,
                 LogoUrl = t.School!.LogoUrl ?? t.LogoUrl,
                 DisplayOrder = t.DisplayOrder,
                 IsActive = t.IsActive,
                 SchoolId = t.SchoolId,
                 IsLinked = t.SchoolId != null
             });

        /// <summary>
        /// Daara ajoutables : validés et pas déjà partenaires. Publique et statique
        /// pour la même raison que <see cref="Project"/>.
        /// </summary>
        public static IQueryable<TrustedSchoolCandidateDto> ProjectCandidates(AppDbContext db)
        {
            var alreadyPartner = db.TrustedSchools
                .Where(t => t.SchoolId != null)
                .Select(t => t.SchoolId!.Value);

            return db.Schools
                .Where(s => s.KycStatus == KycStatus.Validated && !alreadyPartner.Contains(s.Id))
                .OrderBy(s => s.Name).ThenBy(s => s.Id)
                .Select(s => new TrustedSchoolCandidateDto
                {
                    SchoolId = s.Id,
                    Name = s.Name,
                    NameAr = s.NameAr,
                    LogoUrl = s.LogoUrl,
                    // Indicatif, pour aider le SuperAdmin à reconnaître le daara.
                    // ⚠️ C2 : passera par .Enrolled() quand la notion d'élève sortant existera.
                    StudentCount = db.Students.Count(st => st.SchoolId == s.Id && !st.IsDeleted)
                });
        }

        private static TrustedSchoolDto ToDto(Models.TrustedSchool t, Models.School? school) => new()
        {
            Id = t.Id,
            Name = school?.Name ?? t.Name,
            NameAr = school?.NameAr ?? t.NameAr,
            LogoUrl = school?.LogoUrl ?? t.LogoUrl,
            DisplayOrder = t.DisplayOrder,
            IsActive = t.IsActive,
            SchoolId = t.SchoolId,
            IsLinked = t.SchoolId != null
        };

        private static string? NullIfBlank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>Décode + valide + sauvegarde le logo. Retourne l'URL relative ou null.</summary>
        private async Task<string?> SaveLogoAsync(string base64)
        {
            var decoded = FileUploadValidator.DecodeAndValidate(
                base64, _uploads.MaxPhotoSizeMb, _uploads.AllowedPhotoMimeTypes);
            if (decoded == null) return null;

            var folder = Path.Combine(_env.WebRootPath, "uploads", "partners");
            Directory.CreateDirectory(folder);
            var fileName = $"{Guid.NewGuid():N}{decoded.Extension}";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), decoded.Bytes);
            return $"/uploads/partners/{fileName}";
        }

        /// <summary>Supprime le fichier logo du disque (best-effort, défense path-traversal).</summary>
        private void DeleteLogoFile(string? logoUrl)
        {
            if (string.IsNullOrWhiteSpace(logoUrl)) return;
            try
            {
                var root = Path.GetFullPath(_env.WebRootPath);
                var full = Path.GetFullPath(Path.Combine(_env.WebRootPath, logoUrl.TrimStart('/')));
                if (full.StartsWith(root) && System.IO.File.Exists(full))
                    System.IO.File.Delete(full);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[trusted-schools] Échec suppression logo {Url} (non bloquant)", logoUrl);
            }
        }
    }
}
