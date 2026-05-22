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
    [Route("api/[controller]")]
    public class SubjectsController : ControllerBase
    {
        private readonly AppDbContext _context;
        public SubjectsController(AppDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var items = await _context.Subjects
                .Where(s => s.SchoolId == schoolId.Value && !s.IsDeleted)
                .OrderBy(s => s.OrderIndex).ThenBy(s => s.Name)
                .Select(s => new SubjectDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    NameAr = s.NameAr,
                    Kind = s.Kind,
                    DefaultCoefficient = s.DefaultCoefficient,
                    DefaultMaxValue = s.DefaultMaxValue,
                    OrderIndex = s.OrderIndex,
                    IsActive = s.IsActive
                })
                .ToListAsync();
            return Ok(items);
        }

        [HttpPost]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Create([FromBody] SubjectCreateDto dto)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var entity = new Subject
            {
                SchoolId = schoolId.Value,
                Name = dto.Name.Trim(),
                NameAr = string.IsNullOrWhiteSpace(dto.NameAr) ? null : dto.NameAr.Trim(),
                Kind = dto.Kind,
                DefaultCoefficient = dto.DefaultCoefficient,
                DefaultMaxValue = dto.DefaultMaxValue,
                OrderIndex = dto.OrderIndex,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };
            _context.Subjects.Add(entity);
            await _context.SaveChangesAsync();
            return Ok(MapToDto(entity));
        }

        [HttpPut("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Update(int id, [FromBody] SubjectUpdateDto dto)
        {
            if (id != dto.Id) return BadRequest(ApiResponse<bool>.Fail("ID mismatch."));
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var entity = await _context.Subjects
                .FirstOrDefaultAsync(s => s.Id == id && s.SchoolId == schoolId.Value && !s.IsDeleted);
            if (entity == null) return NotFound();

            entity.Name = dto.Name.Trim();
            entity.NameAr = string.IsNullOrWhiteSpace(dto.NameAr) ? null : dto.NameAr.Trim();
            entity.Kind = dto.Kind;
            entity.DefaultCoefficient = dto.DefaultCoefficient;
            entity.DefaultMaxValue = dto.DefaultMaxValue;
            entity.OrderIndex = dto.OrderIndex;
            entity.IsActive = dto.IsActive;
            await _context.SaveChangesAsync();
            return Ok(MapToDto(entity));
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> Delete(int id)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var entity = await _context.Subjects
                .FirstOrDefaultAsync(s => s.Id == id && s.SchoolId == schoolId.Value && !s.IsDeleted);
            if (entity == null) return NotFound();

            entity.IsDeleted = true;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private static SubjectDto MapToDto(Subject s) => new()
        {
            Id = s.Id,
            Name = s.Name,
            NameAr = s.NameAr,
            Kind = s.Kind,
            DefaultCoefficient = s.DefaultCoefficient,
            DefaultMaxValue = s.DefaultMaxValue,
            OrderIndex = s.OrderIndex,
            IsActive = s.IsActive
        };
    }
}
