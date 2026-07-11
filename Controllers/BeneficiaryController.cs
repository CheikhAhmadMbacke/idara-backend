using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Payment;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Espace bénéficiaire (F3) : un enseignant / surveillant / personnel qui a
    /// reçu un virement du daara (salaire, charge) voit ce qu'il a reçu + un reçu
    /// de virement PDF. C'est une VUE en lecture des <see cref="Withdrawal"/>
    /// sortants dont le numéro destinataire correspond à SON compte — aucun
    /// wallet, aucun solde, aucune écriture côté bénéficiaire.
    ///
    /// Auto-lien par numéro (décision produit) : le rapprochement se fait à la
    /// LECTURE — <c>User.PhoneNumber</c> (E.164) == <c>Normalize(Withdrawal.RecipientPhone)</c>,
    /// scopé à l'école du compte. Pas de colonne de liaison à maintenir.
    /// </summary>
    [ApiController]
    [Route("api/beneficiary")]
    [Authorize(Roles = $"{UserRoles.Teacher},{UserRoles.Surveillant},{UserRoles.SchoolStaff}")]
    public class BeneficiaryController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IExportPdfService _exportPdf;

        public BeneficiaryController(AppDbContext context, IExportPdfService exportPdf)
        {
            _context = context;
            _exportPdf = exportPdf;
        }

        /// <summary>
        /// `GET /api/beneficiary/my-transfers` — virements COMPLÉTÉS reçus par le
        /// compte connecté (rapprochés par numéro, dans son école).
        /// </summary>
        [HttpGet("my-transfers")]
        public async Task<ActionResult<IEnumerable<BeneficiaryTransferDto>>> GetMyTransfers(CancellationToken ct)
        {
            var (e164, schoolId) = await ResolveIdentityAsync(ct);
            if (e164 == null || schoolId == null)
                return Ok(Array.Empty<BeneficiaryTransferDto>());

            // Volume faible (retraits d'une école) → on charge les candidats
            // Completed puis on filtre en mémoire par numéro normalisé (le
            // Normalize n'est pas traduisible en SQL, cf. libellés §Lot B).
            var candidates = await _context.Withdrawals
                .Include(w => w.School)
                .Where(w => w.SchoolId == schoolId.Value
                            && !w.IsPlatform
                            && w.Status == WithdrawalStatus.Completed)
                .OrderByDescending(w => w.CreatedAt)
                .ToListAsync(ct);

            var mine = candidates
                .Where(w => SenegalPhone.Normalize(w.RecipientPhone) == e164)
                .Select(MapToDto)
                .ToList();

            return Ok(mine);
        }

        /// <summary>
        /// `GET /api/beneficiary/transfers/{id}/receipt` — reçu de virement PDF,
        /// strictement scopé au numéro du compte connecté.
        /// </summary>
        [HttpGet("transfers/{id:int}/receipt")]
        public async Task<IActionResult> DownloadTransferReceipt(int id, CancellationToken ct)
        {
            var (e164, schoolId) = await ResolveIdentityAsync(ct);
            if (e164 == null || schoolId == null)
                return NotFound(ApiResponse<bool>.Fail("Virement introuvable."));

            var w = await _context.Withdrawals
                .Include(x => x.School)
                .FirstOrDefaultAsync(x => x.Id == id
                                          && x.SchoolId == schoolId.Value
                                          && !x.IsPlatform
                                          && x.Status == WithdrawalStatus.Completed, ct);
            if (w == null || SenegalPhone.Normalize(w.RecipientPhone) != e164)
                return NotFound(ApiResponse<bool>.Fail("Virement introuvable."));

            var bytes = _exportPdf.BuildTransferReceiptPdf(
                w.School?.Name ?? "Daara",
                w.Id,
                string.IsNullOrWhiteSpace(w.RecipientName) ? "Beneficiaire" : w.RecipientName,
                w.AmountFcfa,
                FrenchOperator(w.Operator),
                CategoryLabel(w),
                "Effectue",
                w.CompletedAt ?? w.CreatedAt);

            return File(bytes, "application/pdf", $"recu-virement-idara-{w.Id:D6}.pdf");
        }

        // ===== Helpers =====

        /// <summary>Numéro E.164 du compte connecté + son école (ou (null,null) si absent).</summary>
        private async Task<(string? e164, int? schoolId)> ResolveIdentityAsync(CancellationToken ct)
        {
            var userId = User.GetUserId();
            var schoolId = User.GetSchoolId();
            var phone = await _context.Users
                .Where(u => u.Id == userId && !u.IsDeleted)
                .Select(u => u.PhoneNumber)
                .FirstOrDefaultAsync(ct);
            return (SenegalPhone.Normalize(phone), schoolId);
        }

        private static BeneficiaryTransferDto MapToDto(Withdrawal w) => new()
        {
            Id = w.Id,
            SchoolName = w.School?.Name ?? string.Empty,
            AmountFcfa = w.AmountFcfa,
            Operator = w.Operator,
            Category = w.Category,
            CategoryLabel = CategoryLabel(w),
            Status = w.Status,
            CreatedAt = w.CreatedAt,
            CompletedAt = w.CompletedAt
        };

        private static string CategoryLabel(Withdrawal w)
        {
            if (!string.IsNullOrWhiteSpace(w.CategoryLabel)) return w.CategoryLabel!;
            return w.Category switch
            {
                TransferCategory.TeacherSalary => "Salaire enseignant",
                TransferCategory.StaffSalary => "Salaire personnel",
                TransferCategory.Rent => "Loyer",
                TransferCategory.Supplier => "Fournisseur",
                TransferCategory.Utilities => "Charges",
                TransferCategory.Other => "Autre",
                _ => "Virement"
            };
        }

        private static string FrenchOperator(PaymentOperator op) => op switch
        {
            PaymentOperator.Wave => "Wave",
            PaymentOperator.Orange => "Orange Money",
            _ => op.ToString()
        };
    }
}
