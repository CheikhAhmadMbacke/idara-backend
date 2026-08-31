using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Export;
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
        public async Task<ActionResult<IEnumerable<BeneficiaryTransferDto>>> GetMyTransfers(
            [FromQuery(Name = "q")] string? search,
            [FromQuery] TransferCategory? category,
            CancellationToken ct)
        {
            var (e164, schoolId) = await ResolveIdentityAsync(ct);
            if (e164 == null || schoolId == null)
                return Ok(Array.Empty<BeneficiaryTransferDto>());

            // Volume faible (retraits d'une école) → on charge les candidats
            // Completed puis on filtre en mémoire par numéro normalisé (le
            // Normalize n'est pas traduisible en SQL, cf. libellés §Lot B).
            var query = _context.Withdrawals
                .Include(w => w.School)
                .Where(w => w.SchoolId == schoolId.Value
                            && !w.IsPlatform
                            && w.Status == WithdrawalStatus.Completed);

            // Recherche libre (daara, motif, categorie saisie) + nature. Un
            // beneficiaire ne voit que des virements COMPLETES : filtrer par
            // statut n'aurait pas d'objet ici.
            if (category.HasValue) query = query.Where(w => w.Category == category.Value);
            if (TransactionSearch.Pattern(search) is string pattern)
                query = query.Where(w =>
                    (w.School != null && EF.Functions.ILike(w.School!.Name!, pattern))
                    || (w.Motif != null && EF.Functions.ILike(w.Motif, pattern))
                    || (w.CategoryLabel != null && EF.Functions.ILike(w.CategoryLabel, pattern)));

            var candidates = await query
                .OrderByDescending(w => w.CreatedAt)
                .ToListAsync(ct);

            var mine = candidates
                .Where(w => SenegalPhone.Normalize(w.RecipientPhone) == e164)
                .Select(MapToDto)
                .ToList();

            return Ok(mine);
        }

        /// <summary>
        /// `GET /api/beneficiary/my-transfers/pdf?from&to` — l'historique complet
        /// des virements reçus en PDF (tableau), en plus du reçu unitaire. Utile
        /// pour justifier ses revenus sur une période.
        /// </summary>
        [HttpGet("my-transfers/pdf")]
        public async Task<IActionResult> ExportMyTransfersPdf(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to,
            [FromQuery(Name = "q")] string? search,
            [FromQuery] TransferCategory? category,
            CancellationToken ct)
        {
            var (e164, schoolId) = await ResolveIdentityAsync(ct);

            var mine = new List<Withdrawal>();
            if (e164 != null && schoolId != null)
            {
                var query = _context.Withdrawals
                    .Include(w => w.School)
                    .Where(w => w.SchoolId == schoolId.Value
                                && !w.IsPlatform
                                && w.Status == WithdrawalStatus.Completed);

                if (from.HasValue) query = query.Where(w => w.CreatedAt >= from.Value.ToUtcDay());
                if (to.HasValue) query = query.Where(w => w.CreatedAt < to.Value.ToUtcDay().AddDays(1));

                // Recherche libre (daara, motif, categorie saisie) + nature. Un
                // beneficiaire ne voit que des virements COMPLETES : filtrer par
                // statut n'aurait pas d'objet ici.
                if (category.HasValue) query = query.Where(w => w.Category == category.Value);
                if (TransactionSearch.Pattern(search) is string pattern)
                    query = query.Where(w =>
                        (w.School != null && EF.Functions.ILike(w.School!.Name!, pattern))
                        || (w.Motif != null && EF.Functions.ILike(w.Motif, pattern))
                        || (w.CategoryLabel != null && EF.Functions.ILike(w.CategoryLabel, pattern)));


                var candidates = await query.OrderByDescending(w => w.CreatedAt).ToListAsync(ct);
                // Rapprochement par numéro normalisé, en mémoire (comme la liste).
                mine = candidates
                    .Where(w => SenegalPhone.Normalize(w.RecipientPhone) == e164)
                    .Take(FinanceLabels.MaxExportRows)
                    .ToList();
            }

            var rows = mine.Select(w => new TransactionPdfRow
            {
                Date = w.CompletedAt ?? w.CreatedAt,
                Title = CategoryLabel(w),
                Subtitle = w.School?.Name,
                Note = w.Motif,
                Method = FrenchOperator(w.Operator),
                Status = "Effectué",
                AmountFcfa = w.AmountFcfa // reçu par le bénéficiaire → entrée
            }).ToList();

            var total = mine.Sum(w => w.AmountFcfa);
            var summary = new List<(string, string, bool)>
            {
                ("Total recu", $"{total:N0}", false)
            };

            var myName = await _context.Users
                .Where(u => u.Id == User.GetUserId()).Select(u => u.FullName).FirstOrDefaultAsync(ct)
                ?? "Beneficiaire";

            var bytes = _exportPdf.BuildTransactionsPdf(
                myName,
                FinanceLabels.ExportTitle("Virements recus", rows.Count),
                from, to, rows, summary,
                // Export mono-type : la contrepartie est toujours le daara payeur.
                counterpartyHeader: "École");

            return File(bytes, "application/pdf", "mes-virements-idara.pdf");
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

            var bytes = _exportPdf.BuildTransferReceiptPdf(TransferReceiptFactory.From(w));

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
            Reference = IdaraReference.Withdrawal(w.Id),
            SenePayReference = w.SenePayDisbursementId,
            SchoolNameAr = w.School?.NameAr,
            AmountFcfa = w.AmountFcfa,
            Operator = w.Operator,
            Category = w.Category,
            CategoryLabel = CategoryLabel(w),
            Status = w.Status,
            CreatedAt = w.CreatedAt,
            CompletedAt = w.CompletedAt
        };

        /// <summary>
        /// Nature du virement, telle que la voit le bénéficiaire.
        ///
        /// ⚠️ Délègue à <see cref="FinanceLabels"/> — c'était une COPIE du même
        /// switch, et elle avait déjà divergé : « Fournisseur » ici contre
        /// « Achat / fournisseur » côté école. Le daara et l'enseignant lisaient
        /// donc deux natures différentes pour le même virement, et la copie
        /// ignorait les 7 catégories ajoutées en juillet (nourriture, avance sur
        /// salaire, transport…), qui retombaient toutes sur « Virement ».
        /// </summary>
        private static string CategoryLabel(Withdrawal w) =>
            FinanceLabels.TransferCategory(w.Category, w.CategoryLabel);

        private static string FrenchOperator(PaymentOperator op) =>
            FinanceLabels.Operator(op);
    }
}
