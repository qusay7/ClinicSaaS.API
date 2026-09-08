using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Department;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClinicSaaS.API.Filters;



namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [RequireActiveSubscription]
    public class DepartmentsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly IDepartmentSeedingService _departmentSeedingService;

        public DepartmentsController(ApplicationDbContext db, IClinicContext clinicContext, IDepartmentSeedingService departmentSeedingService)
        {
            _db = db;
            _clinicContext = clinicContext;
            _departmentSeedingService = departmentSeedingService;
        }

        [HttpPost("seed-defaults/{clinicId}")]
        [Authorize(Roles = "SuperAdmin,ClinicAdmin")]
        [RequireActiveSubscription]
        public async Task<ActionResult> SeedDefaultDepartments(Guid clinicId)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null) return NotFound("العيادة غير موجودة");

            var added = await _departmentSeedingService.SeedDefaultDepartments(clinicId);

            return Ok(new { message = $"تم إنشاء {added} قسم" });
        }


        [HttpGet]
        public async Task<ActionResult> GetAll()
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var depts = await _db.Departments
                .Where(d => d.ClinicId == _clinicContext.ClinicId)
                .OrderBy(d => d.Name)
                .ToListAsync();

            // ✅ خطط بدون ميزة "أقسام متعددة" ممكن تكون عندها أقسام زائدة من قبل ما
            // نضيف هذا القيد (بيانات قديمة نتركها كما هي بدل حذفها) — نختصر أي قائمة/
            // قائمة منسدلة تعرض الأقسام لـ 3 عناصر بس (إدارة/استقبال/قسم طبي واحد
            // تمثيلي)، بنفس الـ Id الحقيقية عشان التعيينات الحالية تبقى صحيحة
            if (!_clinicContext.HasMultipleDepartments && depts.Count > 3)
            {
                var admin = depts.FirstOrDefault(d => d.Name.Contains("إدارة") || (d.NameEn ?? "").Contains("Admin", StringComparison.OrdinalIgnoreCase));
                var reception = depts.FirstOrDefault(d => d.Name.Contains("استقبال") || (d.NameEn ?? "").Contains("Reception", StringComparison.OrdinalIgnoreCase));
                var medical = depts.FirstOrDefault(d => d.Id != admin?.Id && d.Id != reception?.Id);

                depts = new[] { admin, reception, medical }
                    .Where(d => d != null)
                    .Cast<Department>()
                    .DistinctBy(d => d.Id)
                    .ToList();
            }

            var result = depts.Select(d => new {
                d.Id,
                d.Name,
                d.NameEn,
                d.Type,

                d.SettingsJson,
                d.IsActive,
                d.CreatedAt,
                DoctorsCount = _db.Doctors.Count(doc =>
                    doc.DepartmentId == d.Id && !doc.IsDeleted)
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

            // ✅ الخطط اللي بدون ميزة "أقسام متعددة" مقيّدة بقسم واحد فقط
            if (!_clinicContext.HasMultipleDepartments)
            {
                var departmentCount = await _db.Departments
                    .CountAsync(d => d.ClinicId == _clinicContext.ClinicId);
                if (departmentCount >= 1)
                    return StatusCode(StatusCodes.Status402PaymentRequired, new
                    {
                        code = "FEATURE_NOT_IN_PLAN",
                        message = "خطتك الحالية تسمح بقسم واحد فقط — يرجى ترقية الخطة لإضافة أقسام متعددة"
                    });
            }

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
                .AnyAsync(d => d.DepartmentId == id && !d.IsDeleted);
            if (hasDoctors)
                return BadRequest("لا يمكن حذف قسم يحتوي على أطباء");

            _db.Departments.Remove(dept);
            await _db.SaveChangesAsync();
            return NoContent();
        }


    }
}