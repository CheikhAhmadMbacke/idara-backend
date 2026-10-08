using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.Enums;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    public sealed record AccessCodeResult(bool Ok, string? Error, UserCredentialDto? Credential, bool NotFound = false)
    {
        public static AccessCodeResult Fail(string e, bool notFound = false) => new(false, e, null, notFound);
    }

    public interface IAccessCodeService
    {
        /// <summary>
        /// Remplace le code à 6 chiffres d'un compte « identité par téléphone »
        /// (parent, enseignant, personnel) de l'école, et révoque ses sessions.
        /// </summary>
        Task<AccessCodeResult> RegenerateAsync(int schoolId, int actorUserId, int targetUserId, CancellationToken ct = default);

        /// <summary>Envoie le code tout juste régénéré par SMS au numéro DU COMPTE — jamais à un autre.</summary>
        Task<bool> SendCodeSmsAsync(int schoolId, int actorUserId, int targetUserId, string code, string triggerSource, CancellationToken ct = default);
    }

    /// <summary>
    /// 🔑 Le code d'accès oublié (2026-10-08) — extrait d'<c>AuthController</c>
    /// pour que l'écran et l'assistant passent par le MÊME chemin (§199) :
    /// mêmes refus, même cloisonnement, même révocation des sessions.
    /// </summary>
    public class AccessCodeService : IAccessCodeService
    {
        private readonly AppDbContext _db;
        private readonly INotificationService _notif;
        private readonly ILogger<AccessCodeService> _logger;

        public AccessCodeService(AppDbContext db, INotificationService notif, ILogger<AccessCodeService> logger)
        {
            _db = db;
            _notif = notif;
            _logger = logger;
        }

        public async Task<AccessCodeResult> RegenerateAsync(
            int schoolId, int actorUserId, int targetUserId, CancellationToken ct = default)
        {
            var actor = await _db.Users.Where(u => u.Id == actorUserId && !u.IsDeleted)
                .Select(u => new { u.SchoolId, u.AccountStatus }).FirstOrDefaultAsync(ct);
            if (actor?.SchoolId != schoolId)
                return AccessCodeResult.Fail("École non trouvée pour cet utilisateur.");
            if (actor.AccountStatus != AccountStatus.Active)
                return AccessCodeResult.Fail("Votre compte doit être actif.");

            var target = await _db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId && !u.IsDeleted, ct);
            if (target == null) return AccessCodeResult.Fail("Utilisateur introuvable.", notFound: true);

            // Seuls les comptes « identité par téléphone » ont un code à 6 chiffres.
            if (target.Role == UserRoles.SuperAdmin || target.Role == UserRoles.SchoolAdmin)
                return AccessCodeResult.Fail("Le code d'accès ne concerne que les parents, enseignants et personnel.");
            if (string.IsNullOrWhiteSpace(target.PhoneNumber))
                return AccessCodeResult.Fail("Ce compte n'a pas de numéro : aucun code à régénérer.");

            if (!await BelongsToSchoolAsync(target.Id, target.Role, target.SchoolId, schoolId, ct))
                return AccessCodeResult.Fail("Cet utilisateur n'appartient pas à votre école.");

            var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            target.PasswordHash = BCrypt.Net.BCrypt.HashPassword(code);
            await _db.SaveChangesAsync(ct);

            // L'ancien code / mot de passe ne doit plus donner accès.
            await _db.RefreshTokens.Where(t => t.UserId == targetUserId).ExecuteDeleteAsync(ct);

            var phone = target.PhoneNumber!;
            var fullName = target.FullName ?? string.Empty;
            _logger.LogInformation(
                "[auth] Code d'accès régénéré pour user {UserId} ({Role}) par {AdminId} (école {SchoolId})",
                target.Id, target.Role, actorUserId, schoolId);

            return new AccessCodeResult(true, null, new UserCredentialDto
            {
                UserId = target.Id,
                FullName = fullName,
                Phone = phone,
                Code = code,
                Message = NotificationTemplates.CredentialShare(fullName, phone, code),
            });
        }

        public async Task<bool> SendCodeSmsAsync(
            int schoolId, int actorUserId, int targetUserId, string code, string triggerSource, CancellationToken ct = default)
        {
            var target = await _db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId && !u.IsDeleted, ct);
            if (target == null || !target.CanLogin || string.IsNullOrWhiteSpace(target.PhoneNumber)) return false;
            if (!await BelongsToSchoolAsync(target.Id, target.Role, target.SchoolId, schoolId, ct)) return false;
            // Le code envoyé doit être le code ACTUEL du compte.
            if (!BCrypt.Net.BCrypt.Verify(code, target.PasswordHash)) return false;

            var platform = await _db.GetPlatformSettingsAsync(ct);
            return await _notif.SendSmsAsync(new NotificationSmsRequest(
                UserId: target.Id,
                RawPhone: target.PhoneNumber,
                PreferredLanguage: target.PreferredLanguage,
                Message: NotificationTemplates.CredentialsSms(target.FullName ?? string.Empty, target.PhoneNumber, code),
                Bilingual: platform.SmsBilingual,
                TemplateCode: "CREDENTIALS_SMS",
                RelatedEntityId: target.Id,
                SchoolId: schoolId,
                TriggerSource: triggerSource,
                TriggerUserId: actorUserId), ct);
        }

        /// <summary>
        /// Scoping strict multi-tenant. Un parent appartient à l'école par ses
        /// enfants (un enfant SORTI suffit : il paie encore ce qu'il doit, D2) ;
        /// les autres comptes par leur école.
        /// </summary>
        private Task<bool> BelongsToSchoolAsync(int userId, string role, int? userSchoolId, int schoolId, CancellationToken ct) =>
            role == UserRoles.Guardian
                ? _db.StudentGuardians.AnyAsync(sg => sg.GuardianId == userId
                    && sg.Student.SchoolId == schoolId && !sg.Student.IsDeleted, ct)
                : Task.FromResult(userSchoolId == schoolId);
    }
}
