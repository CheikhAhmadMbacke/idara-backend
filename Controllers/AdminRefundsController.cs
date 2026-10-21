using System.ComponentModel.DataAnnotations;
using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Idara.API.Controllers
{
    public class RecordRefundDto
    {
        [Required(ErrorMessage = "Le motif est obligatoire.")]
        [StringLength(500, MinimumLength = 3, ErrorMessage = "Le motif doit faire au moins 3 caractères.")]
        public string Reason { get; set; } = string.Empty;

        /// <summary>Identifiant de l'opération de remboursement dans Wave (facultatif).</summary>
        [StringLength(100)]
        public string? ProviderReference { get; set; }

        [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// 💸 Enregistrement d'un remboursement fait depuis l'application Wave
    /// (2026-10-21, §308). SuperAdmin seul, mot de passe exigé — c'est un geste
    /// d'argent, au même titre que l'ajustement d'un solde.
    /// </summary>
    [ApiController]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    [Route("api/admin/payments")]
    public class AdminRefundsController : ControllerBase
    {
        private readonly PaymentRefundService _refunds;
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;

        public AdminRefundsController(PaymentRefundService refunds, AppDbContext context, IMemoryCache cache)
        {
            _refunds = refunds;
            _context = context;
            _cache = cache;
        }

        /// <summary>`GET /api/admin/payments/{ref}/refund-preview` — « 470 » ou « idara-paiement-470 ».</summary>
        [HttpGet("{reference}/refund-preview")]
        public async Task<ActionResult<ApiResponse<RefundPreview>>> Preview(string reference, CancellationToken ct)
        {
            if (PaymentRefundService.ParsePaymentRef(reference) is not int id)
                return BadRequest(ApiResponse<RefundPreview>.Fail("Référence de paiement invalide (ex. 470 ou idara-paiement-470)."));
            var preview = await _refunds.PreviewAsync(id, ct);
            return preview == null
                ? NotFound(ApiResponse<RefundPreview>.Fail("Paiement introuvable."))
                : Ok(ApiResponse<RefundPreview>.Ok(preview));
        }

        [HttpPost("{reference}/refund")]
        public async Task<ActionResult<ApiResponse<RefundPreview>>> Refund(
            string reference, [FromBody] RecordRefundDto dto, CancellationToken ct)
        {
            if (PaymentRefundService.ParsePaymentRef(reference) is not int id)
                return BadRequest(ApiResponse<RefundPreview>.Fail("Référence de paiement invalide."));

            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();
            var admin = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
            if (admin == null) return Unauthorized();

            // Step-up mot de passe (clé partagée avec les autres gestes d'argent).
            var rlKey = $"pwdreauth:{userId.Value}";
            if (_cache.TryGetValue(rlKey, out int attempts) && attempts >= 5)
                return StatusCode(429, ApiResponse<RefundPreview>.Fail("Trop de tentatives. Réessaie dans quelques minutes."));
            if (!BCrypt.Net.BCrypt.Verify(dto.Password, admin.PasswordHash))
            {
                _cache.Set(rlKey, attempts + 1, TimeSpan.FromMinutes(15));
                return BadRequest(ApiResponse<RefundPreview>.Fail("Mot de passe incorrect."));
            }
            _cache.Remove(rlKey);

            var result = await _refunds.RefundAsync(id, dto.Reason, dto.ProviderReference, userId.Value, ct);
            return result.Ok
                ? Ok(ApiResponse<RefundPreview>.Ok(result.Preview!, "Remboursement enregistré."))
                : BadRequest(ApiResponse<RefundPreview>.Fail(result.Error!));
        }
    }
}
