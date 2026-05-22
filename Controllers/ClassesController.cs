using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Class;
using Idara.API.DTOs.Common;
using Idara.API.Models;
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

        public ClassesController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<ActionResult<IEnumerable<ClassDto>>> GetClasses()
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            // Récupération en 2 requêtes (pas N+1) : la liste des classes,
            // puis un GROUP BY pour le nombre d'élèves par classe.
            var rawClasses = await _context.Classes
                .Where(c => c.SchoolId == schoolId.Value && !c.IsDeleted)
                .OrderBy(c => c.Name)
                .ToListAsync();

            var classIds = rawClasses.Select(c => c.Id).ToList();
            var counts = await _context.Students
                .Where(s => s.ClassId != null && classIds.Contains(s.ClassId.Value) && !s.IsDeleted)
                .GroupBy(s => s.ClassId!.Value)
                .Select(g => new { ClassId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ClassId, x => x.Count);

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
                StudentCount = counts.TryGetValue(c.Id, out var n) ? n : 0
            }).ToList();
            return Ok(classes);
        }

        [HttpGet("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff},{UserRoles.Teacher}")]
        public async Task<ActionResult<ClassDto>> GetClass(int id)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var c = await _context.Classes
                .FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == schoolId.Value && !c.IsDeleted);
            if (c == null) return NotFound();

            var studentCount = await _context.Students
                .CountAsync(s => s.ClassId == c.Id && !s.IsDeleted);

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
                StudentCount = studentCount
            });
        }

        [HttpPost]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<ClassDto>> CreateClass([FromBody] CreateClassDto dto)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var duplicate = await _context.Classes.AnyAsync(c =>
                c.SchoolId == schoolId.Value && !c.IsDeleted && c.Name.ToLower() == dto.Name.ToLower());
            if (duplicate)
                return BadRequest(ApiResponse<bool>.Fail("Une classe avec ce nom existe déjà."));

            var entity = new Class
            {
                Name = dto.Name,
                Description = dto.Description,
                Level = dto.Level,
                Capacity = dto.Capacity,
                SchoolId = schoolId.Value,
                CreatedAt = DateTime.UtcNow
            };
            _context.Classes.Add(entity);
            await _context.SaveChangesAsync();

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
                StudentCount = 0
            });
        }

        [HttpPut("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<ClassDto>> UpdateClass(int id, [FromBody] UpdateClassDto dto)
        {
            if (id != dto.Id)
                return BadRequest(ApiResponse<bool>.Fail("L'identifiant dans l'URL ne correspond pas à celui du corps de la requête."));

            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var entity = await _context.Classes
                .FirstOrDefaultAsync(c => c.Id == id && c.SchoolId == schoolId.Value && !c.IsDeleted);
            if (entity == null) return NotFound();

            var duplicate = await _context.Classes.AnyAsync(c =>
                c.SchoolId == schoolId.Value && !c.IsDeleted &&
                c.Id != id && c.Name.ToLower() == dto.Name.ToLower());
            if (duplicate)
                return BadRequest(ApiResponse<bool>.Fail("Une autre classe porte déjà ce nom."));

            entity.Name = dto.Name;
            entity.Description = dto.Description;
            entity.Level = dto.Level;
            entity.Capacity = dto.Capacity;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var studentCount = await _context.Students
                .CountAsync(s => s.ClassId == entity.Id && !s.IsDeleted);

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
                StudentCount = studentCount
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

            // Détacher les élèves de la classe avant suppression
            var students = await _context.Students.Where(s => s.ClassId == id).ToListAsync();
            foreach (var s in students) s.ClassId = null;

            entity.IsDeleted = true;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
