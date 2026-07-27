using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.TreatmentPlans;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TreatmentPlansController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public TreatmentPlansController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        private static string Msg(string lang, string ar, string en) => lang == "ar" ? ar : en;

        // ═══════════════════════════════════════
        // القوالب (Templates)
        // ═══════════════════════════════════════

        // GET: api/treatmentplans/templates
        [HttpGet("templates")]
        public async Task<ActionResult> GetTemplates()
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var templates = await _db.TreatmentPlanTemplates
                .Include(t => t.Department)
                .Where(t => t.ClinicId == _clinicContext.ClinicId && t.IsActive)
                .OrderBy(t => t.Name)
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.NameEn,
                    t.DepartmentId,
                    departmentName = t.Department != null ? t.Department.Name : null,
                    t.DefaultSessionsCount,
                    t.DefaultPricePerSession,
                    t.DefaultTotalPrice,
                    t.FirstVisitPrice,   // ✅ كانت مفقودة
                    t.FollowUpPrice,      // ✅ كانت مفقودة
                })
                .ToListAsync();

            return Ok(templates);
        }

        // POST: api/treatmentplans/templates
        [HttpPost("templates")]
        public async Task<ActionResult> CreateTemplate([FromBody] CreateTreatmentTemplateDto dto, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("treatmenttemplates.manage")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(Msg(lang, "اسم القالب مطلوب", "Template name is required"));

            if (dto.DefaultSessionsCount < 1)
                return BadRequest(Msg(lang, "عدد الجلسات يجب أن يكون واحد على الأقل", "Sessions count must be at least 1"));

            var template = new TreatmentPlanTemplate
            {
                Id = Guid.NewGuid(),
                ClinicId = _clinicContext.ClinicId.Value,
                Name = dto.Name,
                NameEn = dto.NameEn,
                DepartmentId = dto.DepartmentId,
                DefaultSessionsCount = dto.DefaultSessionsCount,
                DefaultPricePerSession = dto.DefaultPricePerSession,
                DefaultTotalPrice = dto.DefaultTotalPrice,
                FirstVisitPrice = dto.FirstVisitPrice,   // ✅ كانت مفقودة
                FollowUpPrice = dto.FollowUpPrice,        // ✅ كانت مفقودة
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };

            _db.TreatmentPlanTemplates.Add(template);
            await _db.SaveChangesAsync();

            return Ok(new { template.Id, message = Msg(lang, "تم إنشاء القالب بنجاح", "Template created successfully") });
        }

        // ✅ PUT: api/treatmentplans/templates/{id}
        // كانت مفقودة بالكامل — بدونها ما فيه طريقة تعدّل سعر أي قالب (حتى الافتراضية اللي أسعارها فاضية)
        [HttpPut("templates/{id}")]
        public async Task<ActionResult> UpdateTemplate(Guid id, [FromBody] CreateTreatmentTemplateDto dto, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("treatmenttemplates.manage")) return Forbid();

            var template = await _db.TreatmentPlanTemplates.FindAsync(id);
            if (template == null) return NotFound();
            if (template.ClinicId != _clinicContext.ClinicId) return Forbid();

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(Msg(lang, "اسم القالب مطلوب", "Template name is required"));

            if (dto.DefaultSessionsCount < 1)
                return BadRequest(Msg(lang, "عدد الجلسات يجب أن يكون واحد على الأقل", "Sessions count must be at least 1"));

            template.Name = dto.Name;
            template.NameEn = dto.NameEn;
            template.DepartmentId = dto.DepartmentId;
            template.DefaultSessionsCount = dto.DefaultSessionsCount;
            template.DefaultPricePerSession = dto.DefaultPricePerSession;
            template.DefaultTotalPrice = dto.DefaultTotalPrice;
            template.FirstVisitPrice = dto.FirstVisitPrice;
            template.FollowUpPrice = dto.FollowUpPrice;

            await _db.SaveChangesAsync();

            return Ok(new { template.Id, message = Msg(lang, "تم تحديث القالب بنجاح", "Template updated successfully") });
        }

        // DELETE: api/treatmentplans/templates/{id}
        [HttpDelete("templates/{id}")]
        public async Task<ActionResult> DeleteTemplate(Guid id, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("treatmenttemplates.manage")) return Forbid();

            var template = await _db.TreatmentPlanTemplates.FindAsync(id);
            if (template == null) return NotFound();
            if (template.ClinicId != _clinicContext.ClinicId) return Forbid();

            template.IsDeleted = true;
            template.IsActive = false;
            await _db.SaveChangesAsync();

            return Ok(new { message = Msg(lang, "تم حذف القالب", "Template deleted") });
        }

        // ✅ POST: api/treatmentplans/templates/seed-defaults/{clinicId}
        // ينشئ القوالب الافتراضية الأربعة (كشف/مراجعة/استشارة/متابعة) لعيادة معيّنة —
        // بنفس نمط departments/seed-defaults و roles/seed-defaults. آمن التكرار.
        [HttpPost("templates/seed-defaults/{clinicId}")]
        [Authorize(Roles = "SuperAdmin,ClinicAdmin")]
        public async Task<ActionResult> SeedDefaultTemplates(
            Guid clinicId,
            [FromServices] ITreatmentTemplateSeedingService seedingService,
            [FromQuery] string lang = "ar")
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null) return NotFound(Msg(lang, "العيادة غير موجودة", "Clinic not found"));

            var added = await seedingService.SeedDefaultTemplates(clinicId);

            return Ok(new
            {
                message = Msg(lang, $"تم إنشاء {added} قالب زيارة افتراضي", $"Created {added} default visit template(s)")
            });
        }

        // ═══════════════════════════════════════
        // الخطط العلاجية
        // ═══════════════════════════════════════

        // GET: api/treatmentplans/patient/{patientId}
        [HttpGet("patient/{patientId}")]
        public async Task<ActionResult> GetByPatient(Guid patientId)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == patientId && !p.IsDeleted);
            if (patient == null) return NotFound("المريض غير موجود");
            if (patient.ClinicId != _clinicContext.ClinicId) return Forbid();

            var plans = await _db.TreatmentPlans
                .Include(p => p.Doctor)
                .Include(p => p.Sessions)
                .Where(p => p.PatientId == patientId && p.ClinicId == _clinicContext.ClinicId)
                .OrderByDescending(p => p.StartDate)
                .ToListAsync();

            return Ok(plans.Select(p => ToResponse(p)));
        }

        // GET: api/treatmentplans/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult> GetById(Guid id)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var plan = await _db.TreatmentPlans
                .Include(p => p.Doctor)
                .Include(p => p.Patient)
                .Include(p => p.Sessions)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (plan == null) return NotFound();
            if (plan.ClinicId != _clinicContext.ClinicId) return Forbid();

            return Ok(ToResponse(plan, includePatientName: true));
        }

        // POST: api/treatmentplans
        [HttpPost]
        public async Task<ActionResult> Create([FromBody] CreateTreatmentPlanDto dto, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("appointments.create")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var clinicId = _clinicContext.ClinicId.Value;

            // تحقق أن المريض ينتمي لنفس العيادة
            var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == dto.PatientId && !p.IsDeleted);
            if (patient == null) return BadRequest(Msg(lang, "المريض غير موجود", "Patient not found"));
            if (patient.ClinicId != clinicId) return Forbid();

            // تحقق من الطبيب لو محدد
            if (dto.DoctorId.HasValue)
            {
                var doctor = await _db.Doctors.FindAsync(dto.DoctorId.Value);
                if (doctor == null || doctor.IsDeleted || doctor.ClinicId != clinicId)
                    return BadRequest(Msg(lang, "الطبيب غير موجود", "Doctor not found"));
            }

            // ── تحديد قيم الخطة: إما من القالب، أو مخصصة يدويًا ──
            string name;
            int totalSessions;
            decimal? pricePerSession = dto.PricePerSession;
            decimal? totalPrice = dto.TotalPrice;

            if (dto.TemplateId.HasValue)
            {
                var template = await _db.TreatmentPlanTemplates
                    .FirstOrDefaultAsync(t => t.Id == dto.TemplateId.Value && t.ClinicId == clinicId && t.IsActive);

                if (template == null)
                    return BadRequest(Msg(lang, "القالب غير موجود", "Template not found"));

                name = dto.Name ?? template.Name;
                totalSessions = dto.TotalSessions ?? template.DefaultSessionsCount;
                pricePerSession ??= template.DefaultPricePerSession;
                totalPrice ??= template.DefaultTotalPrice;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(Msg(lang, "اسم الخطة مطلوب لخطة مخصصة", "Plan name is required for a custom plan"));
                if (!dto.TotalSessions.HasValue || dto.TotalSessions.Value < 1)
                    return BadRequest(Msg(lang, "عدد الجلسات يجب أن يكون واحد على الأقل", "Total sessions must be at least 1"));

                name = dto.Name;
                totalSessions = dto.TotalSessions.Value;
            }

            if (dto.PricingType == "per_session" && pricePerSession == null)
                return BadRequest(Msg(lang, "سعر الجلسة مطلوب", "Price per session is required"));
            if (dto.PricingType == "total" && totalPrice == null)
                return BadRequest(Msg(lang, "السعر الإجمالي مطلوب", "Total price is required"));

            var plan = new TreatmentPlan
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                PatientId = dto.PatientId,
                DoctorId = dto.DoctorId,
                TemplateId = dto.TemplateId,
                Name = name,
                TotalSessions = totalSessions,
                PricingType = dto.PricingType,
                PricePerSession = pricePerSession,
                TotalPrice = totalPrice,
                PaymentType = dto.PaymentType,
                Status = "active",
                StartDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };

            _db.TreatmentPlans.Add(plan);

            // ✅ توليد الجلسات تلقائيًا حسب عدد الجلسات بالخطة
            var sessionCost = dto.PricingType == "per_session"
                ? pricePerSession
                : (totalPrice.HasValue ? Math.Round(totalPrice.Value / totalSessions, 3) : (decimal?)null);

            for (int i = 1; i <= totalSessions; i++)
            {
                _db.TreatmentSessions.Add(new TreatmentSession
                {
                    Id = Guid.NewGuid(),
                    TreatmentPlanId = plan.Id,
                    SessionNumber = i,
                    Status = "scheduled",
                    Cost = sessionCost,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            await _db.SaveChangesAsync();

            var created = await _db.TreatmentPlans
                .Include(p => p.Sessions)
                .Include(p => p.Doctor)
                .FirstAsync(p => p.Id == plan.Id);

            return CreatedAtAction(nameof(GetById), new { id = plan.Id }, ToResponse(created));
        }

        // PATCH: api/treatmentplans/{id}/status
        [HttpPatch("{id}/status")]
        public async Task<ActionResult> UpdateStatus(Guid id, [FromBody] UpdateTreatmentPlanStatusDto dto, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("appointments.edit")) return Forbid();

            var plan = await _db.TreatmentPlans.FindAsync(id);
            if (plan == null) return NotFound();
            if (plan.ClinicId != _clinicContext.ClinicId) return Forbid();

            var validStatuses = new[] { "active", "completed", "cancelled", "paused" };
            if (!validStatuses.Contains(dto.Status))
                return BadRequest(Msg(lang, "حالة غير صحيحة", "Invalid status"));

            plan.Status = dto.Status;
            await _db.SaveChangesAsync();

            return Ok(new { message = Msg(lang, "تم تحديث حالة الخطة", "Plan status updated") });
        }

        // ═══════════════════════════════════════
        // الجلسات
        // ═══════════════════════════════════════

        // PUT: api/treatmentplans/{planId}/sessions/{sessionId}
        [HttpPut("{planId}/sessions/{sessionId}")]
        public async Task<ActionResult> UpdateSession(Guid planId, Guid sessionId, [FromBody] UpdateSessionDto dto, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("appointments.edit")) return Forbid();

            var plan = await _db.TreatmentPlans.FindAsync(planId);
            if (plan == null) return NotFound(Msg(lang, "الخطة غير موجودة", "Plan not found"));
            if (plan.ClinicId != _clinicContext.ClinicId) return Forbid();

            var session = await _db.TreatmentSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.TreatmentPlanId == planId);
            if (session == null) return NotFound(Msg(lang, "الجلسة غير موجودة", "Session not found"));

            var validStatuses = new[] { "scheduled", "completed", "missed", "cancelled" };
            if (!string.IsNullOrEmpty(dto.Status) && !validStatuses.Contains(dto.Status))
                return BadRequest(Msg(lang, "حالة غير صحيحة", "Invalid status"));

            // لو حددوا موعد فعلي، تحقق إنه يخص نفس العيادة
            if (dto.AppointmentId.HasValue)
            {
                var appt = await _db.Appointments.FindAsync(dto.AppointmentId.Value);
                if (appt == null || appt.IsDeleted || appt.ClinicId != _clinicContext.ClinicId)
                    return BadRequest(Msg(lang, "الموعد غير موجود", "Appointment not found"));
                session.AppointmentId = dto.AppointmentId;
            }

            if (!string.IsNullOrEmpty(dto.Status))
            {
                session.Status = dto.Status;
                if (dto.Status == "completed")
                    session.CompletedAt = DateTime.UtcNow;
            }

            if (dto.ScheduledDate.HasValue) session.ScheduledDate = dto.ScheduledDate;
            if (dto.Cost.HasValue) session.Cost = dto.Cost;
            if (dto.IsPaid.HasValue) session.IsPaid = dto.IsPaid.Value;
            if (dto.Notes != null) session.Notes = dto.Notes;

            await _db.SaveChangesAsync();

            // ✅ لو كل الجلسات خلصت (completed)، علّم الخطة نفسها "completed" تلقائيًا
            var allSessions = await _db.TreatmentSessions
                .Where(s => s.TreatmentPlanId == planId)
                .ToListAsync();

            if (allSessions.All(s => s.Status == "completed") && plan.Status == "active")
            {
                plan.Status = "completed";
                await _db.SaveChangesAsync();
            }

            return Ok(new { message = Msg(lang, "تم تحديث الجلسة", "Session updated") });
        }

        // POST: api/treatmentplans/{planId}/sessions
        // إضافة جلسة إضافية استثنائية فوق العدد الأصلي بالخطة
        [HttpPost("{planId}/sessions")]
        public async Task<ActionResult> AddExtraSession(Guid planId, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("appointments.edit")) return Forbid();

            var plan = await _db.TreatmentPlans.FindAsync(planId);
            if (plan == null) return NotFound();
            if (plan.ClinicId != _clinicContext.ClinicId) return Forbid();

            var lastSessionNumber = await _db.TreatmentSessions
                .Where(s => s.TreatmentPlanId == planId)
                .MaxAsync(s => (int?)s.SessionNumber) ?? 0;

            var session = new TreatmentSession
            {
                Id = Guid.NewGuid(),
                TreatmentPlanId = planId,
                SessionNumber = lastSessionNumber + 1,
                Status = "scheduled",
                Cost = plan.PricePerSession,
                CreatedAt = DateTime.UtcNow,
            };

            _db.TreatmentSessions.Add(session);
            plan.TotalSessions += 1;

            await _db.SaveChangesAsync();

            return Ok(new { session.Id, sessionNumber = session.SessionNumber, message = Msg(lang, "تم إضافة جلسة إضافية", "Extra session added") });
        }

        // ═══════════════════════════════════════
        // دالة مساعدة
        // ═══════════════════════════════════════
        private static object ToResponse(TreatmentPlan p, bool includePatientName = false)
        {
            var completedCount = p.Sessions.Count(s => s.Status == "completed");
            var totalCount = p.Sessions.Count;

            return new
            {
                p.Id,
                p.Name,
                p.PatientId,
                patientName = includePatientName ? p.Patient?.FullName : null,
                p.DoctorId,
                doctorName = p.Doctor?.FullName,
                p.TotalSessions,
                p.PricingType,
                p.PricePerSession,
                p.TotalPrice,
                p.PaymentType,
                p.Status,
                startDate = p.StartDate.ToString("yyyy-MM-dd"),
                completedSessions = completedCount,
                remainingSessions = totalCount - completedCount,
                progressPercent = totalCount > 0 ? Math.Round((double)completedCount / totalCount * 100, 0) : 0,
                sessions = p.Sessions.OrderBy(s => s.SessionNumber).Select(s => new
                {
                    s.Id,
                    s.SessionNumber,
                    s.Status,
                    s.AppointmentId,
                    scheduledDate = s.ScheduledDate?.ToString("yyyy-MM-dd"),
                    completedAt = s.CompletedAt?.ToString("yyyy-MM-dd HH:mm"),
                    s.Cost,
                    s.IsPaid,
                    s.Notes,
                }),
            };
        }
    }
}