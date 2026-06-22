using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Doctors;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DoctorsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly SubscriptionService _subscriptionService;

        public DoctorsController(ApplicationDbContext db, IClinicContext clinicContext, SubscriptionService subscriptionService)
        {
            _db = db;
            _clinicContext = clinicContext;
            _subscriptionService = subscriptionService;
        }

        // GET: api/doctors
        [HttpGet]
        public async Task<ActionResult<IEnumerable<DoctorResponseDto>>> GetAll()
        {
            if (!_clinicContext.HasPermission("doctors.view") && !_clinicContext.IsCompanyStaff)
                return Forbid();

            var query = _db.Doctors
                .Include(d => d.Department) // ✅ تحميل القسم
                .Where(d => !d.isdeleted);

            if (!_clinicContext.IsCompanyStaff)
            {
                if (_clinicContext.ClinicId == null)
                    return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

                query = query.Where(d => d.ClinicId == _clinicContext.ClinicId);
            }

            var doctors = await query.OrderBy(d => d.FullName).ToListAsync();
            return Ok(doctors.Select(d => ToResponse(d)).ToList());
        }

        // GET: api/doctors/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<DoctorResponseDto>> GetById(Guid id)
        {
            var doctor = await _db.Doctors
                .Include(d => d.Clinic)
                .Include(d => d.Department) // ✅
                .FirstOrDefaultAsync(d => d.Id == id && !d.isdeleted);

            if (doctor == null)
                return NotFound();

            if (!_clinicContext.IsSuperAdmin && doctor.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            return Ok(ToResponse(doctor));
        }

        // POST: api/doctors
        [HttpPost]
        public async Task<ActionResult<DoctorResponseDto>> Create([FromBody] CreateDoctorDto dto)
        {
            if (!_clinicContext.HasPermission("doctors.create"))
                return Forbid();

            if (_clinicContext.IsSuperAdmin)
                return BadRequest("SuperAdmin لا يستطيع إضافة أطباء مباشرة");

            if (_clinicContext.ClinicId == null)
                return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

            var (canAdd, error) = await _subscriptionService.CanAddDoctor(_clinicContext.ClinicId.Value);
            if (!canAdd) return BadRequest(error);

            if (string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest("اسم الطبيب مطلوب");

            // ✅ التحقق أن القسم ينتمي لنفس العيادة
            if (dto.DepartmentId.HasValue)
            {
                var dept = await _db.Departments
                    .FirstOrDefaultAsync(d => d.Id == dto.DepartmentId.Value
                        && d.ClinicId == _clinicContext.ClinicId.Value
                        && d.IsActive);
                if (dept == null)
                    return BadRequest("القسم غير موجود أو لا ينتمي لهذه العيادة");
            }

            var doctor = new Doctor
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                isdeleted = false,
                ClinicId = _clinicContext.ClinicId.Value,
                FullName = dto.FullName,
                Specialty = dto.Specialty,
                Phone = dto.Phone,
                Email = dto.Email,
                Notes = dto.Notes,
                DepartmentId = dto.DepartmentId,       // ✅
                WorkType = dto.WorkType ?? "both",     // ✅
            };

            _db.Doctors.Add(doctor);
            await _db.SaveChangesAsync();

            await _db.Entry(doctor).Reference(d => d.Clinic).LoadAsync();
            if (doctor.DepartmentId.HasValue)
                await _db.Entry(doctor).Reference(d => d.Department).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = doctor.Id }, ToResponse(doctor));
        }

        // PUT: api/doctors/{id}
        [HttpPut("{id}")]
        public async Task<ActionResult<DoctorResponseDto>> Update(Guid id, [FromBody] UpdateDoctorDto dto)
        {
            if (!_clinicContext.HasPermission("doctors.edit"))
                return Forbid();

            var doctor = await _db.Doctors
                .Include(d => d.Clinic)
                .Include(d => d.Department) // ✅
                .FirstOrDefaultAsync(d => d.Id == id && !d.isdeleted);

            if (doctor == null)
                return NotFound();

            if (!_clinicContext.IsSuperAdmin && doctor.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            if (string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest("اسم الطبيب مطلوب");

            // ✅ التحقق من القسم
            if (dto.DepartmentId.HasValue)
            {
                var dept = await _db.Departments
                    .FirstOrDefaultAsync(d => d.Id == dto.DepartmentId.Value
                        && d.ClinicId == doctor.ClinicId
                        && d.IsActive);
                if (dept == null)
                    return BadRequest("القسم غير موجود أو لا ينتمي لهذه العيادة");
            }

            doctor.FullName = dto.FullName;
            doctor.Specialty = dto.Specialty;
            doctor.Phone = dto.Phone;
            doctor.Email = dto.Email;
            doctor.Notes = dto.Notes;
            doctor.IsActive = dto.IsActive;
            doctor.DepartmentId = dto.DepartmentId;    // ✅
            doctor.WorkType = dto.WorkType ?? "both";  // ✅

            await _db.SaveChangesAsync();

            if (doctor.DepartmentId.HasValue)
                await _db.Entry(doctor).Reference(d => d.Department).LoadAsync();

            return Ok(ToResponse(doctor));
        }

        // PATCH: api/doctors/{id}/toggle
        [HttpPatch("{id}/toggle")]
        public async Task<ActionResult> Toggle(Guid id)
        {
            var doctor = await _db.Doctors.FindAsync(id);

            if (doctor == null || doctor.isdeleted)
                return NotFound();

            if (!_clinicContext.IsSuperAdmin && doctor.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            doctor.IsActive = !doctor.IsActive;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = doctor.IsActive ? "تم تفعيل الطبيب" : "تم تعطيل الطبيب",
                isActive = doctor.IsActive
            });
        }

        // DELETE: api/doctors/{id}
        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(Guid id)
        {
            if (!_clinicContext.HasPermission("doctors.delete"))
                return Forbid();

            var doctor = await _db.Doctors.FindAsync(id);

            if (doctor == null || doctor.isdeleted)
                return NotFound();

            if (!_clinicContext.IsSuperAdmin && doctor.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            doctor.isdeleted = true;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ✅ ToResponse محدث
        private static DoctorResponseDto ToResponse(Doctor d) => new DoctorResponseDto
        {
            Id = d.Id,
            FullName = d.FullName,
            Specialty = d.Specialty,
            Phone = d.Phone,
            Email = d.Email,
            Notes = d.Notes,
            IsActive = d.IsActive,
            ClinicId = d.ClinicId,
            ClinicName = d.Clinic?.Name,
            CreatedAt = d.CreatedAt,
            DepartmentId = d.DepartmentId,           // ✅
            DepartmentName = d.Department?.Name,     // ✅
            WorkType = d.WorkType,                   // ✅
        };
    }
}