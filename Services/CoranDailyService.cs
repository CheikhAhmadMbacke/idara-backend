using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.DTOs.Operations;
using Idara.API.Models;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    public sealed record CoranDailyResult(bool Ok, string? Error, int? RecordId = null)
    {
        public static CoranDailyResult Fail(string e) => new(false, e);
    }

    public interface ICoranDailyService
    {
        /// <summary>Crée ou met à jour le suivi d'UNE journée d'un élève (portions + remarques).</summary>
        Task<CoranDailyResult> UpsertAsync(int schoolId, int userId, string? role, CoranDailyUpsertDto dto,
            CancellationToken ct = default);

        /// <summary>Le cycle ouvert de l'élève, créé s'il n'existe pas (course gérée).</summary>
        Task<CoranCycle> GetOrCreateOpenCycleAsync(int studentId, int schoolId, DateTime date);
    }

    /// <summary>
    /// 📖 Le suivi coranique quotidien (2026-10-08) — extrait de
    /// <c>CoranController</c> pour que l'écran et l'assistant (« Moussa a
    /// récité Al-Mulk 1 à 10, acquis ») suivent le MÊME chemin (§199) :
    /// périmètre enseignant (§150), garde « élève sorti » (D4), verrou 48 h
    /// (§151), clôture du cycle au 22ᵉ jour et notification des parents.
    /// </summary>
    public class CoranDailyService : ICoranDailyService
    {
        public const int CycleLength = 22; // jours ouvrés d'un cycle de suivi

        private readonly AppDbContext _db;
        private readonly INotificationService _notif;

        public CoranDailyService(AppDbContext db, INotificationService notif)
        {
            _db = db;
            _notif = notif;
        }

        public async Task<CoranDailyResult> UpsertAsync(int schoolId, int userId, string? role,
            CoranDailyUpsertDto dto, CancellationToken ct = default)
        {
            if (!await _db.CanAccessStudentAsync(role, userId, schoolId, dto.StudentId, ct))
                return CoranDailyResult.Fail("Élève introuvable ou hors de votre périmètre.");

            // Garde d'ÉCRITURE : plus de suivi quotidien sur un élève sorti (D4).
            if (!await _db.IsEnrolledAsync(dto.StudentId))
                return CoranDailyResult.Fail("Cet élève ne fait plus partie de l'effectif.");

            var kinds = dto.Portions.Select(p => p.Kind).ToList();
            if (kinds.Count != kinds.Distinct().Count())
                return CoranDailyResult.Fail("Chaque type de portion ne peut apparaître qu'une fois.");

            var date = dto.Date.ToUtcDay();
            var cycleJustCompleted = false;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var record = await _db.CoranDailyRecords
                .Include(r => r.Portions)
                .FirstOrDefaultAsync(r => r.StudentId == dto.StudentId && r.Date == date, ct);

            // Verrou 48 h (§151) : un enseignant ne peut plus REPRENDRE un suivi
            // saisi il y a plus de 48 h. Saisir en retard n'est pas modifier.
            if (record != null && !EditWindow.CanEdit(role, record.CreatedAt))
                return CoranDailyResult.Fail(EditWindow.RefusalMessage());

            if (record == null)
            {
                var cycle = await GetOrCreateOpenCycleAsync(dto.StudentId, schoolId, date);
                record = new CoranDailyRecord
                {
                    SchoolId = schoolId,
                    StudentId = dto.StudentId,
                    CycleId = cycle.Id,
                    Date = date,
                    RecordedById = userId,
                    CreatedAt = DateTime.UtcNow
                };
                _db.CoranDailyRecords.Add(record);
            }

            record.Remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();
            record.UpdatedAt = DateTime.UtcNow;

            // Remplacement intégral des portions (l'UI envoie l'état complet du jour).
            if (record.Portions.Count > 0)
                _db.CoranDailyPortions.RemoveRange(record.Portions);
            foreach (var p in dto.Portions)
            {
                record.Portions.Add(new CoranDailyPortion
                {
                    Kind = p.Kind,
                    FromSurah = p.FromSurah,
                    FromAyah = p.FromAyah,
                    FromWordIndex = p.FromWordIndex,
                    FromWordText = TrimOrNull(p.FromWordText),
                    ToSurah = p.ToSurah,
                    ToAyah = p.ToAyah,
                    ToWordIndex = p.ToWordIndex,
                    ToWordText = TrimOrNull(p.ToWordText),
                    Status = p.Status
                });
            }
            await _db.SaveChangesAsync(ct);

            // Clôture du cycle au 22ᵉ jour (transition unique).
            var cycleEntity = await _db.CoranCycles.FirstAsync(c => c.Id == record.CycleId, ct);
            if (!cycleEntity.IsComplete)
            {
                var count = await _db.CoranDailyRecords.CountAsync(r => r.CycleId == cycleEntity.Id, ct);
                if (count >= CycleLength)
                {
                    cycleEntity.IsComplete = true;
                    cycleEntity.CompletedDate = await _db.CoranDailyRecords
                        .Where(r => r.CycleId == cycleEntity.Id).MaxAsync(r => r.Date, ct);
                    await _db.SaveChangesAsync(ct);
                    cycleJustCompleted = true;
                }
            }

            await tx.CommitAsync(ct);

            // Notif parents (push, best-effort, post-commit) une seule fois à la clôture.
            if (cycleJustCompleted)
            {
                var st = await _db.Students
                    .Where(s => s.Id == dto.StudentId && !s.IsDeleted)
                    .Select(s => new { s.FirstName, s.LastName }).FirstOrDefaultAsync(ct);
                var eleve = st == null ? string.Empty : $"{st.FirstName} {st.LastName}".Trim();
                await _notif.NotifyGuardiansOfStudentAsync(
                    dto.StudentId, NotificationTemplates.ChildCoranCycleReady(eleve),
                    "CHILD_CORAN_CYCLE", $"/guardian/children/{dto.StudentId}", oncePerDay: false);
            }

            return new CoranDailyResult(true, null, record.Id);
        }

        public async Task<CoranCycle> GetOrCreateOpenCycleAsync(int studentId, int schoolId, DateTime date)
        {
            var open = await _db.CoranCycles
                .Where(c => c.StudentId == studentId && !c.IsComplete)
                .OrderByDescending(c => c.Number)
                .FirstOrDefaultAsync();
            if (open != null) return open;

            var maxNumber = await _db.CoranCycles
                .Where(c => c.StudentId == studentId)
                .Select(c => (int?)c.Number).MaxAsync() ?? 0;

            var cycle = new CoranCycle
            {
                SchoolId = schoolId,
                StudentId = studentId,
                Number = maxNumber + 1,
                StartDate = date,
                IsComplete = false
            };
            _db.CoranCycles.Add(cycle);
            try
            {
                await _db.SaveChangesAsync(); // besoin de l'Id pour la FK du record
                return cycle;
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505")
            {
                // Course entre deux saisies concurrentes (même élève, pas de cycle
                // ouvert) : l'index unique filtré a rejeté le 2ᵉ INSERT. On relit
                // le cycle ouvert créé par l'autre requête (§50/§71).
                _db.Entry(cycle).State = EntityState.Detached;
                var existing = await _db.CoranCycles
                    .Where(c => c.StudentId == studentId && !c.IsComplete)
                    .OrderByDescending(c => c.Number)
                    .FirstOrDefaultAsync();
                if (existing != null) return existing;
                throw;
            }
        }

        private static string? TrimOrNull(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
