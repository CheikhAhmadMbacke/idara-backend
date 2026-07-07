using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Admin;
using Idara.API.DTOs.Common;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Back-office SuperAdmin dédié à l'école de DÉMONSTRATION : récupération des
    /// identifiants (pour les retrouver à tout moment) + génération manuelle des
    /// factures UNIQUEMENT pour cette école (bac à sable de test, sans polluer de
    /// vraie école — cf. §107).
    /// </summary>
    [ApiController]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    [Route("api/admin/demo")]
    public class DemoAdminController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly MonthlyInvoiceGenerationJob _invoiceJob;
        private readonly ILogger<DemoAdminController> _logger;

        public DemoAdminController(
            AppDbContext context,
            MonthlyInvoiceGenerationJob invoiceJob,
            ILogger<DemoAdminController> logger)
        {
            _context = context;
            _invoiceJob = invoiceJob;
            _logger = logger;
        }

        /// <summary>`GET /api/admin/demo/credentials` — identifiants des comptes de démo.</summary>
        [HttpGet("credentials")]
        public async Task<ActionResult<ApiResponse<DemoCredentialsDto>>> GetCredentials(CancellationToken ct)
        {
            var schoolId = await DemoSchoolIdAsync(ct);
            return Ok(ApiResponse<DemoCredentialsDto>.Ok(new DemoCredentialsDto
            {
                SchoolName = DemoAccounts.SchoolName,
                SchoolId = schoolId,
                AdminEmail = DemoAccounts.SchoolAdminEmail,
                AdminPassword = DemoAccounts.SchoolAdminPassword,
                GuardianPhone = DemoAccounts.GuardianPhone,
                GuardianCode = DemoAccounts.GuardianCode
            }));
        }

        /// <summary>
        /// `POST /api/admin/demo/generate-invoices?year=&amp;month=` — génère les
        /// factures du mois pour l'école de démo UNIQUEMENT. Idempotent (un élève
        /// déjà facturé pour la période n'est pas re-facturé) → pour re-tester un
        /// paiement, générer un mois FUTUR encore vierge.
        /// </summary>
        [HttpPost("generate-invoices")]
        public async Task<ActionResult<ApiResponse<InvoiceGenerationReport>>> GenerateInvoices(
            [FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
        {
            var schoolId = await DemoSchoolIdAsync(ct);
            if (schoolId is null or 0)
            {
                return NotFound(ApiResponse<InvoiceGenerationReport>.Fail(
                    "École de démonstration introuvable (le seed n'a peut-être pas encore tourné)."));
            }

            var now = DateTime.UtcNow;
            var y = year ?? now.Year;
            var m = month ?? now.Month;
            if (m < 1 || m > 12 || y < 2020 || y > now.Year + 1)
            {
                return BadRequest(ApiResponse<InvoiceGenerationReport>.Fail(
                    "Mois/année invalide."));
            }

            _logger.LogInformation(
                "[demo] Génération factures démo par SuperAdmin {UserId} pour {Year}-{Month:D2} (SchoolId={SchoolId})",
                User.GetUserId(), y, m, schoolId);

            var report = await _invoiceJob.GenerateForSchoolNowAsync(schoolId.Value, y, m, now, ct);

            string msg;
            if (report.InvoicesCreated > 0)
                msg = $"{report.InvoicesCreated} facture(s) générée(s) pour l'école de démo.";
            else if (report.StudentsWithoutFee > 0 && report.InvoicesAlreadyExisting == 0)
                msg = "Aucune facture : aucun tarif configuré pour les élèves de démo.";
            else if (report.InvoicesAlreadyExisting > 0)
                msg = "Les factures de ce mois existent déjà (essayez un mois futur pour re-tester).";
            else
                msg = "Aucune facture générée.";

            return Ok(ApiResponse<InvoiceGenerationReport>.Ok(report, msg));
        }

        private Task<int?> DemoSchoolIdAsync(CancellationToken ct) =>
            _context.Users
                .Where(u => u.Email == DemoAccounts.SchoolAdminEmail && !u.IsDeleted)
                .Select(u => u.SchoolId)
                .FirstOrDefaultAsync(ct);
    }
}
