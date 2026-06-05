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
        private readonly IUserDeletionService _userDeletion;
        private readonly ILogger<UsersController> _logger;

        public UsersController(
            AppDbContext context,
            IUserDeletionService userDeletion,
            ILogger<UsersController> logger)
        {
            _context = context;
            _userDeletion = userDeletion;
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

            var result = await _userDeletion.DeleteOrAnonymizeAsync(user, currentUserId ?? 0, ct);
            return Ok(ApiResponse<UserDeletionResultDto>.Ok(result, result.Message));
        }
    }
}
