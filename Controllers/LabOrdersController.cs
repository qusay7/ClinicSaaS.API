using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClinicSaaS.API.Filters;

namespace ClinicSaaS.API.Controllers
{
    // ✅ طلب تحليل مخبري — الطبيب يطلب (POST، صلاحية visitnotes.create نفسها
    // المستخدمة لتوثيق الزيارة)، وقسم المختبر (lab.manage) ينفّذ: يبدأ، يرفع
    // النتيجة (ملف مرفق عادي عبر /attachments/upload أولاً، بعدها attachmentId
    // هون)، أو يلغي. حالة بسيطة بدون نظام مخبري كامل.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [RequireActiveSubscription]
    public class LabOrdersController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public LabOrdersController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        // GET: api/laborders — قائمة/طابور المختبر، بفلترة اختيارية
        // ?status= &patientId= &from= &to=
        [HttpGet]
        public async Task<ActionResult> GetAll(
            [FromQuery] string? status,
            [FromQuery] Guid? patientId,
            [FromQuery] string? from,
            [FromQuery] string? to)
        {
            if (!_clinicContext.HasPermission("lab.manage")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var query = _db.LabOrders
                .Include(o => o.Patient)
                .Include(o => o.Doctor)
                .Where(o => o.ClinicId == _clinicContext.ClinicId);

            if (!string.IsNullOrEmpty(status))
                query = query.Where(o => o.Status == status);
            if (patientId.HasValue)
                query = query.Where(o => o.PatientId == patientId);
            if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fromDate))
                query = query.Where(o => o.OrderedAt >= fromDate);
            if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var toDate))
                query = query.Where(o => o.OrderedAt < toDate.AddDays(1));

            var orders = await query
                .OrderByDescending(o => o.OrderedAt)
                .ToListAsync();

            return Ok(orders.Select(ToResponse));
        }

        // GET: api/laborders/patient/{patientId} — سجل تحاليل مريض معيّن (لعرضها
        // بصفحة الزيارة/الملف الطبي) — نفس صلاحية مشاهدة ملاحظات الزيارة
        [HttpGet("patient/{patientId}")]
        public async Task<ActionResult> GetByPatient(Guid patientId)
        {
            if (!_clinicContext.HasPermission("visitnotes.view") && !_clinicContext.HasPermission("lab.manage")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var orders = await _db.LabOrders
                .Include(o => o.Patient)
                .Include(o => o.Doctor)
                .Where(o => o.ClinicId == _clinicContext.ClinicId && o.PatientId == patientId)
                .OrderByDescending(o => o.OrderedAt)
                .ToListAsync();

            return Ok(orders.Select(ToResponse));
        }

        // POST: api/laborders — الطبيب يطلب تحليل لزيارة (موعد أو طوارئ)
        [HttpPost]
        public async Task<ActionResult> Create([FromBody] CreateLabOrderDto dto)
        {
            if (!_clinicContext.HasPermission("visitnotes.create")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(dto.TestName))
                return BadRequest("اسم التحليل مطلوب");

            var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == dto.PatientId && !p.IsDeleted);
            if (patient == null) return BadRequest("المريض غير موجود");
            if (patient.ClinicId != _clinicContext.ClinicId) return Forbid();

            Guid? doctorId = dto.DoctorId;
            if (doctorId == null && _clinicContext.Role == "Doctor")
            {
                var doctorRecord = await _db.Doctors.FirstOrDefaultAsync(d =>
                    d.UserId == _clinicContext.UserId && d.ClinicId == _clinicContext.ClinicId && !d.IsDeleted);
                doctorId = doctorRecord?.Id;
            }

            var order = new LabOrder
            {
                Id = Guid.NewGuid(),
                ClinicId = _clinicContext.ClinicId.Value,
                PatientId = dto.PatientId,
                AppointmentId = dto.AppointmentId,
                QueueEntryId = dto.QueueEntryId,
                DoctorId = doctorId,
                TestName = dto.TestName.Trim(),
                Status = "ordered",
                Notes = dto.Notes,
                OrderedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };

            _db.LabOrders.Add(order);
            await _db.SaveChangesAsync();

            await _db.Entry(order).Reference(o => o.Patient).LoadAsync();
            await _db.Entry(order).Reference(o => o.Doctor).LoadAsync();

            return Ok(ToResponse(order));
        }

        // PUT: api/laborders/{id}/start
        [HttpPut("{id}/start")]
        public async Task<ActionResult> Start(Guid id)
        {
            if (!_clinicContext.HasPermission("lab.manage")) return Forbid();
            var order = await GetOwnedOrder(id);
            if (order == null) return NotFound();
            if (order.Status != "ordered") return BadRequest("الطلب ليس بحالة 'مطلوب'");

            order.Status = "in_progress";
            order.StartedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(ToResponse(order));
        }

        // PUT: api/laborders/{id}/complete — بعد رفع ملف النتيجة عبر
        // /attachments/upload (category=lab)، يُمرَّر الـ id هون
        [HttpPut("{id}/complete")]
        public async Task<ActionResult> Complete(Guid id, [FromBody] CompleteLabOrderDto dto)
        {
            if (!_clinicContext.HasPermission("lab.manage")) return Forbid();
            var order = await GetOwnedOrder(id);
            if (order == null) return NotFound();
            if (order.Status == "completed" || order.Status == "cancelled")
                return BadRequest("الطلب منتهي بالفعل");

            if (dto.ResultAttachmentId.HasValue)
            {
                var attachment = await _db.Attachments.FirstOrDefaultAsync(a =>
                    a.Id == dto.ResultAttachmentId && a.ClinicId == _clinicContext.ClinicId && !a.IsDeleted);
                if (attachment == null) return BadRequest("المرفق غير موجود");
                order.ResultAttachmentId = attachment.Id;
            }

            order.Status = "completed";
            order.ResultNotes = dto.ResultNotes;
            order.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(ToResponse(order));
        }

        // PUT: api/laborders/{id}/cancel
        [HttpPut("{id}/cancel")]
        public async Task<ActionResult> Cancel(Guid id)
        {
            if (!_clinicContext.HasPermission("lab.manage")) return Forbid();
            var order = await GetOwnedOrder(id);
            if (order == null) return NotFound();
            if (order.Status == "completed") return BadRequest("لا يمكن إلغاء طلب مكتمل");

            order.Status = "cancelled";
            await _db.SaveChangesAsync();
            return Ok(new { message = "تم إلغاء الطلب" });
        }

        private async Task<LabOrder?> GetOwnedOrder(Guid id)
        {
            if (_clinicContext.ClinicId == null) return null;
            var order = await _db.LabOrders
                .Include(o => o.Patient)
                .Include(o => o.Doctor)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null || order.ClinicId != _clinicContext.ClinicId) return null;
            return order;
        }

        private static object ToResponse(LabOrder o) => new
        {
            o.Id,
            o.PatientId,
            patientName = o.Patient?.FullName ?? "—",
            o.AppointmentId,
            o.QueueEntryId,
            o.DoctorId,
            doctorName = o.Doctor?.FullName,
            o.TestName,
            o.Status,
            o.Notes,
            o.ResultNotes,
            o.ResultAttachmentId,
            o.OrderedAt,
            o.StartedAt,
            o.CompletedAt,
        };
    }

    public class CreateLabOrderDto
    {
        public Guid PatientId { get; set; }
        public Guid? AppointmentId { get; set; }
        public Guid? QueueEntryId { get; set; }
        public Guid? DoctorId { get; set; }
        public string TestName { get; set; } = "";
        public string? Notes { get; set; }
    }

    public class CompleteLabOrderDto
    {
        public string? ResultNotes { get; set; }
        public Guid? ResultAttachmentId { get; set; }
    }
}
