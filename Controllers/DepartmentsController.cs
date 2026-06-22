using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Department;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;



namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DepartmentsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public DepartmentsController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        [HttpGet]
        public async Task<ActionResult> GetAll()
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var depts = await _db.Departments
                .Where(d => d.ClinicId == _clinicContext.ClinicId)
                .OrderBy(d => d.Name)
                .ToListAsync();

            var result = depts.Select(d => new {
                d.Id,
                d.Name,
                d.Type,
                d.SettingsJson,
                d.IsActive,
                d.CreatedAt,
                DoctorsCount = _db.Doctors.Count(doc =>
                    doc.DepartmentId == d.Id && !doc.isdeleted)
            });

            return Ok(result);
        }

        [HttpPost]
        public async Task<ActionResult> Create([FromBody] CreateDepartmentDto dto)
        {
            if (!_clinicContext.HasPermission("departments.manage")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var exists = await _db.Departments
                .AnyAsync(d => d.ClinicId == _clinicContext.ClinicId && d.Name == dto.Name);
            if (exists) return BadRequest(
                dto.Name + (": القسم موجود مسبقاً"));

            var dept = new Department
            {
                Id = Guid.NewGuid(),
                ClinicId = _clinicContext.ClinicId.Value,
                Name = dto.Name,
                Type = (DepartmentType)dto.Type,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };

            _db.Departments.Add(dept);
            await _db.SaveChangesAsync();

            return Ok(dept); // ✅ أضف هذا


        }

        [HttpPut("{id}")]
        public async Task<ActionResult> Update(Guid id, [FromBody] CreateDepartmentDto dto)
        {
            if (!_clinicContext.HasPermission("departments.manage")) return Forbid();

            var dept = await _db.Departments.FindAsync(id);
            if (dept == null) return NotFound();
            if (dept.ClinicId != _clinicContext.ClinicId) return Forbid();

            dept.Name = dto.Name;
            dept.Type = (DepartmentType)dto.Type;
            await _db.SaveChangesAsync();
            return Ok(dept);
        }

        [HttpPatch("{id}/toggle")]
        public async Task<ActionResult> Toggle(Guid id)
        {
            if (!_clinicContext.HasPermission("departments.manage")) return Forbid();

            var dept = await _db.Departments.FindAsync(id);
            if (dept == null) return NotFound();
            if (dept.ClinicId != _clinicContext.ClinicId) return Forbid();

            dept.IsActive = !dept.IsActive;
            await _db.SaveChangesAsync();
            return Ok(new { dept.IsActive });
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(Guid id)
        {
            if (!_clinicContext.HasPermission("departments.manage")) return Forbid();

            var dept = await _db.Departments.FindAsync(id);
            if (dept == null) return NotFound();
            if (dept.ClinicId != _clinicContext.ClinicId) return Forbid();

            var hasDoctors = await _db.Doctors
                .AnyAsync(d => d.DepartmentId == id && !d.isdeleted);
            if (hasDoctors)
                return BadRequest("لا يمكن حذف قسم يحتوي على أطباء");

            _db.Departments.Remove(dept);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST: api/departments/seed-defaults/{clinicId}
        [HttpPost("seed-defaults/{clinicId}")]
        [Authorize(Roles = "SuperAdmin,ClinicAdmin")]
        public async Task<ActionResult> SeedDefaultDepartments(Guid clinicId)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null) return NotFound("العيادة غير موجودة");

            var defaultDepartments = new[]
{
    new { Name = "الاستقبال", NameEn = "Reception" },
    new { Name = "الأسنان",   NameEn = "Dentistry" },
    new { Name = "الأطفال",   NameEn = "Pediatrics" },
    new { Name = "العيون",    NameEn = "Ophthalmology" },
    new { Name = "المحاسبة",  NameEn = "Accounting" },
    new { Name = "الإدارة",   NameEn = "Administration" },
    new { Name = "المختبر",   NameEn = "Laboratory" },
    new { Name = "الأشعة",    NameEn = "Radiology" },
    new { Name = "تمريض",     NameEn = "Nursing" },
    new { Name = "صيدله",     NameEn = "Pharmacy" },
};

            int added = 0;

            foreach (var dept in defaultDepartments)
            {
                var exists = await _db.Departments
                    .AnyAsync(d => d.Name == dept.Name && d.ClinicId == clinicId);
                if (exists) continue;

                _db.Departments.Add(new Department
                {
                    Id = Guid.NewGuid(),
                    Name = dept.Name,
                    NameEn = dept.NameEn, // ✅
                    ClinicId = clinicId,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                });
                added++;
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = $"تم إنشاء {added} قسم" });
        }
    }
}
