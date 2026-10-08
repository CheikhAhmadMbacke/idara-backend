using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.DTOs.Class;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    public sealed record ClassCreationResult(bool Ok, string? Error, Class? Class = null);

    public interface IClassCreationService
    {
        Task<ClassCreationResult> CreateAsync(int schoolId, int userId, CreateClassDto dto, CancellationToken ct = default);
    }

    /// <summary>
    /// 🏫 Créer une classe (2026-10-08) — extrait de <c>ClassesController</c>
    /// pour que l'écran et l'assistant suivent le MÊME chemin (§199) : même
    /// refus du doublon de nom, même tarif initial écrit dans <c>ClassFee</c>.
    /// </summary>
    public class ClassCreationService : IClassCreationService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ClassCreationService> _logger;

        public ClassCreationService(AppDbContext db, ILogger<ClassCreationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ClassCreationResult> CreateAsync(int schoolId, int userId, CreateClassDto dto, CancellationToken ct = default)
        {
            var duplicate = await _db.Classes.AnyAsync(c =>
                c.SchoolId == schoolId && !c.IsDeleted && c.Name.ToLower() == dto.Name.ToLower(), ct);
            if (duplicate)
                return new(false, "Une classe avec ce nom existe déjà.");

            var now = DateTime.UtcNow;
            var entity = new Class
            {
                Name = dto.Name,
                Description = dto.Description,
                Level = dto.Level,
                Capacity = dto.Capacity,
                SchoolId = schoolId,
                CreatedAt = now
            };
            _db.Classes.Add(entity);
            await _db.SaveChangesAsync(ct);

            // Mensualité saisie à la création → première version du tarif de la
            // classe, dans la même table que l'écran « Tarif par classe ».
            if (dto.MonthlyFeeFcfa is > 0)
            {
                _db.ClassFees.Add(new ClassFee
                {
                    ClassId = entity.Id,
                    SchoolId = schoolId,
                    AmountFcfa = dto.MonthlyFeeFcfa.Value,
                    EffectiveFrom = now.ToUtcDay(),
                    CreatedById = userId,
                    CreatedAt = now
                });
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "[classes] Tarif initial posé à la création : SchoolId={SchoolId} ClassId={ClassId} Amount={Amount}",
                    schoolId, entity.Id, dto.MonthlyFeeFcfa.Value);
            }

            // Pas de re-tarification : une classe neuve n'a aucun élève.
            return new(true, null, entity);
        }
    }
}
