using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>Ce qu'une école demande quand elle ajoute quelqu'un.</summary>
    /// <param name="Function">Rôle cible : voir <see cref="UserRoles"/>.</param>
    /// <param name="CanLogin">
    /// false = personnel « sans appli » (cuisinière, gardien) : compte créé pour
    /// le pointage, sans code ni connexion.
    /// </param>
    public sealed record InviteUserCommand(
        string FullName,
        string PhoneNumber,
        string Function,
        string? Email = null,
        bool CanLogin = true,
        string? JobTitle = null,
        int? StudentId = null,
        string? Relationship = null,
        bool IsPrimaryGuardian = false);

    /// <summary>
    /// Refus MÉTIER d'une invitation (numéro déjà pris, école non validée…).
    ///
    /// Distinguée d'une exception technique pour une raison précise : l'import
    /// doit pouvoir dire « ligne 34 : ce numéro est déjà utilisé » et continuer,
    /// alors qu'une panne doit, elle, remonter. Le <see cref="Exception.Message"/>
    /// est rédigé POUR UN DIRECTEUR — il est affiché tel quel.
    /// </summary>
    public sealed class InviteRejectedException : Exception
    {
        public InviteRejectedException(string message) : base(message) { }
    }

    public interface IUserInvitationService
    {
        /// <summary>
        /// Crée le compte et renvoie ses identifiants. N'ENVOIE RIEN : le choix
        /// du canal (WhatsApp, SMS, dictée) appartient à l'école (§160).
        /// </summary>
        /// <param name="inviter">
        /// L'auteur du geste. Sa navigation School peut ne pas être chargée : le
        /// statut KYC est alors relu en base plutôt que supposé valide.
        /// </param>
        /// <exception cref="InviteRejectedException">Refus métier, message affichable.</exception>
        Task<UserCredentialDto> InviteAsync(
            User inviter, InviteUserCommand cmd, CancellationToken ct = default);
    }

    /// <summary>
    /// Création d'un compte utilisateur par une école.
    ///
    /// <para><b>Pourquoi ce service existe.</b> Ces règles vivaient dans
    /// <c>AuthController.InviteUser</c>. Tant qu'il n'y avait qu'un appelant,
    /// c'était sans conséquence ; l'import en masse du personnel en fait un
    /// second. Les laisser dans le contrôleur aurait obligé l'import à
    /// ré-écrire la normalisation du numéro, l'unicité, le code à 6 chiffres, le
    /// statut hérité et la langue héritée — et c'est ainsi qu'un enseignant
    /// importé finit par différer d'un enseignant invité, six mois plus tard,
    /// sans que personne ne sache pourquoi.</para>
    ///
    /// <para>Même discipline que <c>StudentImportService</c>, qui appelle
    /// <c>CreateStudentAsync</c> plutôt que de recréer un élève à sa façon.</para>
    /// </summary>
    public class UserInvitationService : IUserInvitationService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<UserInvitationService> _logger;

        public UserInvitationService(AppDbContext context, ILogger<UserInvitationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<UserCredentialDto> InviteAsync(
            User inviter, InviteUserCommand cmd, CancellationToken ct = default)
        {
            if (inviter.SchoolId == null)
                throw new InviteRejectedException("École non trouvée pour cet utilisateur.");

            // La navigation School n'est pas toujours chargée selon l'appelant.
            // On la relit plutôt que de supposer : supposer « validée » ici
            // ouvrirait la création de comptes à une école non validée.
            var kyc = inviter.School?.KycStatus
                      ?? await _context.Schools
                          .Where(s => s.Id == inviter.SchoolId.Value)
                          .Select(s => (KycStatus?)s.KycStatus)
                          .FirstOrDefaultAsync(ct);
            if (kyc == null)
                throw new InviteRejectedException("École non trouvée pour cet utilisateur.");
            if (kyc != KycStatus.Validated)
                throw new InviteRejectedException("L'école doit être validée pour ajouter des utilisateurs.");

            if (inviter.AccountStatus != AccountStatus.Active)
                throw new InviteRejectedException("Votre compte doit être actif pour ajouter des utilisateurs.");

            // Guardian : l'élève doit exister DANS CETTE ÉCOLE (anti-énumération, §41).
            if (cmd.Function == UserRoles.Guardian)
            {
                if (!cmd.StudentId.HasValue)
                    throw new InviteRejectedException("StudentId requis pour inviter un Guardian.");

                var studentOk = await _context.Students.AnyAsync(
                    s => s.Id == cmd.StudentId.Value
                         && s.SchoolId == inviter.SchoolId
                         && !s.IsDeleted, ct);
                if (!studentOk)
                    throw new InviteRejectedException("Élève introuvable dans votre école.");
            }

            var phone = SenegalPhone.Normalize(cmd.PhoneNumber);
            if (phone == null)
                throw new InviteRejectedException("Numéro de téléphone invalide.");
            if (await _context.Users.AnyAsync(u => u.PhoneNumber == phone && !u.IsDeleted, ct))
                throw new InviteRejectedException("Ce numéro est déjà utilisé.");

            var email = string.IsNullOrWhiteSpace(cmd.Email) ? null : cmd.Email.Trim().ToLowerInvariant();
            if (email != null && await _context.Users.AnyAsync(u => u.Email == email && !u.IsDeleted, ct))
                throw new InviteRejectedException("Cet email est déjà utilisé.");

            // Code à 6 chiffres = mot de passe initial (non-expirant). Ce qui le
            // rend sûr n'est pas sa longueur mais le rate-limiting du login (§92).
            var code = SixDigitCode();

            var invitedStatus = cmd.Function == UserRoles.Guardian
                ? AccountStatus.Active
                : (inviter.AccountStatus == AccountStatus.Active
                    ? AccountStatus.Active
                    : AccountStatus.Inactive);

            // Un Guardian n'est attaché à AUCUNE école (il peut avoir des enfants
            // dans plusieurs).
            int? newUserSchoolId = cmd.Function == UserRoles.Guardian ? null : inviter.SchoolId;

            // Un parent et un observateur DOIVENT pouvoir se connecter : leur
            // compte n'a aucune autre utilité.
            var canLogin = cmd.CanLogin
                           || cmd.Function == UserRoles.Guardian
                           || cmd.Function == UserRoles.SchoolViewer;

            var newUser = new User
            {
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(
                    canLogin ? code : Guid.NewGuid().ToString("N")),
                Role = cmd.Function,
                CanLogin = canLogin,
                JobTitle = string.IsNullOrWhiteSpace(cmd.JobTitle) ? null : cmd.JobTitle.Trim(),
                IsEmailVerified = true,
                AccountStatus = invitedStatus,
                SchoolId = newUserSchoolId,
                FullName = cmd.FullName,
                PhoneNumber = phone,
                CreatedAt = DateTime.UtcNow,
                // Hérite de la langue de celui qui invite (env. culturellement homogène).
                PreferredLanguage = inviter.PreferredLanguage,
            };
            _context.Users.Add(newUser);
            await _context.SaveChangesAsync(ct);

            if (cmd.Function == UserRoles.Guardian && cmd.StudentId.HasValue)
            {
                _context.StudentGuardians.Add(new StudentGuardian
                {
                    StudentId = cmd.StudentId.Value,
                    GuardianId = newUser.Id,
                    Relationship = cmd.Relationship ?? string.Empty,
                    IsPrimaryGuardian = cmd.IsPrimaryGuardian,
                });
                await _context.SaveChangesAsync(ct);
            }

            _logger.LogInformation(
                "[invite] École {SchoolId} : compte {UserId} créé ({Role}, connexion {CanLogin}) par {InviterId}",
                inviter.SchoolId, newUser.Id, cmd.Function, canLogin, inviter.Id);

            // Compte sans appli : aucun identifiant à communiquer, aucun SMS.
            if (!canLogin)
            {
                return new UserCredentialDto
                {
                    UserId = newUser.Id,
                    FullName = cmd.FullName,
                    Phone = phone,
                    CanLogin = false,
                };
            }

            return new UserCredentialDto
            {
                UserId = newUser.Id,
                FullName = cmd.FullName,
                Phone = phone,
                Code = code,
                Message = NotificationTemplates.CredentialShare(cmd.FullName, phone, code),
                CanLogin = true,
            };
        }

        /// <summary>Code à 6 chiffres (mot de passe initial des comptes téléphone).</summary>
        private static string SixDigitCode() =>
            System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
    }
}
