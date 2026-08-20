using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Payment;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Côté ÉCOLE : génération / renvoi / révocation du lien de paiement d'un
    /// responsable (cf. <see cref="PaymentLink"/>). Le lien est PAR RESPONSABLE
    /// et PAR ÉCOLE : demandé depuis n'importe lequel de ses enfants, il porte la
    /// dette de toute la fratrie. Il ne crée aucune facture.
    /// </summary>
    [ApiController]
    [Route("api/fees/payment-links")]
    [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
    public class PaymentLinksController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IGuardianPaymentService _guardianPayments;
        private readonly SenePaySettings _senepaySettings;
        private readonly ILogger<PaymentLinksController> _logger;

        public PaymentLinksController(
            AppDbContext context,
            IGuardianPaymentService guardianPayments,
            IOptions<SenePaySettings> senepaySettings,
            ILogger<PaymentLinksController> logger)
        {
            _context = context;
            _guardianPayments = guardianPayments;
            _senepaySettings = senepaySettings.Value;
            _logger = logger;
        }

        /// <summary>
        /// `POST /api/fees/payment-links` — renvoie le lien ACTIF du responsable
        /// (créé s'il n'existe pas) + le message WhatsApp + le résumé de la dette
        /// du jour. Idempotent : redemander = le même lien (comme les identifiants).
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<ApiResponse<PaymentLinkResponseDto>>> Create(
            [FromBody] CreatePaymentLinkRequestDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // Élève de MON école, non supprimé (un élève sorti garde une dette
            // payable → le lien reste générable, §159).
            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Id == dto.StudentId && s.SchoolId == schoolId.Value && !s.IsDeleted, ct);
            if (student == null)
            {
                return NotFound(ApiResponse<PaymentLinkResponseDto>.Fail("Élève introuvable."));
            }

            var guardians = await _context.StudentGuardians
                .Where(sg => sg.StudentId == student.Id && !sg.Guardian.IsDeleted)
                .OrderByDescending(sg => sg.IsPrimaryGuardian)
                .ThenBy(sg => sg.GuardianId)
                .Select(sg => new PaymentLinkGuardianChoiceDto
                {
                    GuardianId = sg.GuardianId,
                    FullName = sg.Guardian.FullName ?? "",
                    Phone = sg.Guardian.PhoneNumber,
                    IsPrimary = sg.IsPrimaryGuardian
                })
                .ToListAsync(ct);

            if (guardians.Count == 0)
            {
                return BadRequest(ApiResponse<PaymentLinkResponseDto>.Fail(
                    "Cet élève n'a aucun responsable. Ajoutez d'abord un responsable sur sa fiche : le lien de paiement lui sera rattaché."));
            }

            PaymentLinkGuardianChoiceDto chosen;
            if (dto.GuardianId is int gid)
            {
                var match = guardians.FirstOrDefault(g => g.GuardianId == gid);
                if (match == null)
                {
                    return BadRequest(ApiResponse<PaymentLinkResponseDto>.Fail(
                        "Ce responsable n'est pas rattaché à cet élève."));
                }
                chosen = match;
            }
            else if (guardians.Count == 1)
            {
                chosen = guardians[0];
            }
            else
            {
                return Ok(ApiResponse<PaymentLinkResponseDto>.Ok(new PaymentLinkResponseDto
                {
                    NeedsGuardianChoice = true,
                    Guardians = guardians
                }));
            }

            if (string.IsNullOrWhiteSpace(chosen.Phone))
            {
                return BadRequest(ApiResponse<PaymentLinkResponseDto>.Fail(
                    "Ce responsable n'a pas de numéro de téléphone : le paiement Wave ne peut pas lui être attribué."));
            }

            var now = DateTime.UtcNow;
            var link = await _context.PaymentLinks
                .FirstOrDefaultAsync(l => l.SchoolId == schoolId.Value && l.GuardianId == chosen.GuardianId && l.RevokedAt == null, ct);
            if (link == null)
            {
                link = new PaymentLink
                {
                    Token = Guid.NewGuid().ToString("N"),
                    SchoolId = schoolId.Value,
                    GuardianId = chosen.GuardianId,
                    CreatedById = userId.Value,
                    CreatedAt = now,
                    LastSharedAt = now
                };
                _context.PaymentLinks.Add(link);
                try
                {
                    await _context.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // Course entre deux membres de l'école : l'index unique a
                    // tranché, on relit le lien gagnant.
                    _context.Entry(link).State = EntityState.Detached;
                    link = await _context.PaymentLinks
                        .FirstAsync(l => l.SchoolId == schoolId.Value && l.GuardianId == chosen.GuardianId && l.RevokedAt == null, ct);
                }
            }
            else
            {
                link.LastSharedAt = now;
                await _context.SaveChangesAsync(ct);
            }

            var response = await BuildResponseAsync(link, chosen, ct);
            return Ok(ApiResponse<PaymentLinkResponseDto>.Ok(response));
        }

        /// <summary>
        /// `POST /api/fees/payment-links/{id}/revoke` — rend le lien mort (un
        /// nouveau sera créé à la prochaine demande). Les paiements déjà faits
        /// restent attribués (FK SetNull).
        /// </summary>
        [HttpPost("{id:int}/revoke")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<bool>>> Revoke(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var link = await _context.PaymentLinks
                .FirstOrDefaultAsync(l => l.Id == id && l.SchoolId == schoolId.Value, ct);
            if (link == null) return NotFound(ApiResponse<bool>.Fail("Lien introuvable."));
            if (link.RevokedAt != null) return Ok(ApiResponse<bool>.Ok(true, "Lien déjà révoqué."));

            link.RevokedAt = DateTime.UtcNow;
            link.RevokedById = userId.Value;
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("[payment-link] Lien {Id} révoqué par {UserId}", link.Id, userId);
            return Ok(ApiResponse<bool>.Ok(true, "Lien révoqué. Un nouveau lien sera créé à la prochaine demande."));
        }

        private async Task<PaymentLinkResponseDto> BuildResponseAsync(
            PaymentLink link, PaymentLinkGuardianChoiceDto guardian, CancellationToken ct)
        {
            var outstanding = await _guardianPayments.GetOutstandingAsync(link.GuardianId, link.SchoolId, ct);
            var school = await _context.Schools
                .Where(s => s.Id == link.SchoolId)
                .Select(s => new { s.Name, s.NameAr })
                .FirstAsync(ct);
            var paidViaLink = await _context.Payments
                .CountAsync(p => p.PaymentLinkId == link.Id && p.Status == PaymentStatus.Completed, ct);

            var url = $"{_senepaySettings.PublicBaseUrl.TrimEnd('/')}/pay/link/{link.Token}";
            var lines = (outstanding?.Lines ?? new List<OutstandingLine>())
                .Select(l => new PaymentLinkDueLineDto
                {
                    StudentId = l.StudentId,
                    StudentName = l.StudentName,
                    Label = InvoiceLabel.Fr(l.Type, l.PeriodStart),
                    AmountFcfa = l.RemainingFcfa
                })
                .ToList();
            var total = outstanding?.TotalDueFcfa ?? 0;
            var schoolName = !string.IsNullOrWhiteSpace(school.Name) ? school.Name! : (school.NameAr ?? "votre daara");

            return new PaymentLinkResponseDto
            {
                LinkId = link.Id,
                Url = url,
                GuardianId = guardian.GuardianId,
                GuardianFullName = guardian.FullName,
                GuardianPhone = guardian.Phone,
                Message = ComposeMessage(guardian.FullName, schoolName, lines, total, url,
                    outstanding?.IsFreeAmount == true),
                HasOutstanding = total > 0,
                IsFreeAmount = outstanding?.IsFreeAmount == true,
                TotalDueFcfa = total,
                AmountToChargeFcfa = outstanding?.ChargeFor(total) ?? total,
                Lines = lines,
                CreatedAt = link.CreatedAt,
                LastOpenedAt = link.LastOpenedAt,
                OpenCount = link.OpenCount,
                PaidViaLinkCount = paidViaLink
            };
        }

        /// <summary>
        /// Message WhatsApp en FRANÇAIS (décision 2026-08-20, comme les
        /// identifiants). Le lien étant permanent, le texte reste vrai même si
        /// le montant change : il invite à ouvrir le lien, qui dit la dette du jour.
        /// </summary>
        internal static string ComposeMessage(
            string fullName, string schoolName, List<PaymentLinkDueLineDto> lines, long total, string url, bool freeAmount)
        {
            var nom = string.IsNullOrWhiteSpace(fullName) ? "" : " " + fullName.Trim();
            var sb = new System.Text.StringBuilder();
            sb.Append($"Salam{nom}, voici votre lien de paiement pour {schoolName} :\n{url}\n\n");
            if (total > 0)
            {
                var children = lines.Select(l => l.StudentName).Distinct().ToList();
                sb.Append(children.Count == 1
                    ? $"À régler pour {children[0]} : {Fmt(total)} FCFA.\n"
                    : $"À régler pour {string.Join(", ", children)} : {Fmt(total)} FCFA.\n");
            }
            else if (freeAmount)
            {
                sb.Append("Vous pouvez y régler le montant de votre choix.\n");
            }
            else
            {
                sb.Append("Rien à régler pour l'instant.\n");
            }
            sb.Append("Ouvrez le lien puis appuyez sur « Payer avec Wave ». Gardez ce lien : il reste valable pour les prochains paiements.");
            return sb.ToString();
        }

        private static string Fmt(long v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
    }
}
