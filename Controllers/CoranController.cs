using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Operations;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CoranController : ControllerBase
    {
        private const int CycleLength = 22; // jours ouvrés d'un cycle de suivi

        private readonly AppDbContext _context;
        private readonly INotificationService _notif;
        public CoranController(AppDbContext context, INotificationService notif)
        {
            _context = context;
            _notif = notif;
        }

        // ----- Progress (1 par élève) -----

        // Endpoints LEGACY (progress/sessions) : plus appelés par l'app (remplacés
        // par cycles/daily/evaluations). Verrouillés à SchoolAdmin+SchoolStaff pour
        // fermer le trou de scoping enseignant (un Teacher pouvait lire/écrire le
        // suivi de tout élève de l'école via StudentBelongsToSchool). Admin/Staff
        // ont un accès école-large légitime. Conservés (données existantes en prod).
        [HttpGet("progress/{studentId}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> GetProgress(int studentId)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            if (!await StudentBelongsToSchool(studentId, schoolId.Value))
                return NotFound();

            var p = await _context.CoranProgresses.FirstOrDefaultAsync(x => x.StudentId == studentId);
            // 204 explicite : "ressource trouvée mais aucune représentation".
            // Évite l'ambiguïté d'`Ok((CoranProgressDto?)null)` qui suivant le
            // pipeline ASP.NET pouvait sortir soit 204 vide, soit 200 + body
            // `null` (literal), soit un body que Dio interprétait comme String
            // → crash `String is not a subtype of Map<String, dynamic>`.
            if (p == null) return NoContent();
            return Ok(MapProgress(p));
        }

        [HttpPut("progress/{studentId}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> UpsertProgress(int studentId, [FromBody] CoranProgressUpsertDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            if (!await StudentBelongsToSchool(studentId, schoolId.Value))
                return NotFound();

            // Garde d'ÉCRITURE : plus de saisie Coran sur un élève sorti (D4).
            if (!await _context.IsEnrolledAsync(studentId))
                return BadRequest(ApiResponse<bool>.Fail("Cet élève ne fait plus partie de l'effectif."));

            var p = await _context.CoranProgresses.FirstOrDefaultAsync(x => x.StudentId == studentId);
            if (p == null)
            {
                p = new CoranProgress
                {
                    SchoolId = schoolId.Value,
                    StudentId = studentId,
                };
                _context.CoranProgresses.Add(p);
            }
            p.CurrentSurah = dto.CurrentSurah;
            p.CurrentVerse = dto.CurrentVerse;
            p.CurrentHizb = dto.CurrentHizb;
            p.CurrentJuz = dto.CurrentJuz;
            p.MemorizedFromSurah = dto.MemorizedFromSurah;
            p.MemorizedFromVerse = dto.MemorizedFromVerse;
            p.MemorizedToSurah = dto.MemorizedToSurah;
            p.MemorizedToVerse = dto.MemorizedToVerse;
            p.Notes = dto.Notes;
            p.UpdatedById = userId.Value;
            p.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(MapProgress(p));
        }

        // ----- Sessions -----

        [HttpGet("sessions")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> GetSessions([FromQuery] int? studentId, [FromQuery] int? classId)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var query = _context.CoranSessions
                .Include(s => s.Student).Include(s => s.Teacher)
                .Where(s => s.SchoolId == schoolId.Value);

            if (studentId.HasValue) query = query.Where(s => s.StudentId == studentId.Value);
            if (classId.HasValue) query = query.Where(s => s.Student.ClassId == classId.Value);

            var items = await query.OrderByDescending(s => s.Date).ToListAsync();
            return Ok(items.Select(MapSession));
        }

        [HttpPost("sessions")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> CreateSession([FromBody] CoranSessionCreateDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            if (!await StudentBelongsToSchool(dto.StudentId, schoolId.Value))
                return BadRequest(ApiResponse<bool>.Fail("Élève introuvable."));

            // Garde d'ÉCRITURE : plus de séance sur un élève sorti (D4).
            if (!await _context.IsEnrolledAsync(dto.StudentId))
                return BadRequest(ApiResponse<bool>.Fail("Cet élève ne fait plus partie de l'effectif."));

            // Au moins un score doit être fourni (sinon la session n'a aucun sens
            // et ne mettra pas à jour les moyennes).
            if (!dto.RecitationScore.HasValue && !dto.TajwidScore.HasValue && !dto.MemorizationScore.HasValue)
                return BadRequest(ApiResponse<bool>.Fail(
                    "Au moins un score (récitation, tajwid ou mémorisation) doit être renseigné."));

            var entity = new CoranSession
            {
                SchoolId = schoolId.Value,
                StudentId = dto.StudentId,
                Date = dto.Date.ToUtcSafe(),
                Kind = dto.Kind,
                FromSurah = dto.FromSurah,
                FromVerse = dto.FromVerse,
                ToSurah = dto.ToSurah,
                ToVerse = dto.ToVerse,
                RecitationScore = dto.RecitationScore,
                TajwidScore = dto.TajwidScore,
                MemorizationScore = dto.MemorizationScore,
                Comment = dto.Comment,
                TeacherId = userId.Value
            };

            // Transaction : la session ET la mise à jour des moyennes doivent
            // réussir ensemble (sinon CoranProgress reste désynchronisé).
            using var tx = await _context.Database.BeginTransactionAsync();

            _context.CoranSessions.Add(entity);
            await _context.SaveChangesAsync();

            await UpdateProgressAverages(dto.StudentId, schoolId.Value, userId.Value);

            await tx.CommitAsync();

            var saved = await _context.CoranSessions
                .Include(s => s.Student).Include(s => s.Teacher)
                .FirstAsync(s => s.Id == entity.Id);
            return Ok(MapSession(saved));
        }

        [HttpDelete("sessions/{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> DeleteSession(int id)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var entity = await _context.CoranSessions
                .FirstOrDefaultAsync(s => s.Id == id && s.SchoolId == schoolId.Value);
            if (entity == null) return NotFound();

            using var tx = await _context.Database.BeginTransactionAsync();

            _context.CoranSessions.Remove(entity);
            await _context.SaveChangesAsync();

            // Les moyennes de progress doivent refléter la suppression.
            await UpdateProgressAverages(entity.StudentId, schoolId.Value, userId.Value);

            await tx.CommitAsync();
            return NoContent();
        }

        // ===================================================================
        // Suivi quotidien (cycles de 22 jours ouvrés) — approche daara
        // ===================================================================

        /// <summary>Liste des cycles d'un élève (du plus récent au plus ancien).</summary>
        [HttpGet("cycles")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> GetCycles([FromQuery] int studentId)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();
            if (!await CanAccessStudentAsync(User.GetRole(), userId.Value, schoolId.Value, studentId))
                return NotFound();

            var cycles = await _context.CoranCycles
                .Where(c => c.StudentId == studentId)
                .OrderByDescending(c => c.Number)
                .Select(c => new CoranCycleDto
                {
                    Id = c.Id,
                    StudentId = c.StudentId,
                    Number = c.Number,
                    StartDate = c.StartDate,
                    CompletedDate = c.CompletedDate,
                    IsComplete = c.IsComplete,
                    DayCount = c.Records.Count
                })
                .ToListAsync();
            return Ok(cycles);
        }

        /// <summary>Jours de suivi d'un élève (optionnellement filtrés sur un cycle).</summary>
        [HttpGet("daily")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> GetDaily([FromQuery] int studentId, [FromQuery] int? cycleId)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();
            if (!await CanAccessStudentAsync(User.GetRole(), userId.Value, schoolId.Value, studentId))
                return NotFound();

            var records = await _context.CoranDailyRecords
                .Include(r => r.Portions)
                .Include(r => r.Cycle)
                .Where(r => r.StudentId == studentId && (cycleId == null || r.CycleId == cycleId.Value))
                .OrderByDescending(r => r.Date)
                .ToListAsync();

            return Ok(MapDailyList(records, IsTeacherLocked(User.GetRole())));
        }

        /// <summary>Crée ou met à jour le suivi d'UNE journée (les 3 portions + remarques).</summary>
        [HttpPut("daily")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> UpsertDaily([FromBody] CoranDailyUpsertDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();
            if (!await CanAccessStudentAsync(User.GetRole(), userId.Value, schoolId.Value, dto.StudentId))
                return BadRequest(ApiResponse<bool>.Fail("Élève introuvable ou hors de votre périmètre."));

            // Garde d'ÉCRITURE : plus de suivi quotidien sur un élève sorti (D4).
            // La consultation de ses cycles reste libre.
            if (!await _context.IsEnrolledAsync(dto.StudentId))
                return BadRequest(ApiResponse<bool>.Fail("Cet élève ne fait plus partie de l'effectif."));

            var kinds = dto.Portions.Select(p => p.Kind).ToList();
            if (kinds.Count != kinds.Distinct().Count())
                return BadRequest(ApiResponse<bool>.Fail("Chaque type de portion ne peut apparaître qu'une fois."));

            var date = dto.Date.ToUtcDay();
            var cycleJustCompleted = false;

            await using var tx = await _context.Database.BeginTransactionAsync();

            var record = await _context.CoranDailyRecords
                .Include(r => r.Portions)
                .FirstOrDefaultAsync(r => r.StudentId == dto.StudentId && r.Date == date);

            // Verrou 48 h (§151) : un enseignant ne peut plus REPRENDRE un suivi
            // saisi il y a plus de 48 h. La création d'une journée ancienne
            // reste permise — saisir en retard n'est pas modifier.
            if (record != null && IsTeacherLocked(User.GetRole()) && !IsWithinEditWindow(record))
            {
                return BadRequest(ApiResponse<bool>.Fail(
                    $"Ce suivi a été enregistré il y a plus de {EditWindowHours} heures : il n'est plus modifiable. " +
                    "Demandez à la direction du daara de le corriger."));
            }

            if (record == null)
            {
                var cycle = await GetOrCreateOpenCycleAsync(dto.StudentId, schoolId.Value, date);
                record = new CoranDailyRecord
                {
                    SchoolId = schoolId.Value,
                    StudentId = dto.StudentId,
                    CycleId = cycle.Id,
                    Date = date,
                    RecordedById = userId.Value,
                    CreatedAt = DateTime.UtcNow
                };
                _context.CoranDailyRecords.Add(record);
            }

            record.Remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();
            record.UpdatedAt = DateTime.UtcNow;

            // Remplacement intégral des portions (l'UI envoie l'état complet du jour).
            if (record.Portions.Count > 0)
                _context.CoranDailyPortions.RemoveRange(record.Portions);
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
            await _context.SaveChangesAsync();

            // Clôture du cycle au 22ᵉ jour (transition unique).
            var cycleEntity = await _context.CoranCycles.FirstAsync(c => c.Id == record.CycleId);
            if (!cycleEntity.IsComplete)
            {
                var count = await _context.CoranDailyRecords.CountAsync(r => r.CycleId == cycleEntity.Id);
                if (count >= CycleLength)
                {
                    cycleEntity.IsComplete = true;
                    cycleEntity.CompletedDate = await _context.CoranDailyRecords
                        .Where(r => r.CycleId == cycleEntity.Id).MaxAsync(r => r.Date);
                    await _context.SaveChangesAsync();
                    cycleJustCompleted = true;
                }
            }

            await tx.CommitAsync();

            // Notif parents (push, best-effort, post-commit) une seule fois à la clôture.
            if (cycleJustCompleted)
            {
                // !IsDeleted manquait ici (oubli préexistant corrigé le
                // 2026-08-17) ; le sortant est déjà exclu en amont (garde
                // d'écriture) et au centre (NotificationService).
                var st = await _context.Students
                    .Where(s => s.Id == dto.StudentId && !s.IsDeleted)
                    .Select(s => new { s.FirstName, s.LastName }).FirstOrDefaultAsync();
                var eleve = st == null ? string.Empty : $"{st.FirstName} {st.LastName}".Trim();
                await _notif.NotifyGuardiansOfStudentAsync(
                    dto.StudentId, NotificationTemplates.ChildCoranCycleReady(eleve),
                    "CHILD_CORAN_CYCLE", $"/guardian/children/{dto.StudentId}", oncePerDay: false);
            }

            var saved = await _context.CoranDailyRecords
                .Include(r => r.Portions).Include(r => r.Cycle)
                .FirstAsync(r => r.Id == record.Id);
            var dayIndex = await _context.CoranDailyRecords
                .CountAsync(r => r.CycleId == saved.CycleId && r.Date <= saved.Date);
            return Ok(MapDaily(saved, dayIndex, IsTeacherLocked(User.GetRole())));
        }

        /// <summary>Supprime le suivi d'une journée.</summary>
        [HttpDelete("daily/{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> DeleteDaily(int id)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var record = await _context.CoranDailyRecords
                .FirstOrDefaultAsync(r => r.Id == id && r.SchoolId == schoolId.Value);
            if (record == null) return NotFound();
            if (!await CanAccessStudentAsync(User.GetRole(), userId.Value, schoolId.Value, record.StudentId))
                return Forbid();

            // Même verrou que la modification (§151) — sans lui, il suffirait de
            // supprimer puis de re-saisir pour contourner le délai.
            if (IsTeacherLocked(User.GetRole()) && !IsWithinEditWindow(record))
            {
                return BadRequest(ApiResponse<bool>.Fail(
                    $"Ce suivi a été enregistré il y a plus de {EditWindowHours} heures : il n'est plus supprimable. " +
                    "Demandez à la direction du daara."));
            }

            // Portions supprimées en cascade (FK Cascade). On ne réouvre PAS un
            // cycle déjà clôturé (le parent l'a peut-être déjà consulté).
            _context.CoranDailyRecords.Remove(record);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ===================================================================
        // Évaluation qualité (par hizb) — création SchoolAdmin ; lecture
        // Admin/Personnel/Enseignant (enseignant scopé à ses classes).
        // ===================================================================

        [HttpGet("evaluations")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<IActionResult> GetEvaluations([FromQuery] int studentId)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();
            if (!await CanAccessStudentAsync(User.GetRole(), userId.Value, schoolId.Value, studentId))
                return NotFound();

            var items = await _context.CoranEvaluations
                .Include(e => e.Incidents)
                .Include(e => e.Evaluator)
                .Include(e => e.Student)
                .Where(e => e.StudentId == studentId && e.SchoolId == schoolId.Value)
                .OrderByDescending(e => e.Date)
                .ToListAsync();
            return Ok(items.Select(MapEvaluation));
        }

        [HttpPost("evaluations")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<IActionResult> CreateEvaluation([FromBody] CoranEvaluationCreateDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();
            if (!await StudentBelongsToSchool(dto.StudentId, schoolId.Value))
                return BadRequest(ApiResponse<bool>.Fail("Élève introuvable."));

            // Garde d'ÉCRITURE : plus d'évaluation sur un élève sorti (D4).
            if (!await _context.IsEnrolledAsync(dto.StudentId))
                return BadRequest(ApiResponse<bool>.Fail("Cet élève ne fait plus partie de l'effectif."));

            var entity = new CoranEvaluation
            {
                SchoolId = schoolId.Value,
                StudentId = dto.StudentId,
                EvaluatorId = userId.Value,
                Date = dto.Date.ToUtcDay(),
                HizbNumber = dto.HizbNumber,
                FromSurah = dto.FromSurah,
                FromAyah = dto.FromAyah,
                FromWordIndex = dto.FromWordIndex,
                FromWordText = TrimOrNull(dto.FromWordText),
                ToSurah = dto.ToSurah,
                ToAyah = dto.ToAyah,
                ToWordIndex = dto.ToWordIndex,
                ToWordText = TrimOrNull(dto.ToWordText),
                RecitationScore = dto.RecitationScore,
                TajwidScore = dto.TajwidScore,
                MemorizationScore = dto.MemorizationScore,
                Comment = TrimOrNull(dto.Comment),
                CreatedAt = DateTime.UtcNow,
                Incidents = dto.Incidents.Select(i => new CoranEvaluationIncident
                {
                    Kind = i.Kind,
                    // Le type de faute n'a de sens que pour une faute (pas un blocage).
                    FaultType = i.Kind == CoranIncidentKind.Fault ? i.FaultType : null,
                    Surah = i.Surah,
                    Ayah = i.Ayah,
                    WordIndex = i.WordIndex,
                    WordText = TrimOrNull(i.WordText),
                    Detail = TrimOrNull(i.Detail)
                }).ToList()
            };
            _context.CoranEvaluations.Add(entity);
            await _context.SaveChangesAsync();

            var saved = await _context.CoranEvaluations
                .Include(e => e.Incidents).Include(e => e.Evaluator).Include(e => e.Student)
                .FirstAsync(e => e.Id == entity.Id);
            return Ok(MapEvaluation(saved));
        }

        [HttpDelete("evaluations/{id}")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<IActionResult> DeleteEvaluation(int id)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();
            var entity = await _context.CoranEvaluations
                .FirstOrDefaultAsync(e => e.Id == id && e.SchoolId == schoolId.Value);
            if (entity == null) return NotFound();

            _context.CoranEvaluations.Remove(entity); // incidents en cascade
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private static CoranEvaluationDto MapEvaluation(CoranEvaluation e) => new()
        {
            Id = e.Id,
            StudentId = e.StudentId,
            StudentName = e.Student != null ? $"{e.Student.FirstName} {e.Student.LastName}".Trim() : string.Empty,
            EvaluatorName = e.Evaluator?.FullName ?? e.Evaluator?.Email,
            Date = e.Date,
            HizbNumber = e.HizbNumber,
            FromSurah = e.FromSurah,
            FromAyah = e.FromAyah,
            FromWordIndex = e.FromWordIndex,
            FromWordText = e.FromWordText,
            ToSurah = e.ToSurah,
            ToAyah = e.ToAyah,
            ToWordIndex = e.ToWordIndex,
            ToWordText = e.ToWordText,
            RecitationScore = e.RecitationScore,
            TajwidScore = e.TajwidScore,
            MemorizationScore = e.MemorizationScore,
            Comment = e.Comment,
            Incidents = e.Incidents.Select(i => new CoranEvaluationIncidentDto
            {
                Kind = i.Kind,
                FaultType = i.FaultType,
                Surah = i.Surah,
                Ayah = i.Ayah,
                WordIndex = i.WordIndex,
                WordText = i.WordText,
                Detail = i.Detail
            }).ToList()
        };

        // ----- Helpers suivi quotidien -----

        private async Task<CoranCycle> GetOrCreateOpenCycleAsync(int studentId, int schoolId, DateTime date)
        {
            var open = await _context.CoranCycles
                .Where(c => c.StudentId == studentId && !c.IsComplete)
                .OrderByDescending(c => c.Number)
                .FirstOrDefaultAsync();
            if (open != null) return open;

            var maxNumber = await _context.CoranCycles
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
            _context.CoranCycles.Add(cycle);
            try
            {
                await _context.SaveChangesAsync(); // besoin de l'Id pour la FK du record
                return cycle;
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505")
            {
                // Course entre deux UpsertDaily concurrents (même élève, pas de cycle
                // ouvert) : l'index unique filtré (1 cycle ouvert/élève) a rejeté le
                // 2ᵉ INSERT. On détache l'entité échouée et on relit le cycle ouvert
                // créé par l'autre requête (idempotence, cf. pattern §50/§71).
                _context.Entry(cycle).State = EntityState.Detached;
                var existing = await _context.CoranCycles
                    .Where(c => c.StudentId == studentId && !c.IsComplete)
                    .OrderByDescending(c => c.Number)
                    .FirstOrDefaultAsync();
                if (existing != null) return existing;
                throw; // cas improbable : conflit mais aucun cycle ouvert retrouvé
            }
        }

        /// <summary>
        /// Vrai si l'appelant peut accéder à cet élève. Délègue au socle partagé
        /// <see cref="AcademicScopeExtensions"/> — cette logique existait ici en
        /// double avant août 2026, ce qui privait l'Observateur (dont
        /// <c>GetRole()</c> renvoie « SchoolViewer ») de tout accès au suivi
        /// Coran alors qu'il doit voir ce que voit le directeur.
        /// </summary>
        private Task<bool> CanAccessStudentAsync(string? role, int userId, int schoolId, int studentId)
            => _context.CanAccessStudentAsync(role, userId, schoolId, studentId);

        private static string? TrimOrNull(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        // ===================================================================
        // Verrou d'édition du suivi quotidien (§151)
        // ===================================================================

        /// <summary>
        /// Délai pendant lequel un enseignant peut encore corriger un suivi
        /// qu'il a saisi. Au-delà, seule la direction (SchoolAdmin /
        /// SchoolStaff) peut intervenir : un verrou absolu graverait la moindre
        /// faute de frappe repérée trop tard.
        /// </summary>
        public const int EditWindowHours = 48;

        /// <summary>
        /// Le délai part de la <b>saisie</b> (<c>CreatedAt</c>), pas de la date
        /// de la journée : un maître qui reporte le jeudi la journée de lundi
        /// doit garder ses 48 h pour se relire. Et surtout PAS de
        /// <c>UpdatedAt</c> — chaque modification repousserait la fenêtre, le
        /// verrou ne se fermerait jamais.
        /// </summary>
        private static bool IsWithinEditWindow(CoranDailyRecord r) =>
            DateTime.UtcNow - r.CreatedAt <= TimeSpan.FromHours(EditWindowHours);

        /// <summary>Seul l'enseignant est soumis au délai.</summary>
        private static bool IsTeacherLocked(string? role) => role == UserRoles.Teacher;

        /// <summary>Mappe une liste de records en calculant le DayIndex (1..22)
        /// par ordre de date au sein de chaque cycle.</summary>
        private static List<CoranDailyRecordDto> MapDailyList(
            List<CoranDailyRecord> records, bool teacherLocked = false)
        {
            // index par cycle : position chronologique (ascendante) du jour.
            var indexByRecord = new Dictionary<int, int>();
            foreach (var grp in records.GroupBy(r => r.CycleId))
            {
                var i = 0;
                foreach (var r in grp.OrderBy(r => r.Date))
                    indexByRecord[r.Id] = ++i;
            }
            return records
                .Select(r => MapDaily(
                    r,
                    indexByRecord.TryGetValue(r.Id, out var di) ? di : 0,
                    teacherLocked))
                .ToList();
        }

        private static CoranDailyRecordDto MapDaily(
            CoranDailyRecord r, int dayIndex, bool teacherLocked = false) => new()
        {
            Id = r.Id,
            StudentId = r.StudentId,
            StudentName = r.Student != null ? $"{r.Student.FirstName} {r.Student.LastName}".Trim() : string.Empty,
            CycleId = r.CycleId,
            CycleNumber = r.Cycle?.Number ?? 0,
            DayIndex = dayIndex,
            Date = r.Date,
            Remarks = r.Remarks,
            // Verrou d'édition calculé PAR LE SERVEUR (§151) : l'app ne le
            // recalcule pas, sinon un téléphone à l'heure fausse verrouillerait
            // ou déverrouillerait à tort. Toujours modifiable pour la direction.
            Editable = !teacherLocked || IsWithinEditWindow(r),
            EditWindowHours = EditWindowHours,
            Portions = r.Portions
                .OrderBy(p => p.Kind)
                .Select(p => new CoranDailyPortionDto
                {
                    Kind = p.Kind,
                    FromSurah = p.FromSurah,
                    FromAyah = p.FromAyah,
                    FromWordIndex = p.FromWordIndex,
                    FromWordText = p.FromWordText,
                    ToSurah = p.ToSurah,
                    ToAyah = p.ToAyah,
                    ToWordIndex = p.ToWordIndex,
                    ToWordText = p.ToWordText,
                    Status = p.Status
                }).ToList()
        };

        // ----- Helpers -----

        private async Task<bool> StudentBelongsToSchool(int studentId, int schoolId) =>
            await _context.Students.AnyAsync(s => s.Id == studentId && s.SchoolId == schoolId && !s.IsDeleted);

        private async Task UpdateProgressAverages(int studentId, int schoolId, int userId)
        {
            var sessions = await _context.CoranSessions
                .Where(s => s.StudentId == studentId && s.SchoolId == schoolId)
                .ToListAsync();
            if (sessions.Count == 0) return;

            // Ne calculer une moyenne QUE s'il existe au moins une session portant
            // ce score. Sinon laisser null (sans ça, DefaultIfEmpty().Average() sur
            // une séquence vide renvoyait 0.0 → fausse moyenne « 0/5 » affichée).
            var recScores = sessions.Where(s => s.RecitationScore.HasValue).Select(s => (double)s.RecitationScore!.Value).ToList();
            var tajScores = sessions.Where(s => s.TajwidScore.HasValue).Select(s => (double)s.TajwidScore!.Value).ToList();

            var p = await _context.CoranProgresses.FirstOrDefaultAsync(x => x.StudentId == studentId);
            if (p == null)
            {
                p = new CoranProgress { SchoolId = schoolId, StudentId = studentId };
                _context.CoranProgresses.Add(p);
            }
            p.AverageRecitation = recScores.Count > 0 ? Math.Round(recScores.Average(), 2) : null;
            p.AverageTajwid = tajScores.Count > 0 ? Math.Round(tajScores.Average(), 2) : null;
            p.UpdatedAt = DateTime.UtcNow;
            p.UpdatedById = userId;
            await _context.SaveChangesAsync();
        }

        private static CoranProgressDto MapProgress(CoranProgress p) => new()
        {
            Id = p.Id,
            StudentId = p.StudentId,
            CurrentSurah = p.CurrentSurah,
            CurrentVerse = p.CurrentVerse,
            CurrentHizb = p.CurrentHizb,
            CurrentJuz = p.CurrentJuz,
            MemorizedFromSurah = p.MemorizedFromSurah,
            MemorizedFromVerse = p.MemorizedFromVerse,
            MemorizedToSurah = p.MemorizedToSurah,
            MemorizedToVerse = p.MemorizedToVerse,
            AverageRecitation = p.AverageRecitation,
            AverageTajwid = p.AverageTajwid,
            Notes = p.Notes,
            UpdatedAt = p.UpdatedAt
        };

        private static CoranSessionDto MapSession(CoranSession s) => new()
        {
            Id = s.Id,
            StudentId = s.StudentId,
            StudentName = s.Student != null ? $"{s.Student.FirstName} {s.Student.LastName}" : string.Empty,
            Date = s.Date,
            Kind = s.Kind,
            FromSurah = s.FromSurah,
            FromVerse = s.FromVerse,
            ToSurah = s.ToSurah,
            ToVerse = s.ToVerse,
            RecitationScore = s.RecitationScore,
            TajwidScore = s.TajwidScore,
            MemorizationScore = s.MemorizationScore,
            Comment = s.Comment,
            TeacherName = s.Teacher?.FullName ?? s.Teacher?.Email
        };
    }
}
