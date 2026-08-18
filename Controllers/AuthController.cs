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
using Idara.API.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
        private readonly INotificationService _notif;
        private readonly IMemoryCache _cache;
        private readonly UploadSettings _uploads;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            AppDbContext context,
            IOtpService otpService,
            IJwtService jwtService,
            IRefreshTokenService refreshTokens,
            IWebHostEnvironment environment,
            IEmailService emailService,
            INotificationService notif,
            IMemoryCache cache,
            IOptions<UploadSettings> uploads,
            ILogger<AuthController> logger)
        {
            _context = context;
            _otpService = otpService;
            _jwtService = jwtService;
            _refreshTokens = refreshTokens;
            _environment = environment;
            _emailService = emailService;
            _notif = notif;
            _cache = cache;
            _uploads = uploads.Value;
            _logger = logger;
        }

        // ===== Rate-limiting applicatif (anti brute-force) =====
        // Compteur de tentatives par clé, en mémoire. Mono-instance.

        private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(15);

        private bool IsRateLimited(string key, int max) =>
            _cache.TryGetValue(key, out int count) && count >= max;

        private void RegisterAttempt(string key)
        {
            var count = _cache.TryGetValue(key, out int c) ? c : 0;
            _cache.Set(key, count + 1, RateWindow);
        }

        private void ResetAttempts(string key) => _cache.Remove(key);

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
            // Identifiant = email (contient "@") OU numéro de téléphone.
            var raw = (request.Email ?? string.Empty).Trim();
            var isEmail = raw.Contains('@');
            var emailKey = raw.ToLowerInvariant();
            var phone = isEmail ? null : SenegalPhone.Normalize(raw);

            // Rate-limiting anti brute-force : 5 tentatives ratées / 15 min. La
            // clé est l'identifiant NORMALISÉ (email minuscule / numéro E.164)
            // pour qu'on ne puisse pas réinitialiser le compteur en variant le
            // format de saisie (ex. "77…" vs "+221 77…" vs "00221…").
            var normId = isEmail ? emailKey : (phone ?? emailKey);
            var rlKey = $"login-fail:{normId}";
            if (IsRateLimited(rlKey, 5))
                return StatusCode(429, ApiResponse<bool>.Fail(
                    "Trop de tentatives. Réessayez dans quelques minutes."));

            // OrderBy(Id) : résolution déterministe si jamais un numéro/email était
            // partagé (le plus ancien compte gagne, jamais un ordre aléatoire).
            User? user;
            if (isEmail)
                user = await _context.Users.Include(u => u.School)
                    .Where(u => u.Email != null && u.Email.ToLower() == emailKey && !u.IsDeleted)
                    .OrderBy(u => u.Id).FirstOrDefaultAsync();
            else
                user = phone == null ? null : await _context.Users.Include(u => u.School)
                    .Where(u => u.PhoneNumber == phone && !u.IsDeleted)
                    .OrderBy(u => u.Id).FirstOrDefaultAsync();

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                RegisterAttempt(rlKey);
                return Unauthorized(ApiResponse<bool>.Fail("Identifiant ou mot de passe incorrect."));
            }

            // Identifiants corrects → on remet le compteur à zéro.
            ResetAttempts(rlKey);

            // Personnel « sans appli » (cuisinière, gardien…) : compte créé
            // uniquement pour le pointage, jamais autorisé à se connecter.
            if (!user.CanLogin)
                return Unauthorized(ApiResponse<bool>.Fail(
                    "Ce compte n'utilise pas l'application."));

            if (user.AccountStatus == AccountStatus.Suspended)
                return Unauthorized(ApiResponse<bool>.Fail("Votre compte a été suspendu. Contactez l'administration."));

            user.LastLoginAt = DateTime.UtcNow;
            // La préférence de langue suit la langue RÉELLE de l'app du
            // destinataire, réactualisée à chaque connexion. Sans ça, elle
            // resterait figée sur la langue de l'ADMIN qui a créé le compte
            // (héritage à l'invitation) et un parent arabophone d'un daara
            // francophone recevrait ses SMS mono-langue en français à vie.
            // Uniquement si le header est présent (l'app l'envoie toujours ;
            // un client nu ne doit pas écraser la préférence avec le défaut).
            if (!string.IsNullOrWhiteSpace(Request.Headers.AcceptLanguage))
                user.PreferredLanguage = HttpContext.GetPreferredLanguage();
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
        /// Envoie un code par SMS pour activer un compte téléphone ou réinitialiser
        /// son mot de passe. Réponse TOUJOURS générique (anti-énumération de
        /// numéros) : on n'indique jamais si un compte existe.
        /// </summary>
        [HttpPost("phone/request-code")]
        [AllowAnonymous]
        public async Task<IActionResult> RequestPhoneCode([FromBody] PhoneRequestCodeRequest request)
        {
            var generic = ApiResponse<bool>.Ok(true,
                "Si un compte existe pour ce numéro, un code a été envoyé par SMS.");

            var phone = Common.Utilities.SenegalPhone.Normalize(request.Phone);
            if (phone == null) return Ok(generic);

            // Anti-spam / abus de coût SMS : 3 envois max / 15 min par numéro.
            var rlKey = $"reqcode:{phone}";
            if (IsRateLimited(rlKey, 3)) return Ok(generic);

            var user = await _context.Users
                .Where(u => u.PhoneNumber == phone && !u.IsDeleted)
                .OrderBy(u => u.Id).FirstOrDefaultAsync();
            if (user == null) return Ok(generic);

            // Personnel « sans appli » (CanLogin=false) : aucun accès à l'app →
            // on n'envoie pas de code (réponse générique inchangée, anti-énumération).
            if (!user.CanLogin) return Ok(generic);

            await _otpService.GenerateAndSendSmsOtpAsync(
                phone, OtpPurpose.ResetPassword, user.Id, user.PreferredLanguage);
            RegisterAttempt(rlKey);
            return Ok(generic);
        }

        /// <summary>
        /// Définit (ou réinitialise) le mot de passe d'un compte téléphone après
        /// vérification du code SMS. Auto-login : retourne directement un
        /// LoginResponse complet.
        /// </summary>
        [HttpPost("phone/set-password")]
        [AllowAnonymous]
        public async Task<IActionResult> SetPhonePassword([FromBody] PhoneSetPasswordRequest request)
        {
            var phone = Common.Utilities.SenegalPhone.Normalize(request.Phone);
            if (phone == null)
                return BadRequest(ApiResponse<bool>.Fail("Numéro invalide."));

            // Anti brute-force du code à 6 chiffres : 5 essais ratés / 15 min.
            var rlKey = $"setpw-fail:{phone}";
            if (IsRateLimited(rlKey, 5))
                return StatusCode(429, ApiResponse<bool>.Fail(
                    "Trop de tentatives. Réessayez dans quelques minutes."));

            if (!await _otpService.VerifyOtpAsync(phone, request.Code, OtpPurpose.ResetPassword))
            {
                RegisterAttempt(rlKey);
                return BadRequest(ApiResponse<bool>.Fail("Code invalide ou expiré."));
            }
            ResetAttempts(rlKey);

            var user = await _context.Users.Include(u => u.School)
                .Where(u => u.PhoneNumber == phone && !u.IsDeleted)
                .OrderBy(u => u.Id).FirstOrDefaultAsync();
            if (user == null)
                return BadRequest(ApiResponse<bool>.Fail("Compte introuvable."));
            // Personnel « sans appli » : jamais autorisé à obtenir une session
            // (même garde qu'à Login — ce chemin auto-login le contournait).
            if (!user.CanLogin)
                return Unauthorized(ApiResponse<bool>.Fail("Ce compte n'utilise pas l'application."));
            if (user.AccountStatus == AccountStatus.Suspended)
                return Unauthorized(ApiResponse<bool>.Fail("Votre compte a été suspendu. Contactez l'administration."));

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            if (user.AccountStatus == AccountStatus.Inactive)
                user.AccountStatus = AccountStatus.Active;
            user.LastLoginAt = DateTime.UtcNow;
            // Même règle qu'à Login : la préférence suit la langue réelle de
            // l'app du destinataire (ce chemin auto-login est une connexion).
            if (!string.IsNullOrWhiteSpace(Request.Headers.AcceptLanguage))
                user.PreferredLanguage = HttpContext.GetPreferredLanguage();
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
            return Ok(ApiResponse<LoginResponse>.Ok(response, "Mot de passe défini avec succès."));
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
            // Personnel « sans appli » : le garde CanLogin doit valoir sur TOUS les
            // chemins d'émission de token (login, set-password, refresh).
            if (!user.CanLogin)
            {
                await _refreshTokens.RevokeAsync(newRefresh, "NoAppAccount");
                return Unauthorized(ApiResponse<bool>.Fail("Ce compte n'utilise pas l'application."));
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

                user.School.Name = Trimmed(request.SchoolName);
                user.School.NameAr = Trimmed(request.SchoolNameAr);
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
                Name = Trimmed(request.SchoolName),
                NameAr = Trimmed(request.SchoolNameAr),
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

            // Fondations paiement (wallet + settings) dès la création de l'école,
            // sans attendre le prochain redémarrage / le seed DbInitializer.
            await _context.EnsurePaymentFoundationsAsync(school.Id);

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
            foreach (var u in school.Users.Where(u => !u.IsDeleted))
                u.AccountStatus = AccountStatus.Active;

            await _context.SaveChangesAsync();

            // Abonnement plateforme : démarre l'essai gratuit de 30 jours dès la
            // validation (idempotent — ne recrée pas si l'école en a déjà un).
            await _context.EnsureSubscriptionAsync(school.Id);

            var adminUser = school.Users
                .Where(u => u.Role == UserRoles.SchoolAdmin && !u.IsDeleted)
                .OrderBy(u => u.CreatedAt)
                .FirstOrDefault();
            if (adminUser != null && school.Name != null)
            {
                // Envoi best-effort : la validation est commit en DB, on ne veut pas
                // retourner 500 si Gmail timeout (l'admin verra le statut dans l'app).
                try
                {
                    await _emailService.SendSchoolValidationEmailAsync(
                        adminUser.Email ?? string.Empty, school.Name, true, language: adminUser.PreferredLanguage);
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
                .Where(u => u.Role == UserRoles.SchoolAdmin && !u.IsDeleted)
                .OrderBy(u => u.CreatedAt)
                .FirstOrDefault();
            if (adminUser != null && school.Name != null)
            {
                // Idem ValidateSchool : envoi best-effort.
                try
                {
                    await _emailService.SendSchoolValidationEmailAsync(
                        adminUser.Email ?? string.Empty, school.Name, false, request.RejectionReason, adminUser.PreferredLanguage);
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

            foreach (var user in school.Users.Where(u => u.Role != UserRoles.Guardian && !u.IsDeleted))
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

            foreach (var user in school.Users.Where(u => u.Role != UserRoles.Guardian && !u.IsDeleted))
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

            var user = await _context.Users.Include(u => u.School)
                .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted);
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
                Users = school.Users.Where(u => !u.IsDeleted).Select(u => new { u.Email, u.Role, u.AccountStatus })
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

            var phone = SenegalPhone.Normalize(request.PhoneNumber);
            if (phone == null)
                return BadRequest(ApiResponse<bool>.Fail("Numéro de téléphone invalide."));
            if (await _context.Users.AnyAsync(u => u.PhoneNumber == phone && !u.IsDeleted))
                return BadRequest(ApiResponse<bool>.Fail("Ce numéro est déjà utilisé."));

            var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
            if (email != null && await _context.Users.AnyAsync(u => u.Email == email && !u.IsDeleted))
                return BadRequest(ApiResponse<bool>.Fail("Cet email est déjà utilisé."));

            // Code à 6 chiffres = mot de passe initial (non-expirant), envoyé par
            // SMS. L'utilisateur se connecte avec son numéro + ce code, puis
            // pourra le changer dans l'app.
            var code = SixDigitCode();

            var invitedStatus = request.Function == UserRoles.Guardian
                ? AccountStatus.Active
                : (currentUser.AccountStatus == AccountStatus.Active ? AccountStatus.Active : AccountStatus.Inactive);

            // Un Guardian n'est pas attaché à une école (multi-école possible).
            int? newUserSchoolId = request.Function == UserRoles.Guardian ? null : currentUser.SchoolId;

            // Personnel « sans appli » : pas de connexion → on n'envoie aucun code
            // et le mot de passe est aléatoire (jamais utilisable).
            var canLogin = request.CanLogin || request.Function == UserRoles.Guardian
                || request.Function == UserRoles.SchoolViewer;

            var newUser = new User
            {
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(canLogin ? code : Guid.NewGuid().ToString("N")),
                Role = request.Function,
                CanLogin = canLogin,
                JobTitle = string.IsNullOrWhiteSpace(request.JobTitle) ? null : request.JobTitle.Trim(),
                IsEmailVerified = true,
                AccountStatus = invitedStatus,
                SchoolId = newUserSchoolId,
                FullName = request.FullName,
                PhoneNumber = phone,
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

            // Compte sans appli : aucun identifiant à communiquer, aucun SMS.
            if (!canLogin)
            {
                return Ok(ApiResponse<UserCredentialDto>.Ok(new UserCredentialDto
                {
                    FullName = request.FullName,
                    Phone = phone,
                    CanLogin = false,
                }, "Personnel ajouté (sans accès à l'application)."));
            }

            // Message prêt à partager pour le modal récap (Copier / WhatsApp /
            // SMS). AUCUN envoi automatique ici (décision produit 2026-08-18) :
            // le SMS ne part que si l'école appuie sur le bouton « SMS » du modal
            // (endpoint credentials-sms ci-dessous) — le choix du canal lui
            // appartient, comme avant.
            var messageText = NotificationTemplates.CredentialShare(request.FullName, phone, code);

            return Ok(ApiResponse<UserCredentialDto>.Ok(new UserCredentialDto
            {
                UserId = newUser.Id,
                FullName = request.FullName,
                Phone = phone,
                Code = code,
                Message = messageText,
                CanLogin = true,
            }, "Utilisateur ajouté."));
        }

        /// <summary>
        /// Régénère le code d'accès à 6 chiffres d'un compte « identité par
        /// téléphone » (Teacher / SchoolStaff / Guardian) de l'école, pour un
        /// utilisateur qui a oublié le sien. Le nouveau code est renvoyé pour le
        /// modal récap (l'école le recommunique par WhatsApp) ET envoyé en SMS
        /// best-effort. Remplace le « mot de passe oublié par SMS » tant que le
        /// SMS auto n'est pas actif. SchoolAdmin/SchoolStaff only, scopé à l'école.
        /// Révoque les sessions existantes (l'ancien code ne marche plus).
        /// </summary>
        [Authorize(Roles = UserRoles.SchoolAdmin + "," + UserRoles.SchoolStaff)]
        [HttpPost("users/{userId}/regenerate-code")]
        public async Task<IActionResult> RegenerateAccessCode(int userId)
        {
            var currentUserId = User.GetUserId();
            if (currentUserId == null) return Unauthorized();

            var currentUser = await _context.Users.Include(u => u.School)
                .FirstOrDefaultAsync(u => u.Id == currentUserId);
            if (currentUser?.School == null || currentUser.SchoolId == null)
                return BadRequest(ApiResponse<UserCredentialDto>.Fail("École non trouvée pour cet utilisateur."));
            if (currentUser.AccountStatus != AccountStatus.Active)
                return BadRequest(ApiResponse<UserCredentialDto>.Fail("Votre compte doit être actif."));

            var target = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted);
            if (target == null)
                return NotFound(ApiResponse<UserCredentialDto>.Fail("Utilisateur introuvable."));

            // Seuls les comptes « identité par téléphone » ont un code à 6 chiffres.
            if (target.Role == UserRoles.SuperAdmin || target.Role == UserRoles.SchoolAdmin)
                return BadRequest(ApiResponse<UserCredentialDto>.Fail(
                    "Le code d'accès ne concerne que les parents, enseignants et personnel."));
            if (string.IsNullOrWhiteSpace(target.PhoneNumber))
                return BadRequest(ApiResponse<UserCredentialDto>.Fail(
                    "Ce compte n'a pas de numéro : aucun code à régénérer."));

            // Scoping strict multi-tenant : la cible appartient à mon école.
            // !Student.IsDeleted (oubli préexistant corrigé le 2026-08-17) ; un
            // enfant SORTI suffit en revanche — son parent utilise encore l'app
            // pour consulter et payer ce qu'il doit (D2).
            var belongsToSchool = target.Role == UserRoles.Guardian
                ? await _context.StudentGuardians.AnyAsync(
                    sg => sg.GuardianId == userId
                          && sg.Student.SchoolId == currentUser.SchoolId.Value
                          && !sg.Student.IsDeleted)
                : target.SchoolId == currentUser.SchoolId.Value;
            if (!belongsToSchool)
                return BadRequest(ApiResponse<UserCredentialDto>.Fail(
                    "Cet utilisateur n'appartient pas à votre école."));

            var code = SixDigitCode();
            target.PasswordHash = BCrypt.Net.BCrypt.HashPassword(code);
            await _context.SaveChangesAsync();

            // L'ancien code / mot de passe ne doit plus donner accès.
            await _context.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();

            var phone = target.PhoneNumber!;
            var fullName = target.FullName ?? string.Empty;
            // Message prêt à partager (modal récap). Aucun envoi automatique :
            // l'école choisit le canal dans le modal (WhatsApp / SMS / Copier).
            var messageText = NotificationTemplates.CredentialShare(fullName, phone, code);

            _logger.LogInformation(
                "[auth] Code d'accès régénéré pour user {UserId} ({Role}) par {AdminId} (école {SchoolId})",
                target.Id, target.Role, currentUserId, currentUser.SchoolId);

            return Ok(ApiResponse<UserCredentialDto>.Ok(new UserCredentialDto
            {
                UserId = target.Id,
                FullName = fullName,
                Phone = phone,
                Code = code,
                Message = messageText,
            }, "Nouveau code généré."));
        }

        /// <summary>
        /// Met à jour la langue préférée du compte CONNECTÉ. Appelé par l'app à
        /// chaque changement de langue : la préférence — qui décide de la langue
        /// des SMS et des push (règle d'or : la langue ACTUELLE du RÉCEPTEUR) —
        /// n'attend ainsi pas la prochaine connexion pour refléter la réalité.
        /// </summary>
        [Authorize]
        [HttpPost("me/language")]
        public async Task<IActionResult> UpdateMyLanguage([FromBody] UpdateLanguageRequest request)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var lang = (request.Language ?? string.Empty).Trim().ToLowerInvariant();
            if (lang != "fr" && lang != "ar")
                return BadRequest(ApiResponse<bool>.Fail("Langue non prise en charge."));

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted);
            if (user == null) return Unauthorized();

            user.PreferredLanguage = lang;
            await _context.SaveChangesAsync();
            return Ok(ApiResponse<bool>.Ok(true));
        }

        /// <summary>
        /// Envoie par SMS (via l'API Orange) les identifiants affichés dans le
        /// modal récap — déclenché par le bouton « SMS » du modal, JAMAIS
        /// automatiquement. Le code vient du client (il n'existe qu'en BCrypt côté
        /// serveur) mais est strictement validé : 6 chiffres ET vérifié contre le
        /// hash du compte cible — impossible d'utiliser l'endpoint comme relais de
        /// texte libre ou d'envoyer un code périmé. Le SMS part UNIQUEMENT vers le
        /// numéro enregistré du compte cible, scopé à l'école de l'appelant.
        /// </summary>
        [Authorize(Roles = UserRoles.SchoolAdmin + "," + UserRoles.SchoolStaff)]
        [HttpPost("users/{userId}/credentials-sms")]
        public async Task<IActionResult> SendCredentialsSms(int userId, [FromBody] SendCredentialsSmsRequest request)
        {
            var currentUserId = User.GetUserId();
            if (currentUserId == null) return Unauthorized();

            var currentUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == currentUserId && !u.IsDeleted);
            if (currentUser?.SchoolId == null)
                return BadRequest(ApiResponse<bool>.Fail("École non trouvée pour cet utilisateur."));
            if (currentUser.AccountStatus != AccountStatus.Active)
                return BadRequest(ApiResponse<bool>.Fail("Votre compte doit être actif."));

            // Anti-spam : 3 envois / cible / 15 min (une école qui mitraille le
            // même parent paie chaque SMS — et le rate-limit ferme aussi tout
            // usage de la vérification BCrypt comme oracle).
            var rlKey = $"credsms:{userId}";
            if (IsRateLimited(rlKey, max: 3))
                return BadRequest(ApiResponse<bool>.Fail(
                    "Trop d'envois vers ce compte. Réessayez dans quelques minutes."));
            RegisterAttempt(rlKey);

            var target = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted);
            if (target == null)
                return NotFound(ApiResponse<bool>.Fail("Utilisateur introuvable."));
            if (target.Role == UserRoles.SuperAdmin || target.Role == UserRoles.SchoolAdmin)
                return BadRequest(ApiResponse<bool>.Fail(
                    "Le code d'accès ne concerne que les parents, enseignants et personnel."));
            if (!target.CanLogin || string.IsNullOrWhiteSpace(target.PhoneNumber))
                return BadRequest(ApiResponse<bool>.Fail(
                    "Ce compte n'a pas de numéro ou pas d'accès à l'application."));

            // Scoping strict multi-tenant : la cible appartient à mon école
            // (même règle que regenerate-code — un enfant sorti suffit, D2).
            var belongsToSchool = target.Role == UserRoles.Guardian
                ? await _context.StudentGuardians.AnyAsync(
                    sg => sg.GuardianId == userId
                          && sg.Student.SchoolId == currentUser.SchoolId.Value
                          && !sg.Student.IsDeleted)
                : target.SchoolId == currentUser.SchoolId.Value;
            if (!belongsToSchool)
                return BadRequest(ApiResponse<bool>.Fail(
                    "Cet utilisateur n'appartient pas à votre école."));

            // Le code envoyé DOIT être le code actuel du compte : si l'école a
            // gardé un vieux modal ouvert après une régénération, on refuse
            // plutôt que d'envoyer un code qui ne marche plus.
            if (!BCrypt.Net.BCrypt.Verify(request.Code, target.PasswordHash))
                return BadRequest(ApiResponse<bool>.Fail(
                    "Ce code n'est plus valide. Régénérez un nouveau code."));

            var platform = await _context.GetPlatformSettingsAsync();
            // La langue est décidée par NotificationService (règle d'or, source
            // unique) : jamais connecté → bilingue ; sinon la langue ACTUELLE du
            // destinataire, relue en base. Ici on ne transmet que le réglage
            // plateforme (bilingue forcé partout s'il est ON).
            var sent = await _notif.SendSmsAsync(new NotificationSmsRequest(
                UserId: target.Id,
                RawPhone: target.PhoneNumber,
                PreferredLanguage: target.PreferredLanguage,
                Message: NotificationTemplates.CredentialsSms(
                    target.FullName ?? string.Empty, target.PhoneNumber, request.Code),
                Bilingual: platform.SmsBilingual,
                TemplateCode: "CREDENTIALS_SMS",
                RelatedEntityId: target.Id));

            if (!sent)
                return BadRequest(ApiResponse<bool>.Fail(
                    "L'envoi automatique du SMS a échoué. Vous pouvez l'envoyer manuellement."));

            _logger.LogInformation(
                "[auth] Identifiants envoyés par SMS à user {UserId} par {AdminId} (école {SchoolId})",
                target.Id, currentUserId, currentUser.SchoolId);
            return Ok(ApiResponse<bool>.Ok(true, "SMS envoyé."));
        }

        /// <summary>
        /// Champ texte optionnel normalisé : une chaîne vide ou faite d'espaces
        /// devient null. Sans ça, un nom arabe laissé vide s'enregistrerait comme
        /// chaîne vide et l'affichage bilingue croirait qu'un second nom existe
        /// (ligne vide sous le titre).
        /// </summary>
        private static string? Trimmed(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>Code à 6 chiffres (mot de passe initial des comptes téléphone).</summary>
        private static string SixDigitCode() =>
            System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        /// <summary>
        /// Change le mot de passe de l'utilisateur connecté (vérifie l'ancien).
        /// Sert notamment aux comptes téléphone pour remplacer leur code initial
        /// à 6 chiffres par un vrai mot de passe.
        /// </summary>
        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted);
            if (user == null) return Unauthorized();

            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
                return BadRequest(ApiResponse<bool>.Fail("Mot de passe actuel incorrect."));

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            await _context.SaveChangesAsync();
            return Ok(ApiResponse<bool>.Ok(true, "Mot de passe modifié avec succès."));
        }

        /// <summary>
        /// Vérifie le mot de passe de l'utilisateur connecté (re-auth légère).
        /// Utilisé comme verrou d'accès à l'espace paiement et comme step-up au
        /// retrait (en remplacement de l'OTP). Rate-limité par utilisateur pour
        /// empêcher le brute-force du mot de passe sur une session valide.
        /// </summary>
        [HttpPost("verify-password")]
        [Authorize]
        public async Task<IActionResult> VerifyPassword([FromBody] VerifyPasswordRequest request)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            // Clé PARTAGÉE avec le step-up retrait (SchoolWalletController) : un même
            // budget anti brute-force du mot de passe couvre gate + retrait.
            var rlKey = $"pwdreauth:{userId.Value}";
            if (IsRateLimited(rlKey, 5))
                return StatusCode(429, ApiResponse<bool>.Fail(
                    "Trop de tentatives. Réessayez dans quelques minutes."));

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted);
            if (user == null) return Unauthorized();

            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                RegisterAttempt(rlKey);
                return BadRequest(ApiResponse<bool>.Fail("Mot de passe incorrect."));
            }

            ResetAttempts(rlKey);
            return Ok(ApiResponse<bool>.Ok(true));
        }

        /// <summary>
        /// Recherche d'un Guardian existant par numéro de téléphone (chemin
        /// principal depuis Phase 2 — l'identité parent est le numéro) ou par
        /// email (rétro-compatibilité). Restreint aux Guardians ayant au moins
        /// un enfant rattaché à l'école courante (anti cross-tenant).
        /// </summary>
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        [HttpGet("search-guardian")]
        public async Task<IActionResult> SearchGuardian(
            [FromQuery] string? email = null,
            [FromQuery] string? phone = null)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            // Recherche par numéro normalisé E.164 en priorité, sinon par email.
            var normalizedPhone = SenegalPhone.Normalize(phone);
            var normalizedEmail = string.IsNullOrWhiteSpace(email)
                ? null
                : email.Trim().ToLowerInvariant();

            if (normalizedPhone == null && normalizedEmail == null)
                return NotFound();

            // Un Guardian est visible si l'un de ses enfants appartient à l'école courante.
            var guardian = await _context.Users
                .Where(u => u.Role == UserRoles.Guardian
                    && !u.IsDeleted
                    && (normalizedPhone != null
                            ? u.PhoneNumber == normalizedPhone
                            : u.Email != null && u.Email.ToLower() == normalizedEmail)
                    && _context.StudentGuardians.Any(sg =>
                        sg.GuardianId == u.Id
                        && sg.Student.SchoolId == schoolId.Value
                        && !sg.Student.IsDeleted))
                .OrderBy(u => u.Id)
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
