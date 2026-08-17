using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Class;
using Idara.API.DTOs.Common;
using Idara.API.Models;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ClassesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IInvoiceRepricingService _repricing;
        private readonly ILogger<ClassesController> _logger;

        public ClassesController(
            AppDbContext context,
            IInvoiceRepricingService repricing,
            ILogger<ClassesController> logger)
        {
            _context = context;
            _repricing = repricing;
            _logger = logger;
        }

        /// <summary>
        /// Tarif COURANT de chaque classe fournie (le <c>ClassFee</c> au
        /// <c>EffectiveFrom</c> le plus récent déjà en vigueur, départagé par Id
        /// décroissant sur égalité de date — cf. gotcha §107). Une seule requête,
        /// et la MÊME règle de sélection que <see cref="Services.FeeResolver"/> :
        /// le montant affiché sur la classe est celui qui sera facturé.
        /// </summary>
        private async Task<Dictionary<int, long>> CurrentClassFeesAsync(
            int schoolId, IReadOnlyCollection<int> classIds, CancellationToken ct = default)
        {
            if (classIds.Count == 0) return new Dictionary<int, long>();

            var today = DateTime.UtcNow.Date;
            var rows = await _context.ClassFees
                .Where(f => f.SchoolId == schoolId
                            && classIds.Contains(f.ClassId)
                            && f.EffectiveFrom <= today)
                .GroupBy(f => f.ClassId)
                .Select(g => new
                {
                    ClassId = g.Key,
                    Amount = g.OrderByDescending(f => f.EffectiveFrom)
                              .ThenByDescending(f => f.Id)
                              .First().AmountFcfa
                })
                .ToListAsync(ct);

            return rows.ToDictionary(x => x.ClassId, x => x.Amount);
        }

        [HttpGet]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher},{UserRoles.Surveillant}")]
        public async Task<ActionResult<IEnumerable<ClassDto>>> GetClasses()
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // Un enseignant ne voit QUE ses classes affectées (§150). `null` =
            // aucune restriction ; une liste VIDE veut dire « aucune classe
            // affectée » et doit renvoyer une liste vide, jamais toute l'école.
            var visible = await _context.VisibleClassIdsAsync(
                User.GetRole(), userId.Value, schoolId.Value);

            // Récupération en 2 requêtes (pas N+1) : la liste des classes,
            // puis un GROUP BY pour le nombre d'élèves par classe.
            var rawClasses = await _context.Classes
                .Where(c => c.SchoolId == schoolId.Value && !c.IsDeleted)
                .Where(c => visible == null || visible.Contains(c.Id))
                .OrderBy(c => c.Name)
                .ToListAsync();

            var classIds = rawClasses.Select(c => c.Id).ToList();
            // Enrolled() : l'effectif d'une classe = les élèves encore là.
            var counts = await _context.Students
                .Where(s => s.ClassId != null && classIds.Contains(s.ClassId.Value))
                .Enrolled()
                .GroupBy(s => s.ClassId!.Value)
                .Select(g => new { ClassId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ClassId, x => x.Count);

            var fees = await CurrentClassFeesAsync(schoolId.Value, classIds);

            var classes = rawClasses.Select(c => new ClassDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                Level = c.Level,
                Capacity = c.Capacity,
                SchoolId = c.SchoolId,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                StudentCount = counts.TryGetValue(c.Id, out var n) ? n : 0,
                MonthlyFeeFcfa = fees.TryGetValue(c.Id, out var fee) ? fee : null
            }).ToList();
            return Ok(classes);
        }

        [HttpGet("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher},{UserRoles.Surveillant}")]
        public async Task<ActionResult<ClassDto>> GetClass(int id)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // Contrôle unitaire indispensable : filtrer la LISTE sans filtrer ici
            // laisserait ouvrir n'importe quelle classe en devinant son id.
            if (!await _context.CanAccessClassAsync(
                    User.GetRole(), userId.Value, schoolId.Value, id))
                return NotFound();

            var c = await _context.Classes
                .FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == schoolId.Value && !c.IsDeleted);
            if (c == null) return NotFound();

            var studentCount = await _context.Students
                .Where(s => s.ClassId == c.Id).Enrolled()
                .CountAsync();
            var fees = await CurrentClassFeesAsync(schoolId.Value, new[] { c.Id });

            return Ok(new ClassDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                Level = c.Level,
                Capacity = c.Capacity,
                SchoolId = c.SchoolId,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                StudentCount = studentCount,
                MonthlyFeeFcfa = fees.TryGetValue(c.Id, out var fee) ? fee : null
            });
        }

        [HttpPost]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<ClassDto>> CreateClass([FromBody] CreateClassDto dto)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var duplicate = await _context.Classes.AnyAsync(c =>
                c.SchoolId == schoolId.Value && !c.IsDeleted && c.Name.ToLower() == dto.Name.ToLower());
            if (duplicate)
                return BadRequest(ApiResponse<bool>.Fail("Une classe avec ce nom existe déjà."));

            var now = DateTime.UtcNow;
            var entity = new Class
            {
                Name = dto.Name,
                Description = dto.Description,
                Level = dto.Level,
                Capacity = dto.Capacity,
                SchoolId = schoolId.Value,
                CreatedAt = now
            };
            _context.Classes.Add(entity);
            await _context.SaveChangesAsync();

            // Mensualité saisie à la création → première version du tarif de la
            // classe. Écrite dans ClassFee, la même table que l'écran « Tarif
            // par classe » : une classe créée avec un tarif y apparaît
            // immédiatement, avec son historique.
            if (dto.MonthlyFeeFcfa is > 0)
            {
                _context.ClassFees.Add(new ClassFee
                {
                    ClassId = entity.Id,
                    SchoolId = schoolId.Value,
                    AmountFcfa = dto.MonthlyFeeFcfa.Value,
                    EffectiveFrom = now.ToUtcDay(),
                    CreatedById = userId.Value,
                    CreatedAt = now
                });
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "[classes] Tarif initial posé à la création : SchoolId={SchoolId} ClassId={ClassId} Amount={Amount}",
                    schoolId, entity.Id, dto.MonthlyFeeFcfa.Value);
            }

            // Pas de re-tarification : une classe qui vient d'être créée n'a
            // encore aucun élève, donc aucune facture à réaligner.

            return CreatedAtAction(nameof(GetClass), new { id = entity.Id }, new ClassDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                Level = entity.Level,
                Capacity = entity.Capacity,
                SchoolId = entity.SchoolId,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                StudentCount = 0,
                MonthlyFeeFcfa = dto.MonthlyFeeFcfa is > 0 ? dto.MonthlyFeeFcfa : null
            });
        }

        [HttpPut("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<ClassDto>> UpdateClass(int id, [FromBody] UpdateClassDto dto)
        {
            if (id != dto.Id)
                return BadRequest(ApiResponse<bool>.Fail("L'identifiant dans l'URL ne correspond pas à celui du corps de la requête."));

            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            var entity = await _context.Classes
                .FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == schoolId.Value && !c.IsDeleted);
            if (entity == null) return NotFound();

            var duplicate = await _context.Classes.AnyAsync(c =>
                c.SchoolId == schoolId.Value && !c.IsDeleted &&
                c.Id != id && c.Name.ToLower() == dto.Name.ToLower());
            if (duplicate)
                return BadRequest(ApiResponse<bool>.Fail("Une autre classe porte déjà ce nom."));

            var now = DateTime.UtcNow;
            entity.Name = dto.Name;
            entity.Description = dto.Description;
            entity.Level = dto.Level;
            entity.Capacity = dto.Capacity;
            entity.UpdatedAt = now;

            // Tarif : nouvelle VERSION seulement si le montant a réellement
            // changé. Enregistrer le formulaire sans toucher au montant
            // n'empile pas une ligne d'historique identique.
            var currentFees = await CurrentClassFeesAsync(schoolId.Value, new[] { entity.Id });
            var currentFee = currentFees.TryGetValue(entity.Id, out var cf) ? (long?)cf : null;
            var feeChanged = dto.MonthlyFeeFcfa is > 0 && dto.MonthlyFeeFcfa != currentFee;

            if (feeChanged)
            {
                _context.ClassFees.Add(new ClassFee
                {
                    ClassId = entity.Id,
                    SchoolId = schoolId.Value,
                    AmountFcfa = dto.MonthlyFeeFcfa!.Value,
                    EffectiveFrom = now.ToUtcDay(),
                    CreatedById = userId.Value,
                    CreatedAt = now
                });
                currentFee = dto.MonthlyFeeFcfa;
            }

            await _context.SaveChangesAsync();

            if (feeChanged)
            {
                _logger.LogInformation(
                    "[classes] Tarif de classe modifié depuis la fiche classe : SchoolId={SchoolId} ClassId={ClassId} Amount={Amount}",
                    schoolId, entity.Id, dto.MonthlyFeeFcfa);

                // Même garde-fou que l'écran « Tarif par classe » : les factures
                // impayées des élèves de la classe suivent le nouveau montant.
                // Sans cela, changer le tarif depuis la fiche classe et depuis
                // l'écran des tarifs ne produirait pas le même résultat.
                var classStudentIds = await _context.Students
                    .Where(s => s.ClassId == entity.Id && s.SchoolId == schoolId.Value)
                    .Enrolled()
                    .Select(s => s.Id)
                    .ToListAsync();
                await _repricing.RepriceUnpaidInvoicesAsync(
                    schoolId.Value, classStudentIds, CancellationToken.None);
            }

            var studentCount = await _context.Students
                .Where(s => s.ClassId == entity.Id).Enrolled()
                .CountAsync();

            return Ok(new ClassDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                Level = entity.Level,
                Capacity = entity.Capacity,
                SchoolId = entity.SchoolId,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                StudentCount = studentCount,
                MonthlyFeeFcfa = currentFee
            });
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> DeleteClass(int id)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var entity = await _context.Classes
                .FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == schoolId.Value && !c.IsDeleted);
            if (entity == null) return NotFound();

            // Détacher les élèves de la classe avant suppression — TOUS, y
            // compris supprimés et sortis : la classe disparaît pour tout le
            // monde, une fiche archivée ne doit pas pointer une classe morte.
            // ⚠️ SchoolId ajouté (oubli préexistant corrigé le 2026-08-17) : sans
            // lui, un identifiant de classe d'une AUTRE école — déjà refusé plus
            // haut, mais la défense en profondeur ne coûte qu'un prédicat.
            var students = await _context.Students
                .Where(s => s.ClassId == id && s.SchoolId == schoolId.Value)
                .ToListAsync();
            foreach (var s in students) s.ClassId = null;

            entity.IsDeleted = true;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
