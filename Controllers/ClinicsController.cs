using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Clinics;
using ClinicSaaS.API.DTOs.Patients;
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

            public ClinicsController(ApplicationDbContext db)
            {
                _db = db;
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
            // SuperAdmin فقط — جلب تفاصيل عيادة معينة
            [HttpGet("{id}")]
            [Authorize(Roles = "SuperAdmin")]
            public async Task<ActionResult<ClinicResponseDto>> GetById(Guid id)
            {
                var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == id);
                if (clinic == null)
                    return NotFound();
                return Ok(ToResponse(clinic));
            }

            //post: api/clinics
            // SuperAdmin فقط — إنشاء عيادة جديدة
            [HttpPost]
            [Authorize(Roles = "SuperAdmin")]
            public async Task<ActionResult<ClinicResponseDto>> Create([FromBody] CreateClinicDto dto)

            {
                //تحقق أن الاسم غير فارغ

                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest("Clinic name is required.");

                // تحقق أن Subdomain غير مكرر
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
                return CreatedAtAction(nameof(GetById), new { id = clinic.Id }, ToResponse(clinic));
            }


        // PUT: api/clinics/{id}
        // SuperAdmin فقط — إنشاء عيادة جديدة
        [HttpPut("{id}")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<ClinicResponseDto>> Update(Guid id, [FromBody] CreateClinicDto dto)
        {
            var clinic = await _db.Clinics.FindAsync(id);
            if (clinic == null)
                return NotFound();


            //تحقق أن الاسم غير فارغ

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Clinic name is required.");

            // تحقق أن Subdomain غير مكرر
            var subdomainExists = await _db.Clinics
                                  .AnyAsync(c => c.Subdomain == dto.subDomain && c.Id != id);

            if (subdomainExists)
                return BadRequest("Subdomain already exists.");

            clinic.Name = dto.Name;
            clinic.Subdomain = dto.subDomain;
            clinic.Logo = dto.Logo;
            clinic.Address = dto.Address;
            clinic.Phone = dto.Phone;
            clinic.Website = dto.website;
            clinic.Email = dto.Email;
            clinic.OwnerName = dto.OwnerName;
            clinic.OwnerEmail = dto.OwnerEmail;
            clinic.OwnerPhone = dto.OwnerPhone;
            clinic.TaxNumber = dto.TaxNumber;
            clinic.SourceNumber = dto.CommercialRegister;
            clinic.InvoiceId = dto.InvoiceId;
            clinic.InvoiceKey = dto.InvoiceKey;
            clinic.Description = dto.Description;

            await _db.SaveChangesAsync();
            return Ok(ToResponse(clinic));
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
