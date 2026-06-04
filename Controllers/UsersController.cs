using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Admin;
using Idara.API.DTOs.Common;
using Idara.API.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Gestion globale des utilisateurs — réservée au SuperAdmin.
    ///
    /// La suppression est <b>hybride</b> (cf. <see cref="DeleteUser"/>) :
    ///  - utilisateur vierge (aucune référence) → suppression physique ;
    ///  - utilisateur avec historique → anonymisation, car les entités
    ///    financières (Payment, WalletTransaction) sont append-only et ne
    ///    peuvent pas être effacées sans casser l'audit / la réconciliation
    ///    SenePay (gotcha §55).
    /// </summary>
    [ApiController]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<UsersController> _logger;

        public UsersController(AppDbContext context, ILogger<UsersController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Liste globale des utilisateurs (tous rôles, toutes écoles, Guardian
        /// inclus). Exclut les utilisateurs déjà supprimés (IsDeleted). Filtres
        /// optionnels : <paramref name="role"/> exact, <paramref name="search"/>
        /// (nom / prénom / email, insensible à la casse). Tri : plus récents
        /// d'abord. Cap à 500.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AdminUserListItemDto>>> GetUsers(
            [FromQuery] string? role,
            [FromQuery] string? search,
            CancellationToken ct)
        {
            var query = _context.Users.Where(u => !u.IsDeleted);

            if (!string.IsNullOrWhiteSpace(role))
                query = query.Where(u => u.Role == role);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(u =>
                    (u.Email != null && u.Email.ToLower().Contains(s)) ||
                    (u.FullName != null && u.FullName.ToLower().Contains(s)) ||
                    (u.FirstName != null && u.FirstName.ToLower().Contains(s)) ||
                    (u.LastName != null && u.LastName.ToLower().Contains(s)));
            }

            var items = await query
                .OrderByDescending(u => u.CreatedAt)
                .Take(500)
                .Select(u => new AdminUserListItemDto
                {
                    Id = u.Id,
                    Email = u.Email,
                    FullName = u.FullName,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    PhoneNumber = u.PhoneNumber,
                    Role = u.Role,
                    AccountStatus = u.AccountStatus,
                    SchoolId = u.SchoolId,
                    SchoolName = u.School != null ? u.School.Name : null,
                    CreatedAt = u.CreatedAt,
                    LastLoginAt = u.LastLoginAt
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        /// <summary>
        /// Supprime un utilisateur (SuperAdmin). Comportement hybride :
        ///  - <b>Hard delete</b> si l'utilisateur n'a AUCUNE référence (compte
        ///    de test, invité jamais utilisé, créé par erreur). La ligne est
        ///    physiquement supprimée ; ses RefreshToken partent en cascade.
        ///  - <b>Anonymisation</b> sinon : la ligne est conservée (intégrité
        ///    référentielle + financière append-only), mais IsDeleted=true,
        ///    PII effacées, email anonymisé (libère l'original pour réinscription),
        ///    PasswordHash régénéré aléatoirement (login impossible), sessions
        ///    révoquées.
        ///
        /// Garde-fous : on ne peut pas supprimer son propre compte ni un
        /// SuperAdmin (évite de se verrouiller dehors).
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<ActionResult<ApiResponse<UserDeletionResultDto>>> DeleteUser(
            int id, CancellationToken ct)
        {
            var currentUserId = User.GetUserId();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user == null || user.IsDeleted)
                return NotFound(ApiResponse<UserDeletionResultDto>.Fail("Utilisateur introuvable."));

            if (user.Id == currentUserId)
                return BadRequest(ApiResponse<UserDeletionResultDto>.Fail(
                    "Vous ne pouvez pas supprimer votre propre compte."));

            if (user.Role == UserRoles.SuperAdmin)
                return BadRequest(ApiResponse<UserDeletionResultDto>.Fail(
                    "Impossible de supprimer un compte SuperAdmin."));

            var hasReferences = await HasReferencesAsync(user.Id, ct);

            // Toujours révoquer les sessions actives, quel que soit le chemin.
            var tokens = _context.RefreshTokens.Where(t => t.UserId == user.Id);
            _context.RefreshTokens.RemoveRange(tokens);

            UserDeletionResultDto result;

            if (!hasReferences)
            {
                _context.Users.Remove(user);
                await _context.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "[users] Hard-delete User {UserId} ({Role}) par SuperAdmin {AdminId}",
                    id, user.Role, currentUserId);

                result = new UserDeletionResultDto
                {
                    UserId = id,
                    Action = "HardDeleted",
                    Message = "Utilisateur supprimé définitivement."
                };
            }
            else
            {
                // Les liens parent-élève ne sont PAS de l'historique financier :
                // on les retire pour qu'un parent supprimé disparaisse bien des
                // fiches élèves (l'historique Payment.GuardianId, lui, reste).
                var links = _context.StudentGuardians.Where(sg => sg.GuardianId == user.Id);
                _context.StudentGuardians.RemoveRange(links);

                // Anonymisation : on garde la ligne pour ne pas orpheliner les
                // références (paiements, journaux, audit), mais on efface tout
                // ce qui est personnel et on rend le compte inutilisable.
                user.IsDeleted = true;
                user.DeletedAt = DateTime.UtcNow;
                user.Email = $"deleted-{user.Id}@deleted.idara.local";
                user.FullName = null;
                user.FirstName = null;
                user.LastName = null;
                user.PhoneNumber = null;
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());
                user.AccountStatus = AccountStatus.Suspended;
                user.LastLoginAt = null;
                await _context.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "[users] Anonymisation User {UserId} ({Role}) par SuperAdmin {AdminId} (historique préservé)",
                    id, user.Role, currentUserId);

                result = new UserDeletionResultDto
                {
                    UserId = id,
                    Action = "Anonymized",
                    Message = "Utilisateur anonymisé (historique financier/pédagogique préservé)."
                };
            }

            return Ok(ApiResponse<UserDeletionResultDto>.Ok(result, result.Message));
        }

        /// <summary>
        /// Vrai si l'utilisateur est référencé n'importe où (FK contraintes OU
        /// colonnes d'audit sans FK). Détermine hard-delete vs anonymisation.
        /// Court-circuite dès la première référence trouvée. Ordonné du plus
        /// probable au moins probable selon les rôles courants.
        /// </summary>
        private async Task<bool> HasReferencesAsync(int userId, CancellationToken ct)
        {
            // Guardian : liens élèves + paiements.
            if (await _context.StudentGuardians.AnyAsync(x => x.GuardianId == userId, ct)) return true;
            if (await _context.Payments.AnyAsync(x => x.GuardianId == userId, ct)) return true;

            // Teacher : affectations + production pédagogique.
            if (await _context.ClassSubjectTeachers.AnyAsync(x => x.TeacherId == userId, ct)) return true;
            if (await _context.DailyJournalEntries.AnyAsync(x => x.TeacherId == userId, ct)) return true;
            if (await _context.CoranSessions.AnyAsync(x => x.TeacherId == userId, ct)) return true;
            if (await _context.TimetableSlots.AnyAsync(x => x.TeacherId == userId, ct)) return true;

            // Audit (sans FK déclarée — orphelinerait l'ID si on hard-delete).
            if (await _context.Attendances.AnyAsync(x => x.RecordedById == userId || x.DeletedById == userId, ct)) return true;
            if (await _context.Grades.AnyAsync(x => x.RecordedById == userId || x.DeletedById == userId, ct)) return true;
            if (await _context.CoranProgresses.AnyAsync(x => x.UpdatedById == userId, ct)) return true;
            if (await _context.ClassFees.AnyAsync(x => x.CreatedById == userId, ct)) return true;
            if (await _context.StudentFeeOverrides.AnyAsync(x => x.CreatedById == userId, ct)) return true;
            if (await _context.Schools.AnyAsync(x => x.ValidatedBy == userId, ct)) return true;

            return false;
        }
    }
}
