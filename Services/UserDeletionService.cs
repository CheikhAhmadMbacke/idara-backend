using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Admin;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    public interface IUserDeletionService
    {
        /// <summary>Vrai si l'utilisateur est référencé quelque part (FK ou colonnes d'audit).</summary>
        Task<bool> HasReferencesAsync(int userId, CancellationToken ct);

        /// <summary>
        /// Supprime l'utilisateur de façon hybride : hard-delete s'il n'a aucune
        /// référence, sinon anonymisation (PII effacées, email anonymisé, login
        /// bloqué, sessions révoquées, liens parent-élève retirés). N'effectue
        /// AUCUN contrôle d'autorisation : l'appelant doit avoir validé les
        /// droits et les garde-fous (pas d'auto-suppression, etc.) avant.
        /// </summary>
        Task<UserDeletionResultDto> DeleteOrAnonymizeAsync(User user, int actorId, CancellationToken ct);
    }

    public class UserDeletionService : IUserDeletionService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<UserDeletionService> _logger;

        public UserDeletionService(AppDbContext context, ILogger<UserDeletionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<bool> HasReferencesAsync(int userId, CancellationToken ct)
        {
            // Guardian : liens élèves + paiements.
            if (await _context.StudentGuardians.AnyAsync(x => x.GuardianId == userId, ct)) return true;
            if (await _context.Payments.AnyAsync(x => x.GuardianId == userId, ct)) return true;

            // Donateur : dons émis (FK Restrict Payment.DonorId → anonymiser, pas hard-delete).
            if (await _context.Payments.AnyAsync(x => x.DonorId == userId, ct)) return true;

            // Teacher : affectations + production pédagogique.
            if (await _context.ClassSubjectTeachers.AnyAsync(x => x.TeacherId == userId, ct)) return true;
            if (await _context.DailyJournalEntries.AnyAsync(x => x.TeacherId == userId || x.DeletedById == userId, ct)) return true;
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

        public async Task<UserDeletionResultDto> DeleteOrAnonymizeAsync(User user, int actorId, CancellationToken ct)
        {
            var hasReferences = await HasReferencesAsync(user.Id, ct);

            // Toujours révoquer les sessions actives, quel que soit le chemin.
            var tokens = _context.RefreshTokens.Where(t => t.UserId == user.Id);
            _context.RefreshTokens.RemoveRange(tokens);

            if (!hasReferences)
            {
                _context.Users.Remove(user);
                await _context.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "[user-delete] Hard-delete User {UserId} ({Role}) par {ActorId}",
                    user.Id, user.Role, actorId);

                return new UserDeletionResultDto
                {
                    UserId = user.Id,
                    Action = "HardDeleted",
                    Message = "Utilisateur supprimé définitivement."
                };
            }

            // Les liens parent-élève ne sont PAS de l'historique financier : on
            // les retire pour qu'un parent supprimé disparaisse des fiches élèves
            // (l'historique Payment.GuardianId, lui, reste préservé).
            var links = _context.StudentGuardians.Where(sg => sg.GuardianId == user.Id);
            _context.StudentGuardians.RemoveRange(links);

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
                "[user-delete] Anonymisation User {UserId} ({Role}) par {ActorId} (historique préservé)",
                user.Id, user.Role, actorId);

            return new UserDeletionResultDto
            {
                UserId = user.Id,
                Action = "Anonymized",
                Message = "Utilisateur anonymisé (historique financier/pédagogique préservé)."
            };
        }
    }
}
