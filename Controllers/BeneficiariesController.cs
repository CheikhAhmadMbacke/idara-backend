using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Payment;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Carnet de bénéficiaires de l'école (salaires, fournisseurs, loyer…) pour
    /// accélérer les transferts récurrents. Lecture SchoolAdmin+Staff, écriture
    /// SchoolAdmin only. Suppression = archivage (préserve l'historique des
    /// transferts qui référencent le bénéficiaire).
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/school/beneficiaries")]
    public class BeneficiariesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<BeneficiariesController> _logger;

        public BeneficiariesController(AppDbContext context, ILogger<BeneficiariesController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>Liste le carnet. Par défaut masque les archivés (includeArchived=true pour tout).</summary>
        [HttpGet]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<IEnumerable<TransferBeneficiaryDto>>> List(
            [FromQuery] bool includeArchived, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var query = _context.TransferBeneficiaries
                .Where(b => b.SchoolId == schoolId.Value);
            if (!includeArchived) query = query.Where(b => !b.IsArchived);

            var items = await query
                .OrderBy(b => b.Name)
                .ToListAsync(ct);

            return Ok(items.Select(Map));
        }

        [HttpPost]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<TransferBeneficiaryDto>>> Create(
            [FromBody] CreateTransferBeneficiaryDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            if (!TryParseOperator(dto.Operator, out var op))
                return BadRequest(ApiResponse<TransferBeneficiaryDto>.Fail("Opérateur non supporté (wave ou orange)."));

            var entity = new TransferBeneficiary
            {
                SchoolId = schoolId.Value,
                Name = dto.Name.Trim(),
                Phone = dto.Phone,
                Operator = op,
                DefaultCategory = dto.DefaultCategory,
                DefaultCategoryLabel = dto.DefaultCategory == TransferCategory.Other
                    ? dto.DefaultCategoryLabel?.Trim()
                    : null,
                Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
                CreatedById = userId.Value,
                CreatedAt = DateTime.UtcNow
            };
            _context.TransferBeneficiaries.Add(entity);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[beneficiaries] Créé #{Id} pour School {SchoolId} par {UserId}",
                entity.Id, schoolId, userId);

            return Ok(ApiResponse<TransferBeneficiaryDto>.Ok(Map(entity), "Bénéficiaire ajouté."));
        }

        [HttpPut("{id}")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<TransferBeneficiaryDto>>> Update(
            int id, [FromBody] UpdateTransferBeneficiaryDto dto, CancellationToken ct)
        {
            if (id != dto.Id) return BadRequest(ApiResponse<TransferBeneficiaryDto>.Fail("ID incohérent."));
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            if (!TryParseOperator(dto.Operator, out var op))
                return BadRequest(ApiResponse<TransferBeneficiaryDto>.Fail("Opérateur non supporté (wave ou orange)."));

            var entity = await _context.TransferBeneficiaries
                .FirstOrDefaultAsync(b => b.Id == id && b.SchoolId == schoolId.Value, ct);
            if (entity == null) return NotFound(ApiResponse<TransferBeneficiaryDto>.Fail("Bénéficiaire introuvable."));

            entity.Name = dto.Name.Trim();
            entity.Phone = dto.Phone;
            entity.Operator = op;
            entity.DefaultCategory = dto.DefaultCategory;
            entity.DefaultCategoryLabel = dto.DefaultCategory == TransferCategory.Other
                ? dto.DefaultCategoryLabel?.Trim()
                : null;
            entity.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            return Ok(ApiResponse<TransferBeneficiaryDto>.Ok(Map(entity), "Bénéficiaire mis à jour."));
        }

        /// <summary>Archive (masque) un bénéficiaire. On ne supprime pas physiquement
        /// pour préserver l'historique des transferts qui le référencent.</summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<bool>>> Archive(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var entity = await _context.TransferBeneficiaries
                .FirstOrDefaultAsync(b => b.Id == id && b.SchoolId == schoolId.Value, ct);
            if (entity == null) return NotFound(ApiResponse<bool>.Fail("Bénéficiaire introuvable."));

            entity.IsArchived = true;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            return Ok(ApiResponse<bool>.Ok(true, "Bénéficiaire archivé."));
        }

        private static bool TryParseOperator(string op, out PaymentOperator parsed)
        {
            switch (op?.ToLowerInvariant())
            {
                case "wave": parsed = PaymentOperator.Wave; return true;
                case "orange": parsed = PaymentOperator.Orange; return true;
                default: parsed = default; return false;
            }
        }

        private static TransferBeneficiaryDto Map(TransferBeneficiary b) => new()
        {
            Id = b.Id,
            Name = b.Name,
            PhoneMasked = MaskPhone(b.Phone),
            Phone = b.Phone,
            Operator = b.Operator,
            DefaultCategory = b.DefaultCategory,
            DefaultCategoryLabel = b.DefaultCategoryLabel,
            Note = b.Note,
            IsArchived = b.IsArchived,
            CreatedAt = b.CreatedAt
        };

        private static string MaskPhone(string phone)
        {
            if (string.IsNullOrEmpty(phone) || phone.Length < 4) return "****";
            return phone[..2] + new string('*', phone.Length - 4) + phone[^2..];
        }
    }
}
