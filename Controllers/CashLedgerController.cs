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
    /// Livre de caisse (gestion financière interne du daara, F1). Enregistre les
    /// mouvements HORS application (cash, espèces) pour la réconciliation et le
    /// bilan total. ⚠️ N'affecte JAMAIS le wallet SenePay (§112) : c'est un
    /// registre distinct, combiné au wallet uniquement à la LECTURE (bilan).
    /// </summary>
    [ApiController]
    [Route("api/cash-ledger")]
    [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
    public class CashLedgerController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly Services.IExportPdfService _exportPdf;

        public CashLedgerController(AppDbContext context, Services.IExportPdfService exportPdf)
        {
            _context = context;
            _exportPdf = exportPdf;
        }

        /// <summary>`GET /api/cash-ledger?from&to&type` — écritures de caisse (filtrables).</summary>
        [HttpGet]
        public async Task<ActionResult<List<CashLedgerEntryDto>>> List(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to,
            [FromQuery] CashEntryType? type, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var q = _context.CashLedgerEntries
                .Where(e => e.SchoolId == schoolId.Value && !e.IsDeleted);
            if (from.HasValue) q = q.Where(e => e.OccurredAt >= from.Value.ToUtcDay());
            if (to.HasValue) q = q.Where(e => e.OccurredAt < to.Value.ToUtcDay().AddDays(1));
            if (type.HasValue) q = q.Where(e => e.Type == type.Value);

            var items = await q
                .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
                .Select(e => new CashLedgerEntryDto
                {
                    Id = e.Id,
                    Type = e.Type,
                    AmountFcfa = e.AmountFcfa,
                    Category = e.Category,
                    CategoryId = e.CategoryId,
                    OccurredAt = e.OccurredAt,
                    Note = e.Note,
                    CreatedAt = e.CreatedAt
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        /// <summary>
        /// `GET /api/cash-ledger/balance?from&to` — bilan consolidé : wallet SenePay
        /// (solde réel) + caisse (période + cumul) + solde GLOBAL du daara.
        /// </summary>
        [HttpGet("balance")]
        public async Task<ActionResult<CashBalanceDto>> Balance(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var walletAvailable = await _context.SchoolWallets
                .Where(w => w.SchoolId == schoolId.Value)
                .Select(w => (long?)w.AvailableBalance)
                .FirstOrDefaultAsync(ct) ?? 0;

            var allEntries = _context.CashLedgerEntries
                .Where(e => e.SchoolId == schoolId.Value && !e.IsDeleted);

            // Cumul toutes périodes (pour le solde caisse + le solde global).
            var allTimeIncome = await allEntries
                .Where(e => e.Type == CashEntryType.Income).SumAsync(e => (long?)e.AmountFcfa, ct) ?? 0;
            var allTimeExpense = await allEntries
                .Where(e => e.Type == CashEntryType.Expense).SumAsync(e => (long?)e.AmountFcfa, ct) ?? 0;
            var cashBalanceAllTime = allTimeIncome - allTimeExpense;

            // Sur la période sélectionnée.
            var periodQ = allEntries;
            if (from.HasValue) periodQ = periodQ.Where(e => e.OccurredAt >= from.Value.ToUtcDay());
            if (to.HasValue) periodQ = periodQ.Where(e => e.OccurredAt < to.Value.ToUtcDay().AddDays(1));

            var periodIncome = await periodQ
                .Where(e => e.Type == CashEntryType.Income).SumAsync(e => (long?)e.AmountFcfa, ct) ?? 0;
            var periodExpense = await periodQ
                .Where(e => e.Type == CashEntryType.Expense).SumAsync(e => (long?)e.AmountFcfa, ct) ?? 0;

            return Ok(new CashBalanceDto
            {
                WalletAvailableFcfa = walletAvailable,
                CashIncomeFcfa = periodIncome,
                CashExpenseFcfa = periodExpense,
                CashNetFcfa = periodIncome - periodExpense,
                CashBalanceAllTimeFcfa = cashBalanceAllTime,
                GlobalBalanceFcfa = walletAvailable + cashBalanceAllTime
            });
        }

        /// <summary>
        /// `GET /api/cash-ledger/report/pdf?from&to` — rapport financier PDF (F4)
        /// partageable WhatsApp : total entrées / sorties / net + ventilation par
        /// catégorie sur la période + rappel wallet SenePay + solde global.
        /// </summary>
        [HttpGet("report/pdf")]
        public async Task<IActionResult> ReportPdf(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var schoolName = await _context.Schools
                .Where(s => s.Id == schoolId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(ct) ?? "Daara";

            var walletAvailable = await _context.SchoolWallets
                .Where(w => w.SchoolId == schoolId.Value)
                .Select(w => (long?)w.AvailableBalance)
                .FirstOrDefaultAsync(ct) ?? 0;

            var all = _context.CashLedgerEntries
                .Where(e => e.SchoolId == schoolId.Value && !e.IsDeleted);

            // Solde caisse cumulé (toutes périodes) → solde global.
            var allIncome = await all.Where(e => e.Type == CashEntryType.Income)
                .SumAsync(e => (long?)e.AmountFcfa, ct) ?? 0;
            var allExpense = await all.Where(e => e.Type == CashEntryType.Expense)
                .SumAsync(e => (long?)e.AmountFcfa, ct) ?? 0;
            var globalBalance = walletAvailable + (allIncome - allExpense);

            // Écritures de la période, groupées par catégorie et par type.
            var periodQ = all;
            if (from.HasValue) periodQ = periodQ.Where(e => e.OccurredAt >= from.Value.ToUtcDay());
            if (to.HasValue) periodQ = periodQ.Where(e => e.OccurredAt < to.Value.ToUtcDay().AddDays(1));

            var grouped = await periodQ
                .GroupBy(e => new { e.Type, e.Category })
                .Select(g => new { g.Key.Type, g.Key.Category, Amount = g.Sum(x => x.AmountFcfa) })
                .ToListAsync(ct);

            var incomeRows = grouped
                .Where(g => g.Type == CashEntryType.Income)
                .OrderByDescending(g => g.Amount)
                .Select(g => (g.Category ?? "Sans categorie", g.Amount))
                .ToList();
            var expenseRows = grouped
                .Where(g => g.Type == CashEntryType.Expense)
                .OrderByDescending(g => g.Amount)
                .Select(g => (g.Category ?? "Sans categorie", g.Amount))
                .ToList();
            var totalIncome = incomeRows.Sum(r => r.Item2);
            var totalExpense = expenseRows.Sum(r => r.Item2);

            var bytes = _exportPdf.BuildFinanceReportPdf(
                schoolName, from, to, incomeRows, expenseRows,
                totalIncome, totalExpense, walletAvailable, globalBalance);

            return File(bytes, "application/pdf", "rapport-financier-idara.pdf");
        }

        /// <summary>
        /// `GET /api/cash-ledger/export/pdf?from&to&type` — le DÉTAIL des écritures
        /// de caisse en PDF (une ligne par mouvement), à distinguer de
        /// `report/pdf` qui donne la synthèse par catégorie.
        /// </summary>
        [HttpGet("export/pdf")]
        public async Task<IActionResult> ExportPdf(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to,
            [FromQuery] CashEntryType? type, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            // Mêmes filtres que la liste affichée (List) — un seul comportement.
            var q = _context.CashLedgerEntries
                .Where(e => e.SchoolId == schoolId.Value && !e.IsDeleted);
            if (from.HasValue) q = q.Where(e => e.OccurredAt >= from.Value.ToUtcDay());
            if (to.HasValue) q = q.Where(e => e.OccurredAt < to.Value.ToUtcDay().AddDays(1));
            if (type.HasValue) q = q.Where(e => e.Type == type.Value);

            var items = await q
                .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
                .Take(Common.Utilities.FinanceLabels.MaxExportRows)
                .ToListAsync(ct);

            var rows = items.Select(e => new DTOs.Export.TransactionPdfRow
            {
                Date = e.OccurredAt,
                Title = e.Type == CashEntryType.Income ? "Entree de caisse" : "Sortie de caisse",
                Subtitle = string.IsNullOrWhiteSpace(e.Category) ? "Sans categorie" : e.Category,
                Note = e.Note,
                AmountFcfa = e.Type == CashEntryType.Income ? e.AmountFcfa : -e.AmountFcfa
            }).ToList();

            var income = items.Where(e => e.Type == CashEntryType.Income).Sum(e => e.AmountFcfa);
            var expense = items.Where(e => e.Type == CashEntryType.Expense).Sum(e => e.AmountFcfa);
            var summary = new List<(string, string, bool)>
            {
                ("Entrees", $"{income:N0}", false),
                ("Sorties", $"{expense:N0}", true),
                ("Net", $"{income - expense:N0}", income - expense < 0)
            };

            var schoolName = await _context.Schools
                .Where(s => s.Id == schoolId.Value).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "Daara";

            var bytes = _exportPdf.BuildTransactionsPdf(
                schoolName,
                Common.Utilities.FinanceLabels.ExportTitle("Livre de caisse", rows.Count),
                from, to, rows, summary);

            return File(bytes, "application/pdf", "livre-de-caisse-idara.pdf");
        }

        /// <summary>`POST /api/cash-ledger` — ajoute une écriture de caisse.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiResponse<CashLedgerEntryDto>>> Create(
            [FromBody] CreateCashLedgerEntryDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var (catId, catName) = await ResolveCategoryAsync(dto, schoolId.Value, ct);
            var entry = new CashLedgerEntry
            {
                SchoolId = schoolId.Value,
                Type = dto.Type,
                AmountFcfa = dto.AmountFcfa,
                Category = catName,
                CategoryId = catId,
                OccurredAt = (dto.OccurredAt ?? DateTime.UtcNow).ToUtcDay(),
                Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
                CreatedById = User.GetUserId(),
                CreatedAt = DateTime.UtcNow
            };
            _context.CashLedgerEntries.Add(entry);
            await _context.SaveChangesAsync(ct);

            return Ok(ApiResponse<CashLedgerEntryDto>.Ok(Map(entry), "Écriture enregistrée."));
        }

        /// <summary>`PUT /api/cash-ledger/{id}` — modifie une écriture de caisse.</summary>
        [HttpPut("{id:int}")]
        public async Task<ActionResult<ApiResponse<CashLedgerEntryDto>>> Update(
            int id, [FromBody] CreateCashLedgerEntryDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var entry = await _context.CashLedgerEntries
                .FirstOrDefaultAsync(e => e.Id == id && e.SchoolId == schoolId.Value && !e.IsDeleted, ct);
            if (entry == null) return NotFound(ApiResponse<bool>.Fail("Écriture introuvable."));

            var (catId, catName) = await ResolveCategoryAsync(dto, schoolId.Value, ct);
            entry.Type = dto.Type;
            entry.AmountFcfa = dto.AmountFcfa;
            entry.Category = catName;
            entry.CategoryId = catId;
            entry.OccurredAt = (dto.OccurredAt ?? entry.OccurredAt).ToUtcDay();
            entry.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            entry.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            return Ok(ApiResponse<CashLedgerEntryDto>.Ok(Map(entry), "Écriture mise à jour."));
        }

        /// <summary>`DELETE /api/cash-ledger/{id}` — supprime (soft) une écriture.</summary>
        [HttpDelete("{id:int}")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var n = await _context.CashLedgerEntries
                .Where(e => e.Id == id && e.SchoolId == schoolId.Value && !e.IsDeleted)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsDeleted, true), ct);
            if (n == 0) return NotFound(ApiResponse<bool>.Fail("Écriture introuvable."));

            return Ok(ApiResponse<bool>.Ok(true, "Écriture supprimée."));
        }

        private async Task<(int? id, string? name)> ResolveCategoryAsync(
            CreateCashLedgerEntryDto dto, int schoolId, CancellationToken ct)
        {
            if (dto.CategoryId is { } cid)
            {
                var cat = await _context.CashCategories
                    .Where(c => c.Id == cid && c.SchoolId == schoolId && !c.IsArchived)
                    .Select(c => new { c.Id, c.Name })
                    .FirstOrDefaultAsync(ct);
                if (cat != null) return (cat.Id, cat.Name);
            }
            var free = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim();
            return (null, free);
        }

        private static CashLedgerEntryDto Map(CashLedgerEntry e) => new()
        {
            Id = e.Id,
            Type = e.Type,
            AmountFcfa = e.AmountFcfa,
            Category = e.Category,
            CategoryId = e.CategoryId,
            OccurredAt = e.OccurredAt,
            Note = e.Note,
            CreatedAt = e.CreatedAt
        };
    }
}
