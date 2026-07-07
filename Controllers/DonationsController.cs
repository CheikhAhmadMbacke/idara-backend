using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Auth;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Donation;
using Idara.API.DTOs.Payment;
using Idara.API.DTOs.Senepay;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Espace DONATEUR : auto-inscription, liste publique des daaras, envoi d'un
    /// don, historique « mes dons ». Un don est un <see cref="Payment"/> avec
    /// <c>Purpose=Donation</c> + <c>DonorId</c> (StudentId/GuardianId null),
    /// FeesPayer=Parent (le donateur porte les frais +8 %, le daara reçoit le
    /// montant plein). Réutilise toute la mécanique payin SenePay (Phase 1.4) et
    /// le webhook (crédit de la poche « Don » du wallet, cf.
    /// <see cref="IPayinSettlementService"/>).
    ///
    /// Le donateur n'a AUCUN SchoolId dans son JWT → il ne peut toucher aucun
    /// endpoint scopé école. Exempté de l'enforcement abonnement (comme Guardian).
    /// </summary>
    [ApiController]
    [Route("api/donations")]
    [Authorize(Roles = UserRoles.Donor)]
    public class DonationsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly IRefreshTokenService _refreshTokens;
        private readonly ISenePayClient _senepay;
        private readonly SenePaySettings _senepaySettings;
        private readonly IReceiptPdfService _receiptPdf;
        private readonly IWebHostEnvironment _env;
        private readonly IMemoryCache _cache;
        private readonly ILogger<DonationsController> _logger;

        public DonationsController(
            AppDbContext context,
            IJwtService jwtService,
            IRefreshTokenService refreshTokens,
            ISenePayClient senepay,
            IOptions<SenePaySettings> senepaySettings,
            IReceiptPdfService receiptPdf,
            IWebHostEnvironment env,
            IMemoryCache cache,
            ILogger<DonationsController> logger)
        {
            _context = context;
            _jwtService = jwtService;
            _refreshTokens = refreshTokens;
            _senepay = senepay;
            _senepaySettings = senepaySettings.Value;
            _receiptPdf = receiptPdf;
            _env = env;
            _cache = cache;
            _logger = logger;
        }

        // ====================================================================
        // ===== Auto-inscription (anonyme) =====
        // ====================================================================

        /// <summary>
        /// `POST /api/donations/register` — crée un compte donateur (nom +
        /// téléphone + mot de passe, email optionnel) et auto-login (LoginResponse).
        /// </summary>
        [HttpPost("register")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ApiResponse<LoginResponse>))]
        public async Task<IActionResult> Register([FromBody] DonorRegisterRequest request)
        {
            var phone = SenegalPhone.Normalize(request.Phone);
            if (phone == null)
                return BadRequest(ApiResponse<bool>.Fail("Numéro de téléphone invalide (format attendu : 7XXXXXXXX)."));

            // Anti-spam de création de comptes : 5 tentatives / 15 min / numéro.
            var rlKey = $"donor-register:{phone}";
            if (_cache.TryGetValue(rlKey, out int attempts) && attempts >= 5)
                return StatusCode(429, ApiResponse<bool>.Fail(
                    "Trop de tentatives. Réessayez dans quelques minutes."));

            // Unicité téléphone (applicative + index DB filtré). Un numéro déjà
            // utilisé par un autre compte (tout rôle) ne peut pas être ré-enregistré.
            var phoneTaken = await _context.Users.AnyAsync(u => u.PhoneNumber == phone && !u.IsDeleted);
            if (phoneTaken)
            {
                _cache.Set(rlKey, attempts + 1, TimeSpan.FromMinutes(15));
                return BadRequest(ApiResponse<bool>.Fail(
                    "Ce numéro est déjà associé à un compte. Connectez-vous."));
            }

            var email = string.IsNullOrWhiteSpace(request.Email)
                ? null
                : request.Email.Trim().ToLowerInvariant();
            if (email != null && await _context.Users.AnyAsync(u => u.Email != null && u.Email == email && !u.IsDeleted))
            {
                _cache.Set(rlKey, attempts + 1, TimeSpan.FromMinutes(15));
                return BadRequest(ApiResponse<bool>.Fail("Cet email est déjà utilisé."));
            }

            var lang = !string.IsNullOrWhiteSpace(request.PreferredLanguage)
                ? request.PreferredLanguage!
                : HttpContext.GetPreferredLanguage();

            var user = new User
            {
                FullName = request.FullName.Trim(),
                PhoneNumber = phone,
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Role = UserRoles.Donor,
                DonorType = request.DonorType,
                IsEmailVerified = email != null,
                AccountStatus = AccountStatus.Active, // pas de KYC pour un donateur
                SchoolId = null,                       // compte global, aucune école
                PreferredLanguage = lang,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var refreshToken = await _refreshTokens.CreateAsync(user.Id);
            return Ok(ApiResponse<LoginResponse>.Ok(new LoginResponse
            {
                Token = _jwtService.GenerateToken(user),
                RefreshToken = refreshToken,
                Role = user.Role,
                SchoolId = null,
                AccountStatus = user.AccountStatus.ToString(),
                KycStatus = null
            }, "Compte donateur créé avec succès."));
        }

        // ====================================================================
        // ===== Liste publique des daaras (anonyme) =====
        // ====================================================================

        /// <summary>
        /// `GET /api/donations/schools` — daaras validés à soutenir (nom +
        /// localisation). Anonyme : on peut parcourir avant de s'inscrire.
        /// </summary>
        [HttpGet("schools")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<DonationSchoolDto>>>> GetSchools(
            [FromQuery] string? search, CancellationToken ct)
        {
            var query = _context.Schools
                .Where(s => s.KycStatus == KycStatus.Validated);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(s =>
                    (s.Name != null && s.Name.ToLower().Contains(term))
                    || (s.Address != null && s.Address.ToLower().Contains(term)));
            }

            var schools = await query
                .OrderBy(s => s.Name)
                .Select(s => new DonationSchoolDto
                {
                    Id = s.Id,
                    Name = s.Name ?? $"Daara #{s.Id}",
                    Address = s.Address
                })
                .ToListAsync(ct);

            return Ok(ApiResponse<List<DonationSchoolDto>>.Ok(schools));
        }

        // ====================================================================
        // ===== Envoi d'un don =====
        // ====================================================================

        /// <summary>`POST /api/donations/initiate` — envoie un don Wave/Orange à un daara.</summary>
        [HttpPost("initiate")]
        public async Task<ActionResult<ApiResponse<InitiatePaymentResponseDto>>> Initiate(
            [FromBody] DonationInitiateRequest dto, CancellationToken ct)
        {
            var donorId = User.GetUserId()
                ?? throw new UnauthorizedAccessException("UserId manquant du JWT.");

            // Numéro du donateur récupéré en base (identité par téléphone) — plus
            // de saisie ni de choix d'opérateur : Wave uniquement (refonte 2026-07-07).
            var donorPhone = await _context.Users.Where(u => u.Id == donorId)
                .Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(donorPhone))
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "Aucun numéro de téléphone n'est associé à votre compte."));

            // Le daara doit exister ET être validé (on ne donne pas à une école
            // non validée / rejetée / supprimée).
            var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == dto.SchoolId, ct);
            if (school == null || school.KycStatus != KycStatus.Validated)
                return NotFound(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "Daara introuvable ou non disponible pour les dons."));

            var platform = await _context.GetPlatformSettingsAsync(ct);
            if (dto.Amount < platform.MinPayinFcfa)
                return BadRequest(ApiResponse<InitiatePaymentResponseDto>.Fail(
                    $"Le montant minimum d'un don est de {platform.MinPayinFcfa} FCFA."));

            // Garantit les fondations paiement (wallet) du daara avant tout crédit.
            await _context.EnsurePaymentFoundationsAsync(dto.SchoolId, ct);

            var targetAmount = dto.Amount;
            // Le donateur porte les frais → le daara reçoit le montant plein.
            var amountToCharge = (long)Math.Ceiling(targetAmount * platform.ParentFeeMultiplier);
            var operatorEnum = PaymentOperator.Wave; // Wave uniquement (2026-07-07)

            var payment = new Payment
            {
                SchoolId = dto.SchoolId,
                StudentId = null,
                GuardianId = null,
                InvoiceId = null,
                Purpose = PaymentPurpose.Donation,
                DonorId = donorId,
                AmountFcfa = amountToCharge,
                TargetAmountFcfa = targetAmount,
                FeesFcfa = 0,
                NetCreditedFcfa = 0,
                Operator = operatorEnum,
                FeesPayer = FeesPayer.Parent, // le donateur porte les frais
                Status = PaymentStatus.Pending,
                InitiatedAt = DateTime.UtcNow,
                PublicResultToken = Guid.NewGuid().ToString("N")
            };
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync(ct);

            SenePayInitiatePaymentResponse resp;
            try
            {
                resp = await _senepay.InitiatePaymentAsync(BuildSenePayRequest(payment, donorPhone), ct);
            }
            catch (SenePayApiException ex)
            {
                _logger.LogError(ex, "[donation/initiate] SenePay error pour Payment {PaymentId}", payment.Id);
                return StatusCode(502, ApiResponse<InitiatePaymentResponseDto>.Fail(
                    "SenePay temporairement indisponible. Réessayez dans quelques secondes."));
            }

            payment.SenePayInternalId = resp.InternalId;
            payment.SenePayTransactionId = resp.Token;
            await _context.SaveChangesAsync(ct);

            if (string.Equals(resp.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(resp.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                payment.Status = string.Equals(resp.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                    ? PaymentStatus.Cancelled : PaymentStatus.Failed;
                payment.FailedAt = DateTime.UtcNow;
                payment.FailureReason = resp.FailedReason ?? resp.ErrorCode;
                await _context.SaveChangesAsync(ct);
            }

            return Ok(ApiResponse<InitiatePaymentResponseDto>.Ok(new InitiatePaymentResponseDto
            {
                PaymentId = payment.Id,
                Status = resp.Status ?? "Pending",
                NextAction = resp.NextAction ?? "NONE",
                RedirectUrl = resp.RedirectUrl,
                OtpRequired = resp.OtpRequired,
                ErrorCode = resp.ErrorCode,
                FailureReason = resp.FailedReason,
                AmountChargedFcfa = payment.AmountFcfa
            }));
        }

        // ====================================================================
        // ===== Historique + statut + reçu =====
        // ====================================================================

        /// <summary>`GET /api/donations/mine` — historique des dons du donateur.</summary>
        [HttpGet("mine")]
        public async Task<ActionResult<ApiResponse<List<DonationDto>>>> Mine(CancellationToken ct)
        {
            var donorId = User.GetUserId()
                ?? throw new UnauthorizedAccessException("UserId manquant du JWT.");

            var items = await _context.Payments
                .Where(p => p.DonorId == donorId && p.Purpose == PaymentPurpose.Donation)
                .OrderByDescending(p => p.InitiatedAt)
                .Select(p => new DonationDto
                {
                    Id = p.Id,
                    SchoolId = p.SchoolId,
                    SchoolName = _context.Schools.Where(s => s.Id == p.SchoolId).Select(s => s.Name).FirstOrDefault(),
                    AmountFcfa = p.TargetAmountFcfa,
                    AmountChargedFcfa = p.AmountFcfa,
                    Operator = p.Operator,
                    Status = p.Status,
                    FailureReason = p.FailureReason,
                    InitiatedAt = p.InitiatedAt,
                    PaidAt = p.PaidAt,
                    ReceiptPdfUrl = p.ReceiptPdfPath
                })
                .ToListAsync(ct);

            return Ok(ApiResponse<List<DonationDto>>.Ok(items));
        }

        /// <summary>`GET /api/donations/{id}` — statut d'un don (poll côté Flutter).</summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ApiResponse<DonationDto>>> Get(int id, CancellationToken ct)
        {
            var donorId = User.GetUserId()
                ?? throw new UnauthorizedAccessException("UserId manquant du JWT.");

            var p = await _context.Payments
                .FirstOrDefaultAsync(x => x.Id == id && x.DonorId == donorId
                                          && x.Purpose == PaymentPurpose.Donation, ct);
            if (p == null) return NotFound(ApiResponse<DonationDto>.Fail("Don introuvable."));

            var schoolName = await _context.Schools.Where(s => s.Id == p.SchoolId)
                .Select(s => s.Name).FirstOrDefaultAsync(ct);

            return Ok(ApiResponse<DonationDto>.Ok(new DonationDto
            {
                Id = p.Id,
                SchoolId = p.SchoolId,
                SchoolName = schoolName,
                AmountFcfa = p.TargetAmountFcfa,
                AmountChargedFcfa = p.AmountFcfa,
                Operator = p.Operator,
                Status = p.Status,
                FailureReason = p.FailureReason,
                InitiatedAt = p.InitiatedAt,
                PaidAt = p.PaidAt,
                ReceiptPdfUrl = p.ReceiptPdfPath
            }));
        }

        /// <summary>`GET /api/donations/{id}/receipt` — reçu de don PDF (donateur propriétaire).</summary>
        [HttpGet("{id:int}/receipt")]
        public async Task<IActionResult> DownloadReceipt(int id, CancellationToken ct)
        {
            var donorId = User.GetUserId()
                ?? throw new UnauthorizedAccessException("UserId manquant du JWT.");

            var p = await _context.Payments
                .Include(x => x.Donor)
                .FirstOrDefaultAsync(x => x.Id == id && x.DonorId == donorId
                                          && x.Purpose == PaymentPurpose.Donation, ct);
            if (p == null) return NotFound(ApiResponse<bool>.Fail("Don introuvable."));
            if (p.Status != PaymentStatus.Completed)
                return BadRequest(ApiResponse<bool>.Fail("Reçu disponible uniquement pour les dons confirmés."));

            var relativePath = p.ReceiptPdfPath ?? $"/uploads/receipts/receipt-{p.Id}.pdf";
            var fullPath = Path.GetFullPath(Path.Combine(
                _env.WebRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

            var webRootFull = Path.GetFullPath(_env.WebRootPath);
            if (!fullPath.StartsWith(webRootFull, StringComparison.Ordinal))
                return BadRequest(ApiResponse<bool>.Fail("Chemin de reçu invalide."));

            if (!System.IO.File.Exists(fullPath))
            {
                var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == p.SchoolId, ct);
                if (school == null) return NotFound();
                var regenerated = await _receiptPdf.GenerateAsync(p, school, null, null, p.Donor);
                if (string.IsNullOrEmpty(p.ReceiptPdfPath))
                    await _context.Payments.Where(x => x.Id == p.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReceiptPdfPath, regenerated), ct);
                fullPath = Path.GetFullPath(Path.Combine(
                    _env.WebRootPath, regenerated.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath, ct);
            return File(bytes, "application/pdf", $"recu-don-idara-{p.Id:D6}.pdf");
        }

        // ====================================================================
        // ===== Helpers =====
        // ====================================================================

        private SenePayInitiatePaymentRequest BuildSenePayRequest(Payment payment, string payerPhone)
        {
            var publicBase = _senepaySettings.PublicBaseUrl.TrimEnd('/');
            var resultBase = $"{publicBase}/pay/{payment.Id}/{payment.PublicResultToken}";

            return new SenePayInitiatePaymentRequest
            {
                Amount = payment.AmountFcfa,
                Currency = "XOF",
                CountryCode = "SN",
                Operator = "wave", // Wave uniquement (2026-07-07)
                CustomerPhone = PaymentPhone.ForSenePay(payerPhone),
                OtpCode = null,
                OrderId = payment.Id.ToString(),
                CustomerName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value,
                WebhookUrl = _senepaySettings.WebhookPayinUrl,
                ReturnUrl = $"{resultBase}?status=success",
                CancelUrl = $"{resultBase}?status=cancel"
            };
        }
    }
}
