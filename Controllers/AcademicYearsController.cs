using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Academic;
using Idara.API.DTOs.Common;
using Idara.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/academic-years")]
    public class AcademicYearsController : ControllerBase
    {
        private readonly AppDbContext _context;
        public AcademicYearsController(AppDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var years = await _context.AcademicYears
                .Where(y => y.SchoolId == schoolId.Value)
                .OrderByDescending(y => y.StartDate)
                .Include(y => y.Periods.OrderBy(p => p.OrderIndex))
                .ToListAsync();

            return Ok(years.Select(MapYear));
        }

        [HttpGet("current")]
        public async Task<IActionResult> GetCurrent()
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var year = await _context.AcademicYears
                .Where(y => y.SchoolId == schoolId.Value && y.IsCurrent)
                .Include(y => y.Periods.OrderBy(p => p.OrderIndex))
                .FirstOrDefaultAsync();

            return year == null ? NotFound(ApiResponse<bool>.Fail("Aucune année courante.")) : Ok(MapYear(year));
        }

        [HttpPost]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Create([FromBody] AcademicYearCreateDto dto)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            if (dto.EndDate <= dto.StartDate)
                return BadRequest(ApiResponse<bool>.Fail("La date de fin doit être après la date de début."));

            // Transaction : unset puis set IsCurrent doivent être atomiques pour
            // ne pas violer l'index unique filtré (SchoolId, IsCurrent = 1).
            using var tx = await _context.Database.BeginTransactionAsync();

            if (dto.IsCurrent)
            {
                await UnsetCurrentYear(schoolId.Value);
                await _context.SaveChangesAsync();
            }

            var entity = new AcademicYear
            {
                SchoolId = schoolId.Value,
                Name = dto.Name.Trim(),
                StartDate = dto.StartDate.ToUtcSafe(),
                EndDate = dto.EndDate.ToUtcSafe(),
                IsCurrent = dto.IsCurrent,
                CreatedAt = DateTime.UtcNow
            };
            _context.AcademicYears.Add(entity);
            await _context.SaveChangesAsync();

            await tx.CommitAsync();
            return Ok(MapYear(entity));
        }

        [HttpPut("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Update(int id, [FromBody] AcademicYearUpdateDto dto)
        {
            if (id != dto.Id) return BadRequest(ApiResponse<bool>.Fail("ID mismatch."));
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            if (dto.EndDate <= dto.StartDate)
                return BadRequest(ApiResponse<bool>.Fail("La date de fin doit être après la date de début."));

            var entity = await _context.AcademicYears.Include(y => y.Periods)
                .FirstOrDefaultAsync(y => y.Id == id && y.SchoolId == schoolId.Value);
            if (entity == null) return NotFound();

            using var tx = await _context.Database.BeginTransactionAsync();

            if (dto.IsCurrent && !entity.IsCurrent)
            {
                await UnsetCurrentYear(schoolId.Value);
                await _context.SaveChangesAsync();
            }

            entity.Name = dto.Name.Trim();
            entity.StartDate = dto.StartDate.ToUtcSafe();
            entity.EndDate = dto.EndDate.ToUtcSafe();
            entity.IsCurrent = dto.IsCurrent;
            await _context.SaveChangesAsync();

            await tx.CommitAsync();
            return Ok(MapYear(entity));
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Delete(int id)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var entity = await _context.AcademicYears
                .FirstOrDefaultAsync(y => y.Id == id && y.SchoolId == schoolId.Value);
            if (entity == null) return NotFound();

            _context.AcademicYears.Remove(entity);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ----- Periods -----

        [HttpPost("{yearId}/periods")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> CreatePeriod(int yearId, [FromBody] AcademicPeriodCreateDto dto)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            if (dto.EndDate <= dto.StartDate)
                return BadRequest(ApiResponse<bool>.Fail("La date de fin doit être après la date de début."));

            var year = await _context.AcademicYears
                .FirstOrDefaultAsync(y => y.Id == yearId && y.SchoolId == schoolId.Value);
            if (year == null) return NotFound(ApiResponse<bool>.Fail("Année introuvable."));

            using var tx = await _context.Database.BeginTransactionAsync();

            if (dto.IsCurrent)
            {
                await UnsetCurrentPeriod(schoolId.Value);
                await _context.SaveChangesAsync();
            }

            var entity = new AcademicPeriod
            {
                SchoolId = schoolId.Value,
                AcademicYearId = yearId,
                Name = dto.Name.Trim(),
                StartDate = dto.StartDate.ToUtcSafe(),
                EndDate = dto.EndDate.ToUtcSafe(),
                OrderIndex = dto.OrderIndex,
                IsCurrent = dto.IsCurrent
            };
            _context.AcademicPeriods.Add(entity);
            await _context.SaveChangesAsync();

            await tx.CommitAsync();
            return Ok(MapPeriod(entity));
        }

        [HttpPut("periods/{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> UpdatePeriod(int id, [FromBody] AcademicPeriodUpdateDto dto)
        {
            if (id != dto.Id) return BadRequest(ApiResponse<bool>.Fail("ID mismatch."));
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            if (dto.EndDate <= dto.StartDate)
                return BadRequest(ApiResponse<bool>.Fail("La date de fin doit être après la date de début."));

            var entity = await _context.AcademicPeriods
                .FirstOrDefaultAsync(p => p.Id == id && p.SchoolId == schoolId.Value);
            if (entity == null) return NotFound();

            using var tx = await _context.Database.BeginTransactionAsync();

            if (dto.IsCurrent && !entity.IsCurrent)
            {
                await UnsetCurrentPeriod(schoolId.Value);
                await _context.SaveChangesAsync();
            }

            entity.Name = dto.Name.Trim();
            entity.StartDate = dto.StartDate.ToUtcSafe();
            entity.EndDate = dto.EndDate.ToUtcSafe();
            entity.OrderIndex = dto.OrderIndex;
            entity.IsCurrent = dto.IsCurrent;
            await _context.SaveChangesAsync();

            await tx.CommitAsync();
            return Ok(MapPeriod(entity));
        }

        [HttpDelete("periods/{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> DeletePeriod(int id)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var entity = await _context.AcademicPeriods
                .FirstOrDefaultAsync(p => p.Id == id && p.SchoolId == schoolId.Value);
            if (entity == null) return NotFound();

            _context.AcademicPeriods.Remove(entity);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ----- Helpers -----

        private async Task UnsetCurrentYear(int schoolId)
        {
            var current = await _context.AcademicYears
                .Where(y => y.SchoolId == schoolId && y.IsCurrent).ToListAsync();
            foreach (var c in current) c.IsCurrent = false;
        }

        private async Task UnsetCurrentPeriod(int schoolId)
        {
            var current = await _context.AcademicPeriods
                .Where(p => p.SchoolId == schoolId && p.IsCurrent).ToListAsync();
            foreach (var c in current) c.IsCurrent = false;
        }

        private static AcademicYearDto MapYear(AcademicYear y) => new()
        {
            Id = y.Id,
            Name = y.Name,
            StartDate = y.StartDate,
            EndDate = y.EndDate,
            IsCurrent = y.IsCurrent,
            Periods = y.Periods?.OrderBy(p => p.OrderIndex).Select(MapPeriod).ToList() ?? new()
        };

        private static AcademicPeriodDto MapPeriod(AcademicPeriod p) => new()
        {
            Id = p.Id,
            AcademicYearId = p.AcademicYearId,
            Name = p.Name,
            StartDate = p.StartDate,
            EndDate = p.EndDate,
            OrderIndex = p.OrderIndex,
            IsCurrent = p.IsCurrent
        };
    }
}
