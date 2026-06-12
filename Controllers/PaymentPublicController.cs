using System.Security.Cryptography;
using System.Text;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Endpoints PUBLICS (anonymes) liés à la page HTML de résultat post-paiement.
    /// Ouverts par le navigateur du parent après que SenePay l'ait redirigé,
    /// donc SANS JWT. L'authentification se fait par <c>(paymentId, token)</c> :
    /// le token est un GUID opaque (128 bits d'entropie) stocké sur le Payment
    /// à l'init et inclus dans le successUrl/cancelUrl envoyé à SenePay.
    /// Comparaison constant-time pour résister aux side-channels.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("pay")]
    public class PaymentPublicController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IReceiptPdfService _receiptPdf;
        private readonly ILogger<PaymentPublicController> _logger;

        public PaymentPublicController(
            AppDbContext context,
            IWebHostEnvironment env,
            IReceiptPdfService receiptPdf,
            ILogger<PaymentPublicController> logger)
        {
            _context = context;
            _env = env;
            _receiptPdf = receiptPdf;
            _logger = logger;
        }

        /// <summary>
        /// `GET /pay/{paymentId}/{token}?status=success|cancel` — sert la page
        /// HTML statique. Elle se charge en JS du polling + rendu du reçu.
        /// On ne vérifie le token qu'au niveau des endpoints data (status,
        /// receipt.pdf) — la page elle-même est juste un container statique.
        /// </summary>
        [HttpGet("{paymentId:int}/{token}")]
        public IActionResult GetResultPage(int paymentId, string token, [FromQuery] string? status = null)
        {
            var pagePath = Path.Combine(_env.WebRootPath, "pay", "result.html");
            if (!System.IO.File.Exists(pagePath))
            {
                _logger.LogError("[pay/page] result.html introuvable à {Path}", pagePath);
                return NotFound("Page de résultat indisponible.");
            }
            // On sert le HTML tel quel ; le JS embarqué va relire paymentId,
            // token et status depuis l'URL.
            return PhysicalFile(pagePath, "text/html; charset=utf-8");
        }

        /// <summary>
        /// `GET /pay/{paymentId}/{token}/status` — JSON consommé par le poll
        /// JS de la page HTML. Retourne le statut du paiement + montant +
        /// nom de l'élève + référence SenePay. NE révèle pas plus que le
        /// strict nécessaire à l'écran (pas de schoolId, pas de detail interne).
        /// </summary>
        [HttpGet("{paymentId:int}/{token}/status")]
        public async Task<IActionResult> GetStatus(int paymentId, string token, CancellationToken ct)
        {
            var payment = await LookupAsync(paymentId, token, ct);
            if (payment == null) return NotFound(new { error = "not_found" });

            var student = payment.StudentId.HasValue
                ? await _context.Students.FirstOrDefaultAsync(s => s.Id == payment.StudentId.Value, ct)
                : null;
            var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == payment.SchoolId, ct);

            // Topup = rechargement du wallet école (FeesPayer=School, sans élève) —
            // distinct d'un paiement de scolarité (FeesPayer=Parent, avec élève).
            var isTopup = payment.FeesPayer == FeesPayer.School && payment.StudentId == null;

            return Ok(new
            {
                paymentId = payment.Id,
                status = payment.Status.ToString(),
                amountChargedFcfa = payment.AmountFcfa,
                targetAmountFcfa = payment.TargetAmountFcfa,
                @operator = payment.Operator.ToString(),
                senePayTransactionId = payment.SenePayTransactionId,
                paidAt = payment.PaidAt,
                failureReason = payment.FailureReason,
                hasReceipt = payment.Status == PaymentStatus.Completed,
                schoolName = school?.Name,
                studentName = student != null ? $"{student.FirstName} {student.LastName}" : null,
                isTopup,
            });
        }

        /// <summary>
        /// `GET /pay/{paymentId}/{token}/receipt.pdf` — sert le PDF directement
        /// (pas de bytes JSON), pour qu'il s'affiche dans une `<iframe>` ou se
        /// télécharge selon le `inline` query param. Régénère si le fichier a
        /// disparu du disque (recovery, cf. gotcha §57).
        /// </summary>
        [HttpGet("{paymentId:int}/{token}/receipt.pdf")]
        public async Task<IActionResult> GetReceipt(
            int paymentId, string token,
            [FromQuery] bool download = false,
            CancellationToken ct = default)
        {
            var payment = await LookupAsync(paymentId, token, ct);
            if (payment == null) return NotFound();
            if (payment.Status != PaymentStatus.Completed)
            {
                return BadRequest("Reçu disponible uniquement pour les paiements complétés.");
            }

            var relative = payment.ReceiptPdfPath ?? $"/uploads/receipts/receipt-{payment.Id}.pdf";
            var fullPath = Path.GetFullPath(Path.Combine(
                _env.WebRootPath, relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            var webRootFull = Path.GetFullPath(_env.WebRootPath);
            if (!fullPath.StartsWith(webRootFull, StringComparison.Ordinal))
            {
                return BadRequest("Chemin invalide.");
            }

            if (!System.IO.File.Exists(fullPath))
            {
                var student = payment.StudentId.HasValue
                    ? await _context.Students.FirstOrDefaultAsync(s => s.Id == payment.StudentId.Value, ct)
                    : null;
                var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == payment.SchoolId, ct);
                var invoice = payment.InvoiceId.HasValue
                    ? await _context.Invoices.FirstOrDefaultAsync(i => i.Id == payment.InvoiceId.Value, ct)
                    : null;
                if (school == null) return NotFound();
                var regenerated = await _receiptPdf.GenerateAsync(payment, school, student, invoice);
                await _context.Payments
                    .Where(p => p.Id == payment.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.ReceiptPdfPath, regenerated), ct);
                fullPath = Path.GetFullPath(Path.Combine(
                    _env.WebRootPath, regenerated.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath, ct);
            var fileName = $"recu-idara-{payment.Id:D6}.pdf";
            // download=true → Content-Disposition: attachment (force download)
            //            false → inline (affichage dans <iframe>)
            if (download)
            {
                return File(bytes, "application/pdf", fileName);
            }
            Response.Headers.Append("Content-Disposition", $"inline; filename=\"{fileName}\"");
            return File(bytes, "application/pdf");
        }

        // ===== Helpers =====

        /// <summary>
        /// Cherche un Payment par (Id, Token). Comparaison constant-time du
        /// token pour bloquer les attaques timing — même si l'attaquant
        /// connaît le PaymentId, il ne peut pas deviner les 128 bits du token
        /// en mesurant le temps de réponse byte par byte.
        /// </summary>
        private async Task<Payment?> LookupAsync(int paymentId, string token, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 64) return null;

            var p = await _context.Payments
                .FirstOrDefaultAsync(x => x.Id == paymentId, ct);
            if (p == null || string.IsNullOrEmpty(p.PublicResultToken)) return null;

            var a = Encoding.UTF8.GetBytes(token);
            var b = Encoding.UTF8.GetBytes(p.PublicResultToken);
            if (a.Length != b.Length) return null;
            return CryptographicOperations.FixedTimeEquals(a, b) ? p : null;
        }
    }
}
