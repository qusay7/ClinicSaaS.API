using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.DiagnosisTemplates;
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
    public class DiagnosisTemplatesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public DiagnosisTemplatesController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        private static string Msg(string lang, string ar, string en) => lang == "ar" ? ar : en;

        // GET: api/diagnosistemplates — القائمة الكاملة، لصفحة الإعدادات
        [HttpGet]
        public async Task<ActionResult> GetAll()
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var templates = await _db.DiagnosisTemplates
                .Include(t => t.Medications)
                .Where(t => t.ClinicId == _clinicContext.ClinicId && t.IsActive)
                .OrderBy(t => t.Name)
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    medications = t.Medications
                        .OrderBy(m => m.SortOrder)
                        .Select(m => new { m.Id, m.DrugName, m.Instructions }),
                })
                .ToListAsync();

            return Ok(templates);
        }

        // GET: api/diagnosistemplates/search?q=... — للاقتراح اللحظي وقت كتابة التشخيص
        // (اسم فقط، بدون الأدوية — تُجلب لاحقاً عند اختيار اقتراح محدد)
        [HttpGet("search")]
        public async Task<ActionResult> Search([FromQuery] string q)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            if (string.IsNullOrWhiteSpace(q)) return Ok(new object[0]);

            var results = await _db.DiagnosisTemplates
                .Where(t => t.ClinicId == _clinicContext.ClinicId && t.IsActive && t.Name.Contains(q))
                .OrderBy(t => t.Name)
                .Take(10)
                .Select(t => new { t.Id, t.Name })
                .ToListAsync();

            return Ok(results);
        }

        // GET: api/diagnosistemplates/{id} — التفصيل الكامل مع الأدوية، عند اختيار اقتراح
        [HttpGet("{id}")]
        public async Task<ActionResult> GetById(Guid id)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var template = await _db.DiagnosisTemplates
                .Include(t => t.Medications)
                .FirstOrDefaultAsync(t => t.Id == id && t.ClinicId == _clinicContext.ClinicId);

            if (template == null) return NotFound();

            return Ok(new
            {
                template.Id,
                template.Name,
                medications = template.Medications
                    .OrderBy(m => m.SortOrder)
                    .Select(m => new { m.Id, m.DrugName, m.Instructions }),
            });
        }

        // POST: api/diagnosistemplates
        [HttpPost]
        public async Task<ActionResult> Create([FromBody] CreateDiagnosisTemplateDto dto, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("diagnosistemplates.manage")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(Msg(lang, "اسم التشخيص مطلوب", "Diagnosis name is required"));

            var template = new DiagnosisTemplate
            {
                Id = Guid.NewGuid(),
                ClinicId = _clinicContext.ClinicId.Value,
                Name = dto.Name,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Medications = dto.Medications
                    .Select((m, i) => new DiagnosisMedication
                    {
                        Id = Guid.NewGuid(),
                        DrugName = m.DrugName,
                        Instructions = m.Instructions,
                        SortOrder = i,
                    })
                    .ToList(),
            };

            _db.DiagnosisTemplates.Add(template);
            await _db.SaveChangesAsync();

            return Ok(new { template.Id, message = Msg(lang, "تم إنشاء التشخيص بنجاح", "Diagnosis template created successfully") });
        }

        // PUT: api/diagnosistemplates/{id} — تعديل، بما فيه الأدوية (replace-all)
        [HttpPut("{id}")]
        public async Task<ActionResult> Update(Guid id, [FromBody] CreateDiagnosisTemplateDto dto, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("diagnosistemplates.manage")) return Forbid();

            var template = await _db.DiagnosisTemplates
                .Include(t => t.Medications)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (template == null) return NotFound();
            if (template.ClinicId != _clinicContext.ClinicId) return Forbid();

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(Msg(lang, "اسم التشخيص مطلوب", "Diagnosis name is required"));

            template.Name = dto.Name;

            _db.DiagnosisMedications.RemoveRange(template.Medications);
            template.Medications = dto.Medications
                .Select((m, i) => new DiagnosisMedication
                {
                    Id = Guid.NewGuid(),
                    DiagnosisTemplateId = template.Id,
                    DrugName = m.DrugName,
                    Instructions = m.Instructions,
                    SortOrder = i,
                })
                .ToList();

            await _db.SaveChangesAsync();

            return Ok(new { template.Id, message = Msg(lang, "تم تحديث التشخيص بنجاح", "Diagnosis template updated successfully") });
        }

        // DELETE: api/diagnosistemplates/{id}
        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(Guid id, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("diagnosistemplates.manage")) return Forbid();

            var template = await _db.DiagnosisTemplates.FindAsync(id);
            if (template == null) return NotFound();
            if (template.ClinicId != _clinicContext.ClinicId) return Forbid();

            template.IsDeleted = true;
            template.IsActive = false;
            await _db.SaveChangesAsync();

            return Ok(new { message = Msg(lang, "تم حذف التشخيص", "Diagnosis template deleted") });
        }
    }
}
