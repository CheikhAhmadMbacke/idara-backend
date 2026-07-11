using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Finance;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Catégories de caisse + budgets (gestion financière daara, F2). Le
    /// « dépensé ce mois » et l'alerte de dépassement sont calculés à la lecture
    /// à partir du livre de caisse (<see cref="CashLedgerEntry"/>).
    /// </summary>
    [ApiController]
    [Route("api/cash-categories")]
    [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
    public class CashCategoriesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CashCategoriesController(AppDbContext context) => _context = context;

        /// <summary>`GET /api/cash-categories?type` — catégories actives + statut budget du mois.</summary>
        [HttpGet]
        public async Task<ActionResult<List<CashCategoryDto>>> List(
            [FromQuery] CashEntryType? type, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var q = _context.CashCategories
                .Where(c => c.SchoolId == schoolId.Value && !c.IsArchived);
            if (type.HasValue) q = q.Where(c => c.Type == type.Value);
            var cats = await q.OrderBy(c => c.Name).ToListAsync(ct);
            if (cats.Count == 0) return Ok(new List<CashCategoryDto>());

            // Dépensé ce mois-ci par catégorie (une seule requête agrégée).
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1);
            var catIds = cats.Select(c => c.Id).ToList();

            var spentByCat = await _context.CashLedgerEntries
                .Where(e => e.SchoolId == schoolId.Value && !e.IsDeleted
                    && e.CategoryId != null && catIds.Contains(e.CategoryId.Value)
                    && e.OccurredAt >= monthStart && e.OccurredAt < monthEnd)
                .GroupBy(e => e.CategoryId!.Value)
                .Select(g => new { CategoryId = g.Key, Sum = g.Sum(e => e.AmountFcfa) })
                .ToDictionaryAsync(x => x.CategoryId, x => x.Sum, ct);

            var result = cats.Select(c =>
            {
                var spent = spentByCat.TryGetValue(c.Id, out var s) ? s : 0L;
                return new CashCategoryDto
                {
                    Id = c.Id,
                    Type = c.Type,
                    Name = c.Name,
                    MonthlyBudgetFcfa = c.MonthlyBudgetFcfa,
                    SpentThisMonthFcfa = spent,
                    IsOverBudget = c.MonthlyBudgetFcfa is { } b && b > 0 && spent > b
                };
            }).ToList();

            return Ok(result);
        }

        /// <summary>`POST /api/cash-categories` — crée une catégorie (+ budget optionnel).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiResponse<CashCategoryDto>>> Create(
            [FromBody] CreateCashCategoryDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var cat = new CashCategory
            {
                SchoolId = schoolId.Value,
                Type = dto.Type,
                Name = dto.Name.Trim(),
                MonthlyBudgetFcfa = dto.MonthlyBudgetFcfa is > 0 ? dto.MonthlyBudgetFcfa : null,
                CreatedAt = DateTime.UtcNow
            };
            _context.CashCategories.Add(cat);
            await _context.SaveChangesAsync(ct);

            return Ok(ApiResponse<CashCategoryDto>.Ok(Map(cat, 0), "Catégorie créée."));
        }

        /// <summary>`PUT /api/cash-categories/{id}` — modifie une catégorie / son budget.</summary>
        [HttpPut("{id:int}")]
        public async Task<ActionResult<ApiResponse<CashCategoryDto>>> Update(
            int id, [FromBody] CreateCashCategoryDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var cat = await _context.CashCategories
                .FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == schoolId.Value && !c.IsArchived, ct);
            if (cat == null) return NotFound(ApiResponse<bool>.Fail("Catégorie introuvable."));

            cat.Type = dto.Type;
            cat.Name = dto.Name.Trim();
            cat.MonthlyBudgetFcfa = dto.MonthlyBudgetFcfa is > 0 ? dto.MonthlyBudgetFcfa : null;
            cat.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            return Ok(ApiResponse<CashCategoryDto>.Ok(Map(cat, 0), "Catégorie mise à jour."));
        }

        /// <summary>`DELETE /api/cash-categories/{id}` — archive (les écritures gardent le nom dénormalisé).</summary>
        [HttpDelete("{id:int}")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var n = await _context.CashCategories
                .Where(c => c.Id == id && c.SchoolId == schoolId.Value && !c.IsArchived)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsArchived, true), ct);
            if (n == 0) return NotFound(ApiResponse<bool>.Fail("Catégorie introuvable."));

            return Ok(ApiResponse<bool>.Ok(true, "Catégorie archivée."));
        }

        private static CashCategoryDto Map(CashCategory c, long spent) => new()
        {
            Id = c.Id,
            Type = c.Type,
            Name = c.Name,
            MonthlyBudgetFcfa = c.MonthlyBudgetFcfa,
            SpentThisMonthFcfa = spent,
            IsOverBudget = c.MonthlyBudgetFcfa is { } b && b > 0 && spent > b
        };
    }
}
