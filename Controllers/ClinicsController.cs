using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Clinics;
using ClinicSaaS.API.DTOs.Patients;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    /*
      [Authorize]                    → أي مستخدم مسجّل
      [Authorize(Roles = "SuperAdmin")] → SuperAdmin فقط ✅
    */
     
        [ApiController]
        [Route("api/[controller]")]
        [Authorize]// يجب تسجيل الدخول أولاً // هذا الكونترولر يتطلب توثيق المستخدم للوصول إليه
        public class ClinicsController : ControllerBase
        {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext; // ✅ أضف

        public ClinicsController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext; // ✅ أضف
        }
        // GET: api/clinics
        // SuperAdmin فقط — جلب كل العيادات
        [HttpGet]
            [Authorize(Roles = "SuperAdmin")]
            public async Task<ActionResult<IEnumerable<ClinicResponseDto>>> GetAll()
            {
                var clinics = await _db.Clinics
                    .OrderByDescending(c => c.CreatedAt)
                    .ToListAsync();

                var result = clinics.Select(c => ToResponse(c)).ToList();
                return Ok(result);
            }

        // GET: api/clinics/{id}
        [HttpGet("{id}")]
        [Authorize(Roles = "SuperAdmin,ClinicStaff,ClinicAdmin")] // ✅ أضف ClinicAdmin
        public async Task<ActionResult<ClinicResponseDto>> GetById(Guid id)
        {
            // ClinicAdmin يرى عيادته فقط
            if (_clinicContext.Role == "ClinicAdmin" && id != _clinicContext.ClinicId)
                return Forbid();

            var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == id);
            if (clinic == null)
                return NotFound();

            return Ok(ToResponse(clinic));
        }

        // GET: api/clinics/by-subdomain/{subdomain}
        [HttpGet("by-subdomain/{subdomain}")]
        [AllowAnonymous]
        public async Task<ActionResult> GetBySubdomain(string subdomain)
        {
            if (subdomain.ToLower() == "admin")
                return Ok(new { name = "Cura Admin", logo = (string?)null, isAdmin = true });

            var clinic = await _db.Clinics
                .FirstOrDefaultAsync(c => c.Subdomain == subdomain && c.IsActive);

            if (clinic == null)
                return NotFound(new { message = "العيادة غير موجودة أو غير نشطة" });

            return Ok(new
            {
                id = clinic.Id,
                name = clinic.Name,
                logo = clinic.Logo,
                isAdmin = false,
            });
        }
        //post: api/clinics
        // SuperAdmin فقط — إنشاء عيادة جديدة

        [HttpPost]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<ClinicResponseDto>> Create([FromBody] CreateClinicDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Clinic name is required.");

            var subdomainExists = await _db.Clinics
                .AnyAsync(c => c.Subdomain == dto.subDomain);
            if (subdomainExists)
                return BadRequest("Subdomain already exists.");

            var clinic = new Clinic
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                Name = dto.Name,
                Subdomain = dto.subDomain,
                Logo = dto.Logo,
                Address = dto.Address,
                Phone = dto.Phone,
                Website = dto.website,
                Email = dto.Email,
                OwnerName = dto.OwnerName,
                OwnerEmail = dto.OwnerEmail,
                OwnerPhone = dto.OwnerPhone,
                TaxNumber = dto.TaxNumber,
                SourceNumber = dto.CommercialRegister,
                InvoiceId = dto.InvoiceId,
                InvoiceKey = dto.InvoiceKey,
                Description = dto.Description
            };
            _db.Clinics.Add(clinic);
            await _db.SaveChangesAsync();

            // ✅ إنشاء الأقسام الافتراضية تلقائياً
            var defaultDepartments = new[]
  {
    new { Name = "الاستقبال", NameEn = "Reception"      },
    new { Name = "الأسنان",   NameEn = "Dentistry"      },
    new { Name = "الأطفال",   NameEn = "Pediatrics"     },
    new { Name = "العيون",    NameEn = "Ophthalmology"  },
    new { Name = "المحاسبة",  NameEn = "Accounting"     },
    new { Name = "الإدارة",   NameEn = "Administration" },
    new { Name = "المختبر",   NameEn = "Laboratory"     },
    new { Name = "الأشعة",    NameEn = "Radiology"      },
    new { Name = "تمريض",     NameEn = "Nursing"        },
    new { Name = "صيدله",     NameEn = "Pharmacy"       },
};

            foreach (var dept in defaultDepartments)
            {
                _db.Departments.Add(new Department
                {
                    Id = Guid.NewGuid(),
                    Name = dept.Name,
                    NameEn = dept.NameEn,  // ✅
                    ClinicId = clinic.Id,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = clinic.Id }, ToResponse(clinic));
        }


        // PUT: api/clinics/{id}

        [HttpPut("{id}")]
        [Authorize(Roles = "SuperAdmin,ClinicAdmin")]
        public async Task<ActionResult<ClinicResponseDto>> Update(Guid id, [FromBody] UpdateClinicDto dto)
        {
            if (_clinicContext.Role == "ClinicAdmin" && id != _clinicContext.ClinicId)
                return Forbid();

            var clinic = await _db.Clinics.FindAsync(id);
            if (clinic == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Clinic name is required.");

            clinic.Name = dto.Name;
            clinic.Phone = dto.Phone;
            clinic.Address = dto.Address;
            clinic.Email = dto.Email;
            clinic.Website = dto.Website;
            clinic.Description = dto.Description;
            clinic.OwnerName = dto.OwnerName;
            clinic.OwnerPhone = dto.OwnerPhone;
            clinic.OwnerEmail = dto.OwnerEmail;
            clinic.TaxNumber = dto.TaxNumber;

            await _db.SaveChangesAsync();
            return Ok(ToResponse(clinic));
        }

        // PATCH: api/clinics/{id}/toggle
        [HttpPatch("{id}/toggle")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult> Toggle(Guid id)
        {
            var clinic = await _db.Clinics.FindAsync(id);
            if (clinic == null) return NotFound();

            clinic.IsActive = !clinic.IsActive;
            await _db.SaveChangesAsync();
            return Ok(new { clinic.IsActive });
        }
        // دالة مساعدة
        private static ClinicResponseDto ToResponse(Clinic c) => new ClinicResponseDto
            {
                Id = c.Id,
                Name = c.Name,
                Subdomain = c.Subdomain,
                Logo = c.Logo,
                Address = c.Address,
                Phone = c.Phone,
                Website = c.Website,
                Email = c.Email,
                OwnerName = c.OwnerName,
                OwnerEmail = c.OwnerEmail,
                OwnerPhone = c.OwnerPhone,
                TaxNumber = c.TaxNumber,
                CommercialRegister = c.SourceNumber,
                InvoiceId = c.InvoiceId,
                InvoiceKey = c.InvoiceKey,
                Description = c.Description,
                IsActive = c.IsActive,
                CreatedAt = c.CreatedAt
            };
        }
     
}
