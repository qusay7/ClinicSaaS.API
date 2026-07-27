using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    /// <summary>
    /// إدارة الإعدادات المالية للطبيب: السعر الخاص وحصته من كل زيارة —
    /// إما "إعداد عام" (TemplateId = null) يطبّق على كل القوالب، أو "استثناء" لقالب معيّن.
    /// </summary>
    [ApiController]
    [Route("api/doctors/{doctorId}/financial-settings")]
    [Authorize]
    public class DoctorTemplateSettingsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public DoctorTemplateSettingsController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        private static string Msg(string lang, string ar, string en) => lang == "ar" ? ar : en;

        private async Task<Doctor?> GetAuthorizedDoctor(Guid doctorId)
        {
            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == doctorId && !d.IsDeleted);
            if (doctor == null) return null;
            if (!_clinicContext.IsSuperAdmin && doctor.ClinicId != _clinicContext.ClinicId) return null;
            return doctor;
        }

        // ═══════════════════════════════════════
        // GET: api/doctors/{doctorId}/financial-settings
        // يرجّع الإعداد العام (لو موجود) + كل الاستثناءات، بترتيب العام أولاً
        // ═══════════════════════════════════════
        [HttpGet]
        public async Task<ActionResult> GetAll(Guid doctorId)
        {
            var doctor = await GetAuthorizedDoctor(doctorId);
            if (doctor == null) return NotFound();

            var settings = await _db.DoctorTemplateSettings
                .Include(s => s.Template)
                .Where(s => s.DoctorId == doctorId)
                .ToListAsync();

            var ordered = settings
                .OrderBy(s => s.TemplateId == null ? 0 : 1)
                .ThenBy(s => s.Template?.Name)
                .Select(ToResponse);

            return Ok(ordered);
        }

        // ═══════════════════════════════════════
        // PUT: api/doctors/{doctorId}/financial-settings/general
        // ✅ Upsert — ينشئ "الإعداد العام" لو أول مرة، أو يحدّثه لو موجود
        // (القيد الفريد بقاعدة البيانات أصلاً يمنع أكثر من إعداد عام واحد لكل طبيب)
        // ═══════════════════════════════════════
        [HttpPut("general")]
        public async Task<ActionResult> UpsertGeneral(Guid doctorId, [FromBody] UpsertDoctorTemplateSettingDto dto, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("doctors.edit")) return Forbid();

            var doctor = await GetAuthorizedDoctor(doctorId);
            if (doctor == null) return NotFound();

            var validationError = Validate(dto, lang);
            if (validationError != null) return BadRequest(validationError);

            var setting = await _db.DoctorTemplateSettings
                .Include(s => s.Template)
                .FirstOrDefaultAsync(s => s.DoctorId == doctorId && s.TemplateId == null);

            if (setting == null)
            {
                setting = new DoctorTemplateSetting
                {
                    Id = Guid.NewGuid(),
                    ClinicId = doctor.ClinicId,
                    DoctorId = doctorId,
                    TemplateId = null,
                    CreatedAt = DateTime.UtcNow,
                };
                _db.DoctorTemplateSettings.Add(setting);
            }

            ApplyDto(setting, dto);
            await _db.SaveChangesAsync();

            return Ok(ToResponse(setting));
        }

        // ═══════════════════════════════════════
        // PUT: api/doctors/{doctorId}/financial-settings/exceptions/{templateId}
        // ✅ Upsert — استثناء خاص لقالب معيّن (يتجاوز الإعداد العام + سعر القالب نفسه)
        // ═══════════════════════════════════════
        [HttpPut("exceptions/{templateId}")]
        public async Task<ActionResult> UpsertException(Guid doctorId, Guid templateId, [FromBody] UpsertDoctorTemplateSettingDto dto, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("doctors.edit")) return Forbid();

            var doctor = await GetAuthorizedDoctor(doctorId);
            if (doctor == null) return NotFound();

            var template = await _db.TreatmentPlanTemplates
                .FirstOrDefaultAsync(t => t.Id == templateId && t.ClinicId == doctor.ClinicId);
            if (template == null)
                return NotFound(Msg(lang, "القالب غير موجود", "Template not found"));

            var validationError = Validate(dto, lang);
            if (validationError != null) return BadRequest(validationError);

            var setting = await _db.DoctorTemplateSettings
                .Include(s => s.Template)
                .FirstOrDefaultAsync(s => s.DoctorId == doctorId && s.TemplateId == templateId);

            if (setting == null)
            {
                setting = new DoctorTemplateSetting
                {
                    Id = Guid.NewGuid(),
                    ClinicId = doctor.ClinicId,
                    DoctorId = doctorId,
                    TemplateId = templateId,
                    CreatedAt = DateTime.UtcNow,
                };
                _db.DoctorTemplateSettings.Add(setting);
            }

            ApplyDto(setting, dto);
            await _db.SaveChangesAsync();

            await _db.Entry(setting).Reference(s => s.Template).LoadAsync();
            return Ok(ToResponse(setting));
        }

        // ═══════════════════════════════════════
        // DELETE: api/doctors/{doctorId}/financial-settings/{id}
        // حذف إعداد (عام أو استثناء) — يرجع الطبيب يعتمد على القيمة الافتراضية بعدها
        // ═══════════════════════════════════════
        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(Guid doctorId, Guid id, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("doctors.edit")) return Forbid();

            var doctor = await GetAuthorizedDoctor(doctorId);
            if (doctor == null) return NotFound();

            var setting = await _db.DoctorTemplateSettings
                .FirstOrDefaultAsync(s => s.Id == id && s.DoctorId == doctorId);
            if (setting == null) return NotFound();

            setting.IsDeleted = true;
            setting.IsActive = false;
            await _db.SaveChangesAsync();

            return Ok(new { message = Msg(lang, "تم الحذف", "Deleted") });
        }

        // ═══════════════════════════════════════
        // Helpers
        // ═══════════════════════════════════════

        private static object? Validate(UpsertDoctorTemplateSettingDto dto, string lang)
        {
            if (dto.CommissionType != "percentage" && dto.CommissionType != "fixed")
                return new { message = Msg(lang, "نوع الحصة يجب أن يكون نسبة أو مبلغ ثابت", "Commission type must be 'percentage' or 'fixed'") };

            if (dto.CommissionType == "percentage")
            {
                if (dto.FirstVisitCommissionRate is > 100 or < 0)
                    return new { message = Msg(lang, "نسبة الكشف الأول يجب أن تكون بين 0 و100", "First-visit rate must be between 0 and 100") };
                if (dto.FollowUpCommissionRate is > 100 or < 0)
                    return new { message = Msg(lang, "نسبة المراجعة يجب أن تكون بين 0 و100", "Follow-up rate must be between 0 and 100") };
            }
            else
            {
                if (dto.FirstVisitCommissionRate is < 0 || dto.FollowUpCommissionRate is < 0)
                    return new { message = Msg(lang, "المبلغ الثابت لا يمكن أن يكون سالباً", "Fixed amount cannot be negative") };
            }

            if (dto.FirstVisitPrice is < 0 || dto.FollowUpPrice is < 0)
                return new { message = Msg(lang, "السعر لا يمكن أن يكون سالباً", "Price cannot be negative") };

            return null;
        }

        private static void ApplyDto(DoctorTemplateSetting setting, UpsertDoctorTemplateSettingDto dto)
        {
            setting.FirstVisitPrice = dto.FirstVisitPrice;
            setting.FollowUpPrice = dto.FollowUpPrice;
            setting.CommissionType = dto.CommissionType;
            setting.FirstVisitCommissionRate = dto.FirstVisitCommissionRate;
            setting.FollowUpCommissionRate = dto.FollowUpCommissionRate;
            setting.IsActive = true;
        }

        private static object ToResponse(DoctorTemplateSetting s) => new
        {
            s.Id,
            s.TemplateId,
            templateName = s.Template?.Name,
            isGeneral = s.TemplateId == null,
            s.FirstVisitPrice,
            s.FollowUpPrice,
            s.CommissionType,
            s.FirstVisitCommissionRate,
            s.FollowUpCommissionRate,
            s.IsActive,
        };
    }

    public class UpsertDoctorTemplateSettingDto
    {
        public decimal? FirstVisitPrice { get; set; }
        public decimal? FollowUpPrice { get; set; }
        public string CommissionType { get; set; } = "percentage";   // percentage / fixed
        public decimal? FirstVisitCommissionRate { get; set; }
        public decimal? FollowUpCommissionRate { get; set; }
    }
}