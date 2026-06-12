using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.TrustedSchool;
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
            var items = await _context.TrustedSchools
                .Where(t => t.IsActive)
                .OrderBy(t => t.DisplayOrder).ThenBy(t => t.Id)
                .Select(t => new TrustedSchoolDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    LogoUrl = t.LogoUrl,
                    DisplayOrder = t.DisplayOrder,
                    IsActive = t.IsActive
                })
                .ToListAsync(ct);
            return Ok(items);
        }

        /// <summary>Liste complète (SuperAdmin), inclut les inactives.</summary>
        [HttpGet("all")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<IEnumerable<TrustedSchoolDto>>> GetAll(CancellationToken ct)
        {
            var items = await _context.TrustedSchools
                .OrderBy(t => t.DisplayOrder).ThenBy(t => t.Id)
                .Select(t => new TrustedSchoolDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    LogoUrl = t.LogoUrl,
                    DisplayOrder = t.DisplayOrder,
                    IsActive = t.IsActive
                })
                .ToListAsync(ct);
            return Ok(items);
        }

        [HttpPost]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<ApiResponse<TrustedSchoolDto>>> Create(
            [FromBody] CreateTrustedSchoolDto dto, CancellationToken ct)
        {
            var entity = new Models.TrustedSchool
            {
                Name = dto.Name.Trim(),
                DisplayOrder = dto.DisplayOrder,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            if (!string.IsNullOrWhiteSpace(dto.LogoBase64))
            {
                var saved = await SaveLogoAsync(dto.LogoBase64);
                if (saved == null)
                    return BadRequest(ApiResponse<TrustedSchoolDto>.Fail(
                        $"Logo invalide (formats : JPEG/PNG/WEBP, max {_uploads.MaxPhotoSizeMb} Mo)."));
                entity.LogoUrl = saved;
            }

            _context.TrustedSchools.Add(entity);
            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<TrustedSchoolDto>.Ok(ToDto(entity), "École ajoutée."));
        }

        [HttpPut("{id}")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<ActionResult<ApiResponse<TrustedSchoolDto>>> Update(
            int id, [FromBody] UpdateTrustedSchoolDto dto, CancellationToken ct)
        {
            var entity = await _context.TrustedSchools.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (entity == null) return NotFound(ApiResponse<TrustedSchoolDto>.Fail("École introuvable."));

            if (!string.IsNullOrWhiteSpace(dto.Name)) entity.Name = dto.Name.Trim();
            if (dto.DisplayOrder.HasValue) entity.DisplayOrder = dto.DisplayOrder.Value;
            if (dto.IsActive.HasValue) entity.IsActive = dto.IsActive.Value;

            if (!string.IsNullOrWhiteSpace(dto.LogoBase64))
            {
                var saved = await SaveLogoAsync(dto.LogoBase64);
                if (saved == null)
                    return BadRequest(ApiResponse<TrustedSchoolDto>.Fail(
                        $"Logo invalide (formats : JPEG/PNG/WEBP, max {_uploads.MaxPhotoSizeMb} Mo)."));
                DeleteLogoFile(entity.LogoUrl);
                entity.LogoUrl = saved;
            }

            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<TrustedSchoolDto>.Ok(ToDto(entity), "École mise à jour."));
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var entity = await _context.TrustedSchools.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (entity == null) return NotFound();

            DeleteLogoFile(entity.LogoUrl);
            _context.TrustedSchools.Remove(entity);
            await _context.SaveChangesAsync(ct);
            return NoContent();
        }

        // ----- Helpers -----

        private static TrustedSchoolDto ToDto(Models.TrustedSchool t) => new()
        {
            Id = t.Id,
            Name = t.Name,
            LogoUrl = t.LogoUrl,
            DisplayOrder = t.DisplayOrder,
            IsActive = t.IsActive
        };

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
