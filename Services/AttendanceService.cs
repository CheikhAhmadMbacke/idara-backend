using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    public sealed record AttendanceEntryInput(int StudentId, AttendanceStatus Status, string? Reason);

    public interface IAttendanceService
    {
        /// <summary>
        /// Pointage en lot d'une journée. Les élèves hors école, hors périmètre de
        /// l'appelant ou sortis sont ignorés (§16). Renvoie le nombre de
        /// pointages enregistrés.
        /// </summary>
        Task<int> RecordBulkAsync(int schoolId, int userId, string? role, DateTime date,
            IReadOnlyList<AttendanceEntryInput> entries, CancellationToken ct = default);
    }

    /// <summary>
    /// 📋 Le pointage (2026-10-08) — extrait d'<c>AttendancesController</c> pour
    /// que l'écran et l'assistant (« tous présents sauf Moussa ») suivent le
    /// MÊME chemin (§199) : même périmètre enseignant (§150), même résurrection
    /// des pointages supprimés (§40), même notification des absents.
    /// </summary>
    public class AttendanceService : IAttendanceService
    {
        private readonly AppDbContext _db;
        private readonly INotificationService _notif;

        public AttendanceService(AppDbContext db, INotificationService notif)
        {
            _db = db;
            _notif = notif;
        }

        public async Task<int> RecordBulkAsync(int schoolId, int userId, string? role, DateTime date,
            IReadOnlyList<AttendanceEntryInput> entries, CancellationToken ct = default)
        {
            date = date.ToUtcDay();
            var studentIds = entries.Select(e => e.StudentId).ToList();

            // Périmètre de l'appelant : un enseignant ne pointe que ses classes.
            var visible = await _db.VisibleClassIdsAsync(role, userId, schoolId, ct);

            var validStudentIds = await _db.Students
                .Where(s => studentIds.Contains(s.Id) && s.SchoolId == schoolId)
                .Enrolled()
                .Where(s => visible == null
                    || (s.ClassId != null && visible.Contains(s.ClassId.Value)))
                .Select(s => s.Id).ToListAsync(ct);

            // Les soft-deleted sont inclus pour pouvoir les « ressusciter » (§40).
            var existing = await _db.Attendances
                .Where(a => validStudentIds.Contains(a.StudentId) && a.Date == date)
                .ToDictionaryAsync(a => a.StudentId, ct);

            var saved = 0;
            var absentStudents = new HashSet<int>();
            foreach (var entry in entries.Where(e => validStudentIds.Contains(e.StudentId)))
            {
                if (entry.Status == AttendanceStatus.Absent)
                    absentStudents.Add(entry.StudentId);
                if (existing.TryGetValue(entry.StudentId, out var rec))
                {
                    rec.Status = entry.Status;
                    rec.Reason = entry.Reason;
                    rec.RecordedById = userId;
                    rec.RecordedAt = DateTime.UtcNow;
                    if (rec.IsDeleted)
                    {
                        rec.IsDeleted = false;
                        rec.DeletedAt = null;
                        rec.DeletedById = null;
                    }
                }
                else
                {
                    _db.Attendances.Add(new Attendance
                    {
                        SchoolId = schoolId,
                        StudentId = entry.StudentId,
                        Date = date,
                        Status = entry.Status,
                        Reason = entry.Reason,
                        RecordedById = userId,
                        RecordedAt = DateTime.UtcNow
                    });
                }
                saved++;
            }

            await _db.SaveChangesAsync(ct);

            // Notif parents des absents (push, post-commit, best-effort, 1/élève/jour).
            await NotifyAbsencesAsync(absentStudents);
            return saved;
        }

        private async Task NotifyAbsencesAsync(IReadOnlyCollection<int> absentStudentIds)
        {
            if (absentStudentIds.Count == 0) return;
            var names = await _db.Students
                .Where(s => absentStudentIds.Contains(s.Id))
                .Enrolled()
                .Select(s => new { s.Id, s.FirstName, s.LastName })
                .ToListAsync();
            foreach (var s in names)
            {
                var eleve = $"{s.FirstName} {s.LastName}".Trim();
                await _notif.NotifyGuardiansOfStudentAsync(
                    s.Id, NotificationTemplates.ChildAbsent(eleve),
                    "CHILD_ABSENCE", $"/guardian/children/{s.Id}", oncePerDay: true);
            }
        }
    }
}
