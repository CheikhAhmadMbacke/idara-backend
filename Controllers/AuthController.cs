using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Auth;
using Idara.API.DTOs.Common;
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
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IOtpService _otpService;
        private readonly IJwtService _jwtService;
        private readonly IRefreshTokenService _refreshTokens;
        private readonly IWebHostEnvironment _environment;
        private readonly IEmailService _emailService;
        private readonly UploadSettings _uploads;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            AppDbContext context,
            IOtpService otpService,
            IJwtService jwtService,
            IRefreshTokenService refreshTokens,
            IWebHostEnvironment environment,
            IEmailService emailService,
            IOptions<UploadSettings> uploads,
            ILogger<AuthController> logger)
        {
            _context = context;
            _otpService = otpService;
            _jwtService = jwtService;
            _refreshTokens = refreshTokens;
            _environment = environment;
            _emailService = emailService;
            _uploads = uploads.Value;
            _logger = logger;
        }

        /// <summary>
        /// Envoie un OTP de réinitialisation de mot de passe.
        /// Pour ne pas révéler quels emails sont enregistrés (énumération),
        /// la réponse est toujours 200 OK même si l'email n'existe pas.
        /// </summary>
        [HttpPost("send-otp")]
        public async Task<IActionResult> SendOtp([FromBody] SendOtpRequest request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user != null)
            {
                // Priorité : DTO > préférence persistée du user > header Accept-Language > "fr".
                var lang = !string.IsNullOrWhiteSpace(request.PreferredLanguage)
                    ? request.PreferredLanguage!
                    : !string.IsNullOrWhiteSpace(user.PreferredLanguage)
                        ? user.PreferredLanguage
                        : HttpContext.GetPreferredLanguage();
                await _otpService.GenerateAndSendOtpAsync(request.Email, OtpPurpose.ResetPassword, lang);
            }
            // Même message dans les deux cas (anti-énumération).
            return Ok(ApiResponse<bool>.Ok(true, "Si cet email existe, un code de 6 chiffres a été envoyé."));
        }

        /// <summary>
        /// Envoie un OTP pour l'inscription après vérification que l'email n'existe pas déjà.
        /// </summary>
        [HttpPost("send-otp-register")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ApiResponse<bool>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ApiResponse<bool>))]
        public async Task<IActionResult> SendOtpForRegister([FromBody] SendOtpRequest request)
        {
            if (await _context.Users.AnyAsync(u => u.Email == request.Email))
                return BadRequest(ApiResponse<bool>.Fail("Cet email est déjà utilisé."));

            // Priorité : DTO > header Accept-Language > "fr". Pas de user encore.
            var lang = !string.IsNullOrWhiteSpace(request.PreferredLanguage)
                ? request.PreferredLanguage!
                : HttpContext.GetPreferredLanguage();
            await _otpService.GenerateAndSendOtpAsync(request.Email, OtpPurpose.Register, lang);
            return Ok(ApiResponse<bool>.Ok(true, "Un code OTP a été envoyé à votre adresse email."));
        }

        /// <summary>
        /// Crée un nouveau compte (école) après vérification OTP, puis émet
        /// directement une paire (access + refresh token) pour auto-login.
        /// L'OTP a déjà prouvé la possession de l'email et les credentials
        /// viennent d'être fournis : forcer un second login est de la friction
        /// inutile.
        /// </summary>
        [HttpPost("register")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ApiResponse<LoginResponse>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ApiResponse<bool>))]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (!await _otpService.VerifyOtpAsync(request.Email, request.OtpCode, OtpPurpose.Register))
                return BadRequest(ApiResponse<bool>.Fail("OTP invalide ou expiré."));

            if (await _context.Users.AnyAsync(u => u.Email == request.Email))
                return BadRequest(ApiResponse<bool>.Fail("Cet email est déjà utilisé."));

            var user = new User
            {
                Email = request.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Role = UserRoles.SchoolAdmin,
                IsEmailVerified = true,
                AccountStatus = AccountStatus.Inactive,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow,
                PreferredLanguage = !string.IsNullOrWhiteSpace(request.PreferredLanguage)
                    ? request.PreferredLanguage!
                    : HttpContext.GetPreferredLanguage(),
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var refreshToken = await _refreshTokens.CreateAsync(user.Id);

            var response = new LoginResponse
            {
                Token = _jwtService.GenerateToken(user),
                RefreshToken = refreshToken,
                Role = user.Role,
                SchoolId = user.SchoolId,
                AccountStatus = user.AccountStatus.ToString(),
                // École pas encore créée → KYC non soumis. Le client interprétera
                // null comme "NotSubmitted" et redirigera vers /submit-kyc.
                KycStatus = null,
            };
            return Ok(ApiResponse<LoginResponse>.Ok(response, "Compte créé avec succès."));
        }

        /// <summary>
        /// Réinitialise le mot de passe (après OTP).
        /// </summary>
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (!await _otpService.VerifyOtpAsync(request.Email, request.OtpCode, OtpPurpose.ResetPassword))
                return BadRequest(ApiResponse<bool>.Fail("OTP invalide ou expiré."));

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user == null)
                return BadRequest(ApiResponse<bool>.Fail("Aucun compte trouvé avec cet email."));

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            await _context.SaveChangesAsync();

            return Ok(ApiResponse<bool>.Ok(true, "Mot de passe réinitialisé avec succès."));
        }

        /// <summary>
        /// Connexion d'un utilisateur.
        /// </summary>
        /// <remarks>
        /// Les comptes inactifs (KYC non validé) peuvent se connecter pour soumettre leur dossier.
        /// Seuls les comptes suspendus sont bloqués.
        /// </remarks>
        [HttpPost("login")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ApiResponse<LoginResponse>))]
        [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ApiResponse<bool>))]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var user = await _context.Users.Include(u => u.School)
                .FirstOrDefaultAsync(u => u.Email == request.Email);

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Unauthorized(ApiResponse<bool>.Fail("Email ou mot de passe incorrect."));

            if (user.AccountStatus == AccountStatus.Suspended)
                return Unauthorized(ApiResponse<bool>.Fail("Votre compte a été suspendu. Contactez l'administration."));

            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var refreshToken = await _refreshTokens.CreateAsync(user.Id);

            var response = new LoginResponse
            {
                Token = _jwtService.GenerateToken(user),
                RefreshToken = refreshToken,
                Role = user.Role,
                SchoolId = user.SchoolId,
                AccountStatus = user.AccountStatus.ToString(),
                KycStatus = user.School?.KycStatus.ToString()
            };
            return Ok(ApiResponse<LoginResponse>.Ok(response));
        }

        /// <summary>
        /// Rotation du refresh token : prend l'ancien refresh, retourne un nouveau
        /// (access + refresh). L'ancien est immédiatement révoqué (rotation OWASP).
        /// </summary>
        [HttpPost("refresh")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ApiResponse<RefreshTokenResponse>))]
        [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ApiResponse<bool>))]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
        {
            var rotation = await _refreshTokens.RotateAsync(request.RefreshToken);
            if (rotation == null)
                return Unauthorized(ApiResponse<bool>.Fail("Refresh token invalide ou expiré."));

            var (user, newRefresh) = rotation.Value;
            // Re-vérifier le statut du compte au passage : un compte suspendu ne
            // doit plus pouvoir refresh.
            if (user.AccountStatus == AccountStatus.Suspended)
            {
                await _refreshTokens.RevokeAsync(newRefresh, "AccountSuspended");
                return Unauthorized(ApiResponse<bool>.Fail("Votre compte a été suspendu."));
            }

            var newAccess = _jwtService.GenerateToken(user);
            return Ok(ApiResponse<RefreshTokenResponse>.Ok(new RefreshTokenResponse
            {
                Token = newAccess,
                RefreshToken = newRefresh,
            }));
        }

        /// <summary>
        /// Logout : révoque le refresh token (l'access token JWT, lui, expire
        /// naturellement après quelques minutes).
        /// </summary>
        [HttpPost("logout")]
        [AllowAnonymous]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
        {
            await _refreshTokens.RevokeAsync(request.RefreshToken, "Logout");
            return Ok(ApiResponse<bool>.Ok(true, "Déconnecté."));
        }

        /// <summary>
        /// Soumission des informations de l'école (KYC) avec documents en base64.
        /// </summary>
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        [HttpPost("submit-kyc")]
        public async Task<IActionResult> SubmitKyc([FromBody] SubmitKycRequest request)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var user = await _context.Users.Include(u => u.School)
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return Unauthorized();

            var legalUrls = await SaveBase64FilesAsync(request.LegalDocumentsBase64, request.LegalDocumentsNames, "legal");
            var repUrls = await SaveBase64FilesAsync(request.RepresentativeDocumentsBase64, request.RepresentativeDocumentsNames, "representative");

            if (user.School != null)
            {
                if (user.School.KycStatus != KycStatus.Rejected)
                    return BadRequest(ApiResponse<bool>.Fail("Vous avez déjà soumis les informations de votre école."));

                user.School.Name = request.SchoolName;
                user.School.Address = request.SchoolAddress;
                user.School.PhoneNumber = request.SchoolPhone;
                user.School.LegalDocumentsUrl = legalUrls.Any() ? string.Join(",", legalUrls) : user.School.LegalDocumentsUrl;
                user.School.RepresentativeFirstName = request.RepFirstName;
                user.School.RepresentativeLastName = request.RepLastName;
                user.School.RepresentativePhone = request.RepPhone;
                user.School.RepresentativeIdDocumentUrl = repUrls.Any() ? string.Join(",", repUrls) : user.School.RepresentativeIdDocumentUrl;
                user.School.KycStatus = KycStatus.Submitted;
                user.School.SubmittedAt = DateTime.UtcNow;
                user.School.RejectionReason = null;
                await _context.SaveChangesAsync();
                return Ok(ApiResponse<bool>.Ok(true, "Informations mises à jour et soumises à validation."));
            }

            var school = new School
            {
                KycStatus = KycStatus.Submitted,
                Name = request.SchoolName,
                Address = request.SchoolAddress,
                PhoneNumber = request.SchoolPhone,
                LegalDocumentsUrl = legalUrls.Any() ? string.Join(",", legalUrls) : null,
                RepresentativeFirstName = request.RepFirstName,
                RepresentativeLastName = request.RepLastName,
                RepresentativePhone = request.RepPhone,
                RepresentativeIdDocumentUrl = repUrls.Any() ? string.Join(",", repUrls) : null,
                CreatedAt = DateTime.UtcNow,
                SubmittedAt = DateTime.UtcNow
            };
            _context.Schools.Add(school);
            await _context.SaveChangesAsync();

            user.SchoolId = school.Id;
            await _context.SaveChangesAsync();

            return Ok(ApiResponse<bool>.Ok(true, "Informations soumises. En attente de validation par l'administration."));
        }

        /// <summary>
        /// Valide une école (réservé au SuperAdmin).
        /// </summary>
        [Authorize(Roles = UserRoles.SuperAdmin)]
        [HttpPost("validate-school/{schoolId}")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ApiResponse<bool>))]
        [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ApiResponse<bool>))]
        public async Task<IActionResult> ValidateSchool(int schoolId)
        {
            var school = await _context.Schools.Include(s => s.Users).FirstOrDefaultAsync(s => s.Id == schoolId);
            if (school == null)
                return NotFound(ApiResponse<bool>.Fail("École non trouvée."));

            var adminId = User.GetUserId();
            if (adminId == null) return Unauthorized();

            school.KycStatus = KycStatus.Validated;
            school.ValidatedAt = DateTime.UtcNow;
            school.ValidatedBy = adminId.Value;
            foreach (var u in school.Users)
                u.AccountStatus = AccountStatus.Active;

            await _context.SaveChangesAsync();

            var adminUser = school.Users
                .Where(u => u.Role == UserRoles.SchoolAdmin)
                .OrderBy(u => u.CreatedAt)
                .FirstOrDefault();
            if (adminUser != null && school.Name != null)
            {
                // Envoi best-effort : la validation est commit en DB, on ne veut pas
                // retourner 500 si Gmail timeout (l'admin verra le statut dans l'app).
                try
                {
                    await _emailService.SendSchoolValidationEmailAsync(
                        adminUser.Email, school.Name, true, language: adminUser.PreferredLanguage);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Email de validation school {SchoolId} echoue (continue malgre tout)", schoolId);
                }
            }

            return Ok(ApiResponse<bool>.Ok(true, "École validée et comptes activés."));
        }

        /// <summary>
        /// Rejette une école (réservé au SuperAdmin).
        /// </summary>
        [Authorize(Roles = UserRoles.SuperAdmin)]
        [HttpPost("reject-school/{schoolId}")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ApiResponse<bool>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ApiResponse<bool>))]
        [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ApiResponse<bool>))]
        public async Task<IActionResult> RejectSchool(int schoolId, [FromBody] RejectSchoolRequest request)
        {
            var school = await _context.Schools.Include(s => s.Users).FirstOrDefaultAsync(s => s.Id == schoolId);
            if (school == null)
                return NotFound(ApiResponse<bool>.Fail("École non trouvée."));

            school.KycStatus = KycStatus.Rejected;
            school.RejectionReason = request.RejectionReason;
            await _context.SaveChangesAsync();

            var adminUser = school.Users
                .Where(u => u.Role == UserRoles.SchoolAdmin)
                .OrderBy(u => u.CreatedAt)
                .FirstOrDefault();
            if (adminUser != null && school.Name != null)
            {
                // Idem ValidateSchool : envoi best-effort.
                try
                {
                    await _emailService.SendSchoolValidationEmailAsync(
                        adminUser.Email, school.Name, false, request.RejectionReason, adminUser.PreferredLanguage);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Email de rejet school {SchoolId} echoue (continue malgre tout)", schoolId);
                }
            }

            return Ok(ApiResponse<bool>.Ok(true, "École rejetée."));
        }

        /// <summary>
        /// Liste des écoles en attente de validation (réservé au SuperAdmin).
        /// </summary>
        [Authorize(Roles = UserRoles.SuperAdmin)]
        [HttpGet("pending-schools")]
        public async Task<IActionResult> GetPendingSchools()
        {
            var pending = await _context.Schools
                .Where(s => s.KycStatus == KycStatus.Submitted)
                .Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.Address,
                    s.PhoneNumber,
                    s.RepresentativeFirstName,
                    s.RepresentativeLastName,
                    s.RepresentativePhone,
                    s.SubmittedAt,
                    LegalDocumentsUrls = s.LegalDocumentsUrl != null
                        ? s.LegalDocumentsUrl.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        : null,
                    RepresentativeDocumentsUrls = s.RepresentativeIdDocumentUrl != null
                        ? s.RepresentativeIdDocumentUrl.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        : null
                })
                .ToListAsync();
            return Ok(ApiResponse<object>.Ok(pending));
        }

        /// <summary>
        /// Suspension de tous les utilisateurs d'une école (sauf les Guardians).
        /// </summary>
        [Authorize(Roles = UserRoles.SuperAdmin)]
        [HttpPost("suspend-school/{schoolId}")]
        public async Task<IActionResult> SuspendSchool(int schoolId)
        {
            var school = await _context.Schools.Include(s => s.Users).FirstOrDefaultAsync(s => s.Id == schoolId);
            if (school == null)
                return NotFound(ApiResponse<bool>.Fail("École non trouvée."));

            foreach (var user in school.Users.Where(u => u.Role != UserRoles.Guardian))
                user.AccountStatus = AccountStatus.Suspended;

            await _context.SaveChangesAsync();
            return Ok(ApiResponse<bool>.Ok(true, "Compte de l'école suspendu (sauf parents)."));
        }

        /// <summary>
        /// Réactivation des utilisateurs d'une école (sauf les Guardians).
        /// </summary>
        [Authorize(Roles = UserRoles.SuperAdmin)]
        [HttpPost("activate-school/{schoolId}")]
        public async Task<IActionResult> ActivateSchool(int schoolId)
        {
            var school = await _context.Schools.Include(s => s.Users).FirstOrDefaultAsync(s => s.Id == schoolId);
            if (school == null)
                return NotFound(ApiResponse<bool>.Fail("École non trouvée."));

            var newStatus = school.KycStatus == KycStatus.Validated ? AccountStatus.Active : AccountStatus.Inactive;

            foreach (var user in school.Users.Where(u => u.Role != UserRoles.Guardian))
                user.AccountStatus = newStatus;

            await _context.SaveChangesAsync();
            return Ok(ApiResponse<bool>.Ok(true, $"Compte de l'école réactivé (statut : {newStatus}) (parents exclus)."));
        }

        /// <summary>
        /// Vérification du statut du compte connecté.
        /// </summary>
        [Authorize]
        [HttpGet("my-status")]
        public async Task<IActionResult> GetMyStatus()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var user = await _context.Users.Include(u => u.School).FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return Unauthorized();

            return Ok(ApiResponse<object>.Ok(new
            {
                user.AccountStatus,
                KycStatus = user.School?.KycStatus.ToString(),
                RejectionReason = user.School?.RejectionReason,
                user.SchoolId,
                user.Email,
                user.Role
            }));
        }

        /// <summary>
        /// Vérification du statut d'une école par le SuperAdmin.
        /// </summary>
        [Authorize(Roles = UserRoles.SuperAdmin)]
        [HttpGet("school-status/{schoolId}")]
        public async Task<IActionResult> GetSchoolStatus(int schoolId)
        {
            var school = await _context.Schools
                .Include(s => s.Users)
                .FirstOrDefaultAsync(s => s.Id == schoolId);
            if (school == null)
                return NotFound(ApiResponse<bool>.Fail("École non trouvée."));

            return Ok(ApiResponse<object>.Ok(new
            {
                school.Id,
                school.Name,
                school.KycStatus,
                school.RejectionReason,
                Users = school.Users.Select(u => new { u.Email, u.Role, u.AccountStatus })
            }));
        }

        /// <summary>
        /// Invite un nouvel utilisateur (Teacher, SchoolStaff ou Guardian) pour l'école connectée.
        /// </summary>
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        [HttpPost("invite-user")]
        public async Task<IActionResult> InviteUser([FromBody] InviteUserRequest request)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var currentUser = await _context.Users.Include(u => u.School).FirstOrDefaultAsync(u => u.Id == userId);
            if (currentUser?.School == null)
                return BadRequest(ApiResponse<bool>.Fail("École non trouvée pour cet utilisateur."));

            if (currentUser.School.KycStatus != KycStatus.Validated)
                return BadRequest(ApiResponse<bool>.Fail("L'école doit être validée pour ajouter des utilisateurs."));

            if (currentUser.AccountStatus != AccountStatus.Active)
                return BadRequest(ApiResponse<bool>.Fail("Votre compte doit être actif pour ajouter des utilisateurs."));

            // Validation spécifique Guardian : l'élève doit appartenir à l'école.
            if (request.Function == UserRoles.Guardian)
            {
                if (!request.StudentId.HasValue)
                    return BadRequest(ApiResponse<bool>.Fail("StudentId requis pour inviter un Guardian."));

                var studentOk = await _context.Students.AnyAsync(s =>
                    s.Id == request.StudentId.Value
                    && s.SchoolId == currentUser.SchoolId
                    && !s.IsDeleted);
                if (!studentOk)
                    return BadRequest(ApiResponse<bool>.Fail("Élève introuvable dans votre école."));
            }

            if (await _context.Users.AnyAsync(u => u.Email == request.Email))
                return BadRequest(ApiResponse<bool>.Fail("Cet email est déjà utilisé."));

            var tempPassword = PasswordGenerator.Generate(12);

            var invitedStatus = request.Function == UserRoles.Guardian
                ? AccountStatus.Active
                : (currentUser.AccountStatus == AccountStatus.Active ? AccountStatus.Active : AccountStatus.Inactive);

            // Un Guardian n'est pas attaché à une école (multi-école possible).
            int? newUserSchoolId = request.Function == UserRoles.Guardian ? null : currentUser.SchoolId;

            var newUser = new User
            {
                Email = request.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword),
                Role = request.Function,
                IsEmailVerified = true,
                AccountStatus = invitedStatus,
                SchoolId = newUserSchoolId,
                FullName = request.FullName,
                PhoneNumber = request.PhoneNumber,
                CreatedAt = DateTime.UtcNow,
                // Hérite de la langue de celui qui invite (env. culturellement homogène).
                PreferredLanguage = currentUser.PreferredLanguage,
            };
            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            // Liaison Guardian ↔ Student (l'invite n'a de sens que liée à un enfant).
            if (request.Function == UserRoles.Guardian && request.StudentId.HasValue)
            {
                _context.StudentGuardians.Add(new StudentGuardian
                {
                    StudentId = request.StudentId.Value,
                    GuardianId = newUser.Id,
                    Relationship = request.Relationship ?? string.Empty,
                    IsPrimaryGuardian = request.IsPrimaryGuardian
                });
                await _context.SaveChangesAsync();
            }

            await _emailService.SendInvitationEmailAsync(
                request.Email,
                request.FullName,
                currentUser.School.Name ?? "Idara",
                request.Function,
                tempPassword,
                newUser.PreferredLanguage);

            return Ok(ApiResponse<bool>.Ok(true, "Utilisateur invité avec succès. Un email lui a été envoyé."));
        }

        /// <summary>
        /// Recherche d'un Guardian existant par email (pour pouvoir le lier à un élève).
        /// Restreint aux Guardians ayant au moins un enfant rattaché à l'école courante.
        /// </summary>
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        [HttpGet("search-guardian")]
        public async Task<IActionResult> SearchGuardian([FromQuery] string email)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            // Un Guardian est visible si l'un de ses enfants appartient à l'école courante.
            var guardian = await _context.Users
                .Where(u => u.Role == UserRoles.Guardian
                    && u.Email == email
                    && _context.StudentGuardians.Any(sg =>
                        sg.GuardianId == u.Id
                        && sg.Student.SchoolId == schoolId.Value
                        && !sg.Student.IsDeleted))
                .Select(u => new { u.Id, u.Email, u.FullName, u.PhoneNumber })
                .FirstOrDefaultAsync();

            return guardian == null ? NotFound() : Ok(guardian);
        }

        /// <summary>
        /// Sauvegarde une liste de fichiers KYC en validant taille, MIME déclaré
        /// (data URI) et magic-bytes. Lève <see cref="InvalidOperationException"/>
        /// en cas de dépassement — transformé en HTTP 400 par GlobalExceptionMiddleware.
        /// </summary>
        private async Task<List<string>> SaveBase64FilesAsync(List<string> base64List, List<string> fileNames, string subFolder)
        {
            var savedUrls = new List<string>();
            if (base64List.Count == 0) return savedUrls;

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", subFolder);
            Directory.CreateDirectory(uploadsFolder);

            for (var i = 0; i < base64List.Count; i++)
            {
                var base64 = base64List[i];
                var originalName = i < fileNames.Count ? fileNames[i] : null;

                var decoded = FileUploadValidator.DecodeAndValidate(
                    base64,
                    _uploads.MaxDocumentSizeMb,
                    _uploads.AllowedDocumentMimeTypes,
                    declaredContentType: null,
                    originalFileName: originalName);

                if (decoded == null)
                    throw new InvalidOperationException(
                        $"Fichier KYC '{originalName ?? "(sans nom)"}' invalide " +
                        $"(formats acceptés : PDF/JPEG/PNG/WEBP, max {_uploads.MaxDocumentSizeMb} Mo).");

                var safeBaseName = string.IsNullOrWhiteSpace(originalName)
                    ? $"{Guid.NewGuid()}{decoded.Extension}"
                    : Path.GetFileName(originalName);

                var uniqueFileName = $"{Guid.NewGuid()}_{safeBaseName}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                await System.IO.File.WriteAllBytesAsync(filePath, decoded.Bytes);
                savedUrls.Add($"/uploads/{subFolder}/{uniqueFileName}");
            }
            return savedUrls;
        }
    }
}
