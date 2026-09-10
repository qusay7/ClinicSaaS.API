using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Appointments;
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
    public class AppointmentsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly IRoleSeedingService _roleSeedingService;   // ✅ جديد
        private readonly IPdfExportService _pdfExport;
        private readonly IExcelExportService _excelExport;
        private readonly IWebHostEnvironment _env;
        private readonly INotificationService _notificationService;
        private readonly ILogger<AppointmentsController> _logger;
        public AppointmentsController(
    ApplicationDbContext db,
    IClinicContext clinicContext,
    IRoleSeedingService roleSeedingService,
    IPdfExportService pdfExport,
    IExcelExportService excelExport,
    IWebHostEnvironment env,
    INotificationService notificationService,
    ILogger<AppointmentsController> logger)
        {
            _db = db;
            _clinicContext = clinicContext;
            _roleSeedingService = roleSeedingService;
            _pdfExport = pdfExport;
            _excelExport = excelExport;
            _env = env;
            _notificationService = notificationService;
            _logger = logger;
        }

        private string? ResolveLogoPath(string? logoUrl)
        {
            if (string.IsNullOrEmpty(logoUrl)) return null;
            var cleanPath = logoUrl.Split('?')[0].TrimStart('/');
            var fullPath = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), cleanPath.Replace("logos/", "logos" + Path.DirectorySeparatorChar));
            return System.IO.File.Exists(fullPath) ? fullPath : null;
        }

        private static string Msg(string? lang, string ar, string en)
            => lang == "ar" ? ar : en;

        // GET: api/appointments?date=&doctorId=
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AppointmentResponseDto>>> GetAll(
            [FromQuery] DateTime? date, [FromQuery] Guid? doctorId,
            [FromQuery] Guid? patientId, [FromQuery] DateTime? dateFrom)
        {
            var query = _db.Appointments.Where(a => !a.IsDeleted);

            if (!_clinicContext.IsCompanyStaff)
            {
                if (_clinicContext.ClinicId == null)
                    return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

                query = query.Where(a => a.ClinicId == _clinicContext.ClinicId);

                if (_clinicContext.Role == "Doctor")
                {
                    var doctorRecord = await _db.Doctors
                        .FirstOrDefaultAsync(d => d.UserId == _clinicContext.UserId
                            && d.ClinicId == _clinicContext.ClinicId
                            && !d.IsDeleted);

                    if (doctorRecord != null)
                        query = query.Where(a => a.DoctorId == doctorRecord.Id);
                    else
                        return Ok(new List<AppointmentResponseDto>());
                }
            }

            // ✅ فلترة اختيارية — تخدم شاشة "الطبيب اليومية" (بدون تحميل كل السجل التاريخي)
            if (date.HasValue)
            {
                var dayStart = date.Value.Date;
                var dayEnd = dayStart.AddDays(1);
                query = query.Where(a => a.AppointmentDate >= dayStart && a.AppointmentDate < dayEnd);
            }
            if (doctorId.HasValue)
                query = query.Where(a => a.DoctorId == doctorId);
            // ✅ جديد — تخدم "الموعد القادم" بشاشة زيارة الطبيب
            if (patientId.HasValue)
                query = query.Where(a => a.PatientId == patientId);
            if (dateFrom.HasValue)
                query = query.Where(a => a.AppointmentDate >= dateFrom.Value);

            var appointments = await query
                .OrderByDescending(a => a.AppointmentDate)
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .ToListAsync();

            // ✅ نجيب حالة الدفع لكل المواعيد بضربة استعلام واحدة — بدل طلب منفصل لكل صف بالقائمة
            var appointmentIds = appointments.Select(a => a.Id).ToList();
            var payments = await _db.PaymentDetails
                .Where(p => appointmentIds.Contains(p.AppointmentId))
                .ToListAsync();
            var paymentLookup = payments.ToDictionary(p => p.AppointmentId, p => p);

            var result = appointments.Select(a =>
            {
                var dto = ToResponse(a);
                if (paymentLookup.TryGetValue(a.Id, out var payment))
                {
                    dto.IsPaid = payment.IsPaid;
                    dto.AmountPaid = payment.AmountPaid;
                    dto.PatientBalance = payment.PatientBalance;
                }
                // ✅ ما فيه سجل دفعة إطلاقاً — IsPaid تبقى null (يعني "ما انسجّل شي بعد"، مختلف عن false)
                return dto;
            }).ToList();

            return Ok(result);
        }

        // ✅ GET: api/appointments/export?format=pdf|excel&date=&doctorId=&patientId=&dateFrom=
        [HttpGet("export")]
        public async Task<ActionResult> Export(
            [FromQuery] DateTime? date, [FromQuery] Guid? doctorId,
            [FromQuery] Guid? patientId, [FromQuery] DateTime? dateFrom,
            [FromQuery] string format = "pdf", [FromQuery] string lang = "ar")
        {
            var isRtl = lang == "ar";
            var query = _db.Appointments.Where(a => !a.IsDeleted);

            if (!_clinicContext.IsCompanyStaff)
            {
                if (_clinicContext.ClinicId == null) return Unauthorized();
                query = query.Where(a => a.ClinicId == _clinicContext.ClinicId);

                if (_clinicContext.Role == "Doctor")
                {
                    var doctorRecord = await _db.Doctors
                        .FirstOrDefaultAsync(d => d.UserId == _clinicContext.UserId
                            && d.ClinicId == _clinicContext.ClinicId && !d.IsDeleted);
                    if (doctorRecord != null)
                        query = query.Where(a => a.DoctorId == doctorRecord.Id);
                    else
                        query = query.Where(a => false);
                }
            }

            if (date.HasValue)
            {
                var dayStart = date.Value.Date;
                var dayEnd = dayStart.AddDays(1);
                query = query.Where(a => a.AppointmentDate >= dayStart && a.AppointmentDate < dayEnd);
            }
            if (doctorId.HasValue) query = query.Where(a => a.DoctorId == doctorId);
            if (patientId.HasValue) query = query.Where(a => a.PatientId == patientId);
            if (dateFrom.HasValue) query = query.Where(a => a.AppointmentDate >= dateFrom.Value);

            var appointments = await query
                .OrderByDescending(a => a.AppointmentDate)
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .ToListAsync();

            var rows = appointments.Select(a => new List<string> {
                a.AppointmentDate.ToString("yyyy-MM-dd HH:mm"),
                a.Patient?.FullName ?? "—", a.Doctor?.FullName ?? "—",
                a.Type ?? "—",
                a.Status == "completed" ? (isRtl ? "مكتمل" : "Completed")
                    : a.Status == "cancelled" ? (isRtl ? "ملغي" : "Cancelled")
                    : a.Status == "confirmed" ? (isRtl ? "مؤكد" : "Confirmed")
                    : (isRtl ? "مجدول" : "Scheduled"),
                a.Price?.ToString("F2") ?? "—",
            }).ToList();

            var columns = isRtl
                ? new List<string> { "التاريخ", "المريض", "الطبيب", "النوع", "الحالة", "السعر" }
                : new List<string> { "Date", "Patient", "Doctor", "Type", "Status", "Price" };

            var clinic = _clinicContext.ClinicId.HasValue ? await _db.Clinics.FindAsync(_clinicContext.ClinicId.Value) : null;
            var summary = new List<(string, string)> {
                (isRtl ? "إجمالي المواعيد" : "Total Appointments", appointments.Count.ToString()),
                (isRtl ? "مكتملة" : "Completed", appointments.Count(a => a.Status == "completed").ToString()),
            };

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "المواعيد" : "Appointments",
                    Title = isRtl ? "قائمة المواعيد" : "Appointments List",
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "appointments.xlsx");
            }
            else
            {
                var bytes = _pdfExport.GenerateTableReport(new PdfReportRequest
                {
                    Title = isRtl ? "قائمة المواعيد" : "Appointments List",
                    ClinicName = clinic?.Name ?? "",
                    LogoPath = ResolveLogoPath(clinic?.Logo),
                    IsRtl = isRtl,
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                });
                return File(bytes, "application/pdf", "appointments.pdf");
            }
        }

        // GET: api/appointments/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<AppointmentResponseDto>> GetById(Guid id)
        {
            var appointment = await _db.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .FirstOrDefaultAsync(a =>
                    a.Id == id &&
                    !a.IsDeleted);

            if (appointment == null)
                return NotFound();

            if (!_clinicContext.IsSuperAdmin &&
                appointment.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            return Ok(ToResponse(appointment));
        }

        // ✅ GET: api/appointments/{id}/export?format=pdf|excel&fields=...
        [HttpGet("{id}/export")]
        public async Task<ActionResult> ExportOne(Guid id, [FromQuery] string? fields, [FromQuery] string format = "pdf", [FromQuery] string lang = "ar")
        {
            var appointment = await _db.Appointments
                .Include(a => a.Patient).Include(a => a.Doctor)
                .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
            if (appointment == null) return NotFound();
            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId) return Forbid();

            var isRtl = lang == "ar";
            var requestedFields = (fields ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            var visitNote = await _db.VisitNotes.FirstOrDefaultAsync(v => v.AppointmentId == id && !v.IsDeleted);
            var payment = await _db.PaymentDetails.FirstOrDefaultAsync(p => p.AppointmentId == id);

            var allFields = new List<(string Key, string LabelAr, string LabelEn, string? Value)>
            {
                ("patient", "المريض", "Patient", appointment.Patient?.FullName),
                ("doctor", "الطبيب", "Doctor", appointment.Doctor?.FullName),
                ("date", "الموعد", "Appointment", appointment.AppointmentDate.ToString("yyyy-MM-dd HH:mm")),
                ("type", "نوع الزيارة", "Visit Type", appointment.Type),
                ("status", "الحالة", "Status", appointment.Status),
                ("checkIn", "وقت الدخول", "Check-in", appointment.CheckInTime?.ToString("HH:mm")),
                ("checkOut", "وقت الخروج", "Check-out", appointment.CheckOutTime?.ToString("HH:mm")),
                ("price", "السعر", "Price", appointment.Price?.ToString("F2")),
                ("commission", "حصة الطبيب", "Doctor Commission", appointment.DoctorCommissionAmount?.ToString("F2")),
                ("diagnosis", "التشخيص", "Diagnosis", visitNote?.Diagnosis),
                ("prescription", "الوصفة الطبية", "Prescription", visitNote?.Prescription),
                ("tests", "الفحوصات", "Tests", visitNote?.Tests),
                ("notes", "ملاحظات", "Notes", visitNote?.Notes),
                ("nextVisit", "الزيارة القادمة", "Next Visit", visitNote?.NextVisitDate?.ToString("yyyy-MM-dd")),
                ("totalAmount", "المبلغ الإجمالي", "Total Amount", payment?.TotalAmount.ToString("F2")),
                ("amountPaid", "المبلغ المدفوع", "Amount Paid", payment?.AmountPaid.ToString("F2")),
                ("paymentMethod", "طريقة الدفع", "Payment Method", payment?.PaymentMethod),
            };

            var selected = requestedFields.Count > 0 ? allFields.Where(f => requestedFields.Contains(f.Key)) : allFields;
            var rows = selected.Where(f => !string.IsNullOrEmpty(f.Value)).Select(f => new List<string> { isRtl ? f.LabelAr : f.LabelEn, f.Value! }).ToList();

            var columns = isRtl ? new List<string> { "الحقل", "القيمة" } : new List<string> { "Field", "Value" };
            var clinic = _clinicContext.ClinicId.HasValue ? await _db.Clinics.FindAsync(_clinicContext.ClinicId.Value) : null;

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "تفاصيل الزيارة" : "Visit Details",
                    Title = appointment.Patient?.FullName ?? "",
                    Columns = columns,
                    Rows = rows,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "appointment.xlsx");
            }
            else
            {
                var bytes = _pdfExport.GenerateTableReport(new PdfReportRequest
                {
                    Title = appointment.Patient?.FullName ?? "",
                    Subtitle = isRtl ? "تفاصيل الزيارة" : "Visit Details",
                    ClinicName = clinic?.Name ?? "",
                    LogoPath = ResolveLogoPath(clinic?.Logo),
                    IsRtl = isRtl,
                    Columns = columns,
                    Rows = rows,
                });
                return File(bytes, "application/pdf", "appointment.pdf");
            }
        }

        // GET: api/appointments/today-by-doctor
        [HttpGet("today-by-doctor")]
        public async Task<ActionResult> GetTodayByDoctor()
        {
            if (_clinicContext.ClinicId == null && !_clinicContext.IsSuperAdmin)
                return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            var query = _db.Appointments
                .Where(a => !a.IsDeleted
                    && a.AppointmentDate >= today
                    && a.AppointmentDate < tomorrow
                    && a.DoctorId != null);

            if (!_clinicContext.IsCompanyStaff)
                query = query.Where(a => a.ClinicId == _clinicContext.ClinicId);

            var appointments = await query
                .Include(a => a.Doctor)
                .Include(a => a.Patient)
                .ToListAsync();

            var result = appointments
                .GroupBy(a => a.DoctorId)
                .Select(g => new {
                    doctorId = g.Key,
                    doctorName = g.First().Doctor?.FullName ?? "—",
                    doctorSpecialty = g.First().Doctor?.Specialty ?? "",
                    appointmentCount = g.Count(),
                    appointments = g.Select(a => new { id = a.Id, patientName = a.Patient.FullName, time = a.AppointmentDate }).ToList()
                })
                .OrderByDescending(d => d.appointmentCount)
                .ToList();

            return Ok(result);
        }

        // GET: api/appointments/patient/{patientId}
        [HttpGet("patient/{patientId}")]
        public async Task<ActionResult<IEnumerable<AppointmentResponseDto>>> GetByPatient(Guid patientId)
        {
            var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == patientId && !p.IsDeleted);
            if (patient == null) return NotFound("المريض غير موجود");
            if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId) return Forbid();

            var query = _db.Appointments.Where(a => a.PatientId == patientId && !a.IsDeleted);
            if (!_clinicContext.IsCompanyStaff)
                query = query.Where(a => a.ClinicId == _clinicContext.ClinicId);

            var appointments = await query
                .OrderByDescending(a => a.AppointmentDate)
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .ToListAsync();

            return Ok(appointments.Select(a => ToResponse(a)).ToList());
        }

        // POST: api/appointments
        // ═══════════════════════════════════════════════════════════════════════════
        // AppointmentsController.cs — التعديلات الوحيدة المطلوبة
        // ═══════════════════════════════════════════════════════════════════════════
        // عدّل هذه الـ 3 methods فقط لاستدعاء الإشعارات الموجودة بالفعل

        // 1️⃣ في method Create
        [HttpPost]
        public async Task<ActionResult<AppointmentResponseDto>> Create(
    [FromBody] CreateAppointmentDto dto)
        {
            if (!_clinicContext.HasPermission("appointments.create"))
                return Forbid();

            if (_clinicContext.IsSuperAdmin)
                return BadRequest("SuperAdmin لا يستطيع إضافة مواعيد مباشرة");

            if (_clinicContext.ClinicId == null)
                return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

            // التأكد من وجود المريض
            var patient = await _db.Patients
                .FirstOrDefaultAsync(p =>
                    p.Id == dto.PatientId &&
                    p.ClinicId == _clinicContext.ClinicId &&
                    !p.IsDeleted);

            if (patient == null)
                return BadRequest(
                    Msg(dto.Lang, "المريض غير موجود", "Patient not found"));

            // التأكد من وجود الطبيب إذا تم تحديده
            if (dto.DoctorId.HasValue)
            {
                var doctorExists = await _db.Doctors.AnyAsync(d =>
                    d.Id == dto.DoctorId.Value &&
                    d.ClinicId == _clinicContext.ClinicId &&
                    !d.IsDeleted);

                if (!doctorExists)
                    return BadRequest(
                        Msg(dto.Lang, "الطبيب غير موجود", "Doctor not found"));
            }

            // منع حجز موعد بتاريخ غير صالح
            if (dto.AppointmentDate == default)
            {
                return BadRequest(
                    Msg(dto.Lang, "تاريخ الموعد مطلوب", "Appointment date is required"));
            }

            var appointment = new Appointment
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false,

                ClinicId = _clinicContext.ClinicId.Value,
                PatientId = dto.PatientId,
                DoctorId = dto.DoctorId,

                AppointmentDate = dto.AppointmentDate,
                Type = dto.Type,
                Price = dto.Price,

                Status = "scheduled",

                Notes = dto.Notes,
                Notes2 = dto.Notes2,
                Notes3 = dto.Notes3
            };

            _db.Appointments.Add(appointment);

            await _db.SaveChangesAsync();

            // نعيد تحميل الموعد مع العلاقات حتى يستطيع NotificationService
            // الوصول إلى بيانات المريض والطبيب
            var savedAppointment = await _db.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .FirstOrDefaultAsync(a => a.Id == appointment.Id);

            if (savedAppointment == null)
                return StatusCode(500, "Failed to load created appointment");

            // إرسال إشعار التأكيد بدون التأثير على نجاح إنشاء الموعد
            try
            {
                if (_clinicContext.NotifyOnCreate)
                    await _notificationService.SendAppointmentConfirmation(savedAppointment);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send appointment confirmation for appointment {AppointmentId}",
                    savedAppointment.Id);
            }

            return CreatedAtAction(
                nameof(GetById),
                new { id = savedAppointment.Id },
                ToResponse(savedAppointment));
        }

        // ═══════════════════════════════════════════════════════════════════════════

        // 2️⃣ في method Update
        [HttpPut("{id}")]
        public async Task<ActionResult<AppointmentResponseDto>> Update(
    Guid id,
    [FromBody] UpdateAppointmentDto dto)
        {
            if (!_clinicContext.HasPermission("appointments.edit"))
                return Forbid();

            var appointment = await _db.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .FirstOrDefaultAsync(a =>
                    a.Id == id &&
                    !a.IsDeleted);

            if (appointment == null)
                return NotFound();

            if (!_clinicContext.IsSuperAdmin &&
                appointment.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            // تحديث التاريخ
            if (dto.AppointmentDate.HasValue)
                appointment.AppointmentDate = dto.AppointmentDate.Value;

            // تحديث الطبيب
            if (dto.DoctorId.HasValue)
            {
                var doctorExists = await _db.Doctors.AnyAsync(d =>
                    d.Id == dto.DoctorId.Value &&
                    d.ClinicId == appointment.ClinicId &&
                    !d.IsDeleted);

                if (!doctorExists)
                    return BadRequest("الطبيب غير موجود");

                appointment.DoctorId = dto.DoctorId.Value;
            }

            appointment.Type = dto.Type;
            appointment.Price = dto.Price;
            appointment.Status = dto.Status ?? appointment.Status;

            appointment.Notes = dto.Notes;
            appointment.Notes2 = dto.Notes2;
            appointment.Notes3 = dto.Notes3;

            await _db.SaveChangesAsync();

            // إعادة تحميل العلاقات بعد التعديل
            var updatedAppointment = await _db.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .FirstOrDefaultAsync(a =>
                    a.Id == id &&
                    !a.IsDeleted);

            if (updatedAppointment == null)
                return NotFound();

            try
            {
                if (_clinicContext.NotifyOnEdit)
                    await _notificationService.SendAppointmentUpdate(
                        updatedAppointment);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send appointment update notification for appointment {AppointmentId}",
                    id);
            }

            return Ok(ToResponse(updatedAppointment));
        }

        // ═══════════════════════════════════════════════════════════════════════════

        // 3️⃣ في method Delete
        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(Guid id)
        {
            if (!_clinicContext.HasPermission("appointments.delete"))
                return Forbid();

            var appointment = await _db.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .FirstOrDefaultAsync(a =>
                    a.Id == id &&
                    !a.IsDeleted);

            if (appointment == null)
                return NotFound();

            if (!_clinicContext.IsSuperAdmin &&
                appointment.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            // إرسال إشعار الإلغاء قبل الحذف
            try
            {
                if (_clinicContext.NotifyOnCancel)
                    await _notificationService.SendAppointmentCancellation(
                        appointment);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send appointment cancellation notification for appointment {AppointmentId}",
                    id);
            }

            // Soft Delete
            appointment.IsDeleted = true;

            await _db.SaveChangesAsync();

            return NoContent();
        }

        /*
        ═══════════════════════════════════════════════════════════════════════════
        ملخص التعديلات على AppointmentsController:

        1️⃣ في Create():
           ✅ بعد SaveChangesAsync()
           ✅ أضيف: await _notificationService.SendAppointmentConfirmation(appointment);
           → رسالة تأكيد: "تم تأكيد موعدك بنجاح ✅"

        2️⃣ في Update():
           ✅ بعد SaveChangesAsync()
           ✅ أضيف: await _notificationService.SendAppointmentUpdate(appointment);
           → رسالة تعديل: "تم تعديل موعدك بنجاح 🔄"

        3️⃣ في Delete():
           ✅ قبل IsDeleted = true
           ✅ أضيف: await _notificationService.SendAppointmentCancellation(appointment);
           → رسالة إلغاء: "تم إلغاء موعدك ❌"

        ═══════════════════════════════════════════════════════════════════════════
        ملاحظة: كل الدوال موجودة بالفعل في NotificationService.cs
        لا توجد أي إضافات جديدة — فقط استدعاءات!
        ═══════════════════════════════════════════════════════════════════════════
        */

        // ✅ PATCH: api/appointments/{id}/update-type
        // تصحيح "نوع الزيارة الفعلي" — يُستخدم وقت الـ Checkout لو الطبيب اكتشف إن الزيارة
        // كانت نوع مختلف عن المحجوز أصلاً (مثلاً حُجزت "كشف" لكن تبيّن إنها "استشارة").
        // لا يلمس أي شي ثاني بالموعد (لا تاريخ، لا طبيب، لا فحص تعارض) — تحديث بسيط ومقصود.
        [HttpPatch("{id}/update-type")]
        public async Task<ActionResult> UpdateVisitType(Guid id, [FromBody] UpdateVisitTypeDto dto, [FromQuery] string lang = "ar")
        {
            var appointment = await _db.Appointments.FindAsync(id);
            if (appointment == null || appointment.IsDeleted) return NotFound();
            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId) return Forbid();

            if (dto.TemplateId.HasValue)
            {
                var template = await _db.TreatmentPlanTemplates
                    .FirstOrDefaultAsync(t => t.Id == dto.TemplateId.Value && t.ClinicId == appointment.ClinicId);
                if (template == null)
                    return BadRequest(Msg(lang, "القالب غير موجود", "Template not found"));

                appointment.TemplateId = dto.TemplateId;
                appointment.Type = dto.Type ?? template.Name;
            }
            else if (!string.IsNullOrWhiteSpace(dto.Type))
            {
                appointment.Type = dto.Type;
            }

            await _db.SaveChangesAsync();
            return Ok(new { appointment.TemplateId, appointment.Type });
        }

        // POST: api/appointments/{id}/checkin
        [HttpPost("{id}/checkin")]
        public async Task<ActionResult> CheckIn(Guid id)
        {
            var appointment = await _db.Appointments.FindAsync(id);
            if (appointment == null || appointment.IsDeleted) return NotFound();
            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId) return Forbid();
            // appointment.CheckInTime = DateTime.Now;
            appointment.CheckInTime = DateTime.UtcNow;
            appointment.Status = "confirmed";
            await _db.SaveChangesAsync();
            return Ok(new { checkInTime = appointment.CheckInTime, status = appointment.Status });
        }

        // ✅ POST: api/appointments/recalculate-commissions?doctorId=
        // يعيد احتساب حصة الطبيب لأي موعد مكتمل ما احتُسبت له حصة أصلاً — مفيد للمواعيد
        // اللي اكتملت قبل ما يتحدد إعداد نسبة الطبيب بالنظام. يستخدم النسبة الحالية،
        // ويحترم ترتيب الزيارات التاريخي (كشف أول/مراجعة) وقت حدوثها فعلياً، مو الوضع الحالي
        [HttpPost("recalculate-commissions")]
        public async Task<ActionResult> RecalculateCommissions([FromQuery] Guid? doctorId, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("settlements.manage")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var query = _db.Appointments.Where(a => a.ClinicId == _clinicContext.ClinicId
                && !a.IsDeleted
                && a.Status == "completed"
                && a.DoctorId != null
                && a.DoctorCommissionAmount == null
                && a.Price != null && a.Price > 0);

            if (doctorId.HasValue) query = query.Where(a => a.DoctorId == doctorId);

            var appointments = await query.ToListAsync();
            int updated = 0;

            foreach (var appt in appointments)
            {
                // ✅ نحدد "كشف أول أو مراجعة" حسب ترتيب الزيارات وقتها التاريخي الفعلي
                // (مو حسب وضع اليوم)، عشان النتيجة تطابق اللي كان المفروض يُحسب وقتها
                var hasVisitedBefore = await _db.Appointments.AnyAsync(a =>
                    a.Id != appt.Id
                    && a.PatientId == appt.PatientId
                    && a.DoctorId == appt.DoctorId
                    && !a.IsDeleted
                    && a.Status == "completed"
                    && a.CheckOutTime != null && appt.CheckOutTime != null
                    && a.CheckOutTime < appt.CheckOutTime);

                var commission = await ResolveDoctorCommission(
                    appt.DoctorId!.Value, appt.TemplateId, !hasVisitedBefore, appt.Price!.Value);

                if (commission.HasValue)
                {
                    appt.DoctorCommissionAmount = commission;
                    updated++;
                }
            }

            if (updated > 0) await _db.SaveChangesAsync();

            return Ok(new
            {
                updated,
                total = appointments.Count,
                message = Msg(lang,
                    $"تم احتساب حصة {updated} موعد من أصل {appointments.Count}",
                    $"Commission calculated for {updated} of {appointments.Count} appointment(s)"),
            });
        }

        // POST: api/appointments/{id}/checkout
        [HttpPost("{id}/checkout")]
        public async Task<ActionResult> CheckOut(Guid id, [FromQuery] string lang = "ar")
        {
            var appointment = await _db.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
            if (appointment == null) return NotFound();
            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId) return Forbid();
            if (appointment.CheckInTime == null)
                return BadRequest(Msg(lang, "لم يتم تسجيل الدخول بعد", "Check-in not recorded yet"));

            //appointment.CheckOutTime = DateTime.Now;
            appointment.CheckOutTime = DateTime.UtcNow;
            appointment.Status = "completed";

            // ✅ حساب وتخزين حصة الطبيب (Snapshot ثابت — ما يتغيّر لو تغيّرت النسبة مستقبلاً)
            if (appointment.DoctorId.HasValue && (appointment.Price ?? 0) > 0)
            {
                var hasVisitedBefore = await _db.Appointments.AnyAsync(a =>
      a.Id != appointment.Id
      && a.PatientId == appointment.PatientId
      && a.DoctorId == appointment.DoctorId
      && !a.IsDeleted
      && a.Status == "completed"
      && a.CheckOutTime != null
      && appointment.CheckOutTime != null
      && a.CheckOutTime < appointment.CheckOutTime);

                appointment.DoctorCommissionAmount = await ResolveDoctorCommission(
                    appointment.DoctorId.Value,
                    appointment.TemplateId,
                    !hasVisitedBefore,
                    appointment.Price!.Value);
            }

            // ✅ تحقق من تأمين المريض وأنشئ مطالبة تلقائياً
            string? claimNumber = null;
            decimal? insuranceAmount = null;
            decimal? patientAmount = null;
            string? companyName = null;

            var totalAmount = appointment.Price ?? 0;

            if (totalAmount > 0)
            {
                var insurance = await _db.PatientInsurances
                    .Include(p => p.InsuranceCompany)
                    .FirstOrDefaultAsync(p =>
                        p.PatientId == appointment.PatientId &&
                        p.ClinicId == appointment.ClinicId &&
                        p.IsActive && p.IsPrimary &&
                        p.EndDate >= DateTime.UtcNow);

                if (insurance != null)
                {
                    insuranceAmount = Math.Round(totalAmount * insurance.CoverageRate / 100, 3);
                    patientAmount = totalAmount - insuranceAmount;
                    companyName = insurance.InsuranceCompany?.Name;

                    // إنشاء مطالبة تأمين تلقائياً
                    var claim = new InsuranceClaim
                    {
                        Id = Guid.NewGuid(),
                        ClinicId = appointment.ClinicId,
                        PatientId = appointment.PatientId,
                        PatientInsuranceId = insurance.Id,
                        AppointmentId = appointment.Id,
                        ClaimNumber = $"CLM-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}",
                        TotalAmount = totalAmount,
                        CoverageRate = insurance.CoverageRate,
                        InsuranceAmount = insuranceAmount.Value,
                        PatientAmount = patientAmount.Value,
                        Status = "pending",
                        ServiceDate = appointment.AppointmentDate,
                        CreatedAt = DateTime.UtcNow,
                    };
                    _db.InsuranceClaims.Add(claim);
                    claimNumber = claim.ClaimNumber;
                }
            }

            await _db.SaveChangesAsync();
            var duration = appointment.CheckOutTime - appointment.CheckInTime;

            return Ok(new
            {
                checkOutTime = appointment.CheckOutTime,
                status = appointment.Status,
                durationMinutes = (int)duration!.Value.TotalMinutes,
                totalAmount,
                doctorCommissionAmount = appointment.DoctorCommissionAmount,   // ✅ جديد
                // ✅ بيانات التأمين
                hasInsurance = insuranceAmount.HasValue,
                companyName,
                insuranceAmount,
                patientAmount,
                claimNumber,
                message = insuranceAmount.HasValue
                    ? Msg(lang,
                        $"تم إنشاء مطالبة تأمين برقم {claimNumber} — المريض يدفع {patientAmount:F2} د.أ",
                        $"Insurance claim {claimNumber} created — Patient pays {patientAmount:F2} JD")
                    : Msg(lang, "تم إتمام الزيارة", "Visit completed"),
            });
        }

        // GET: api/appointments/doctor-status/{doctorId}
        [HttpGet("doctor-status/{doctorId}")]
        public async Task<ActionResult> GetDoctorStatus(Guid doctorId)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var now = DateTime.UtcNow;
            var today = now.Date;

            var currentAppointment = await _db.Appointments
                .Where(a => a.DoctorId == doctorId && a.ClinicId == _clinicContext.ClinicId
                    && !a.IsDeleted && a.Status != "cancelled"
                    && a.AppointmentDate <= now.AddMinutes(30) && a.AppointmentDate >= now.AddMinutes(-30))
                .Include(a => a.Patient)
                .FirstOrDefaultAsync();

            var queueCount = await _db.QueueEntries
                .CountAsync(q => q.DoctorId == doctorId && q.ClinicId == _clinicContext.ClinicId
                    && q.Date == today && !q.IsDeleted && (q.Status == "waiting" || q.Status == "called"));

            var nextAppointment = await _db.Appointments
                .Where(a => a.DoctorId == doctorId && a.ClinicId == _clinicContext.ClinicId
                    && !a.IsDeleted && a.Status == "scheduled" && a.AppointmentDate > now)
                .OrderBy(a => a.AppointmentDate)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                isBusy = currentAppointment != null,
                queueCount,
                currentPatient = currentAppointment?.Patient?.FullName,
                nextAppointmentTime = nextAppointment?.AppointmentDate.ToString("HH:mm"),
            });
        }

        // POST: api/appointments/seed-defaults/{clinicId}
        // POST: api/appointments/seed-defaults/{clinicId}
        [HttpPost("seed-defaults/{clinicId}")]
        [RequireActiveSubscription]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult> SeedDefaultRoles(Guid clinicId)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null) return NotFound("العيادة غير موجودة");

            var added = await _roleSeedingService.SeedDefaultRoles(clinicId);

            return Ok(new { message = "تم إنشاء الأدوار الأساسية بنجاح" });
        }
        // ✅ POST: api/appointments/{id}/visit-types
        // يحفظ بنود الفاتورة كأنواع زيارة فعلية — سطر لكل بند على نفس الموعد،
        // ويعتمد البند الأول كنوع الزيارة الرئيسي للموعد (تقارير وحصة الطبيب)
        [HttpPost("{id}/visit-types")]
        public async Task<ActionResult> SaveVisitTypes(Guid id, [FromBody] SaveVisitTypesDto dto, [FromQuery] string lang = "ar")
        {
            var appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
            if (appointment == null) return NotFound();
            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId) return Forbid();
            if (!_clinicContext.IsSuperAdmin && !_clinicContext.HasPermission("appointments.edit")) return Forbid();

            var items = dto.Items ?? new List<VisitTypeItemDto>();
            if (items.Count == 0)
                return BadRequest(Msg(lang, "لا توجد بنود", "No items provided"));

            var templateIds = items.Select(i => i.TemplateId).Distinct().ToList();
            var templates = await _db.TreatmentPlanTemplates
                .Where(t => templateIds.Contains(t.Id) && t.ClinicId == appointment.ClinicId)
                .ToListAsync();

            if (templates.Count != templateIds.Count)
                return BadRequest(Msg(lang, "أحد القوالب غير موجود", "One of the templates was not found"));

            var existing = await _db.AppointmentVisitTypes
                .Where(v => v.AppointmentId == id)
                .ToListAsync();
            _db.AppointmentVisitTypes.RemoveRange(existing);

            foreach (var item in items)
            {
                _db.AppointmentVisitTypes.Add(new AppointmentVisitType
                {
                    Id = Guid.NewGuid(),
                    ClinicId = appointment.ClinicId,
                    AppointmentId = appointment.Id,
                    TemplateId = item.TemplateId,
                    Price = item.Price,
                    InsuranceRate = item.InsuranceRate,
                    InsuranceAmount = item.InsuranceAmount,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            var firstTemplate = templates.First(t => t.Id == items[0].TemplateId);
            appointment.TemplateId = firstTemplate.Id;
            appointment.Type = firstTemplate.Name;
            appointment.Price = items.Sum(i => i.Price);

            await _db.SaveChangesAsync();

            return Ok(new { count = items.Count, appointment.TemplateId, appointment.Type, appointment.Price });
        }

        // ✅ GET: api/appointments/{id}/visit-types — بنود الفاتورة لهذا الموعد
        [HttpGet("{id}/visit-types")]
        public async Task<ActionResult> GetVisitTypes(Guid id)
        {
            var appointment = await _db.Appointments
    .Include(a => a.Patient)
    .Include(a => a.Doctor)
    .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
            if (appointment == null) return NotFound();
            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId) return Forbid();

            var items = await _db.AppointmentVisitTypes
                .Where(v => v.AppointmentId == id)
                .Include(v => v.Template)
                .Select(v => new {
                    v.Id,
                    v.TemplateId,
                    name = v.Template!.Name,
                    nameEn = v.Template.NameEn,
                    v.Price,
                    v.InsuranceRate,
                    v.InsuranceAmount,
                })
                .ToListAsync();

            return Ok(items);
        }
        // ═══════════════════════════════════════
        // ✅ دوال حل السعر والحصة — الأولوية:
        // 1) استثناء خاص بالطبيب لهذا القالب تحديداً
        // 2) سعر/حصة القالب العام (للسعر فقط) أو الإعداد العام للطبيب (للحصة فقط)
        // 3) fallback على دوام الطبيب الافتراضي (للسعر فقط — الحصة بدون fallback، يعني بدون نظام عمولة)
        // ═══════════════════════════════════════

        //private async Task<decimal?> ResolveVisitPrice(Guid doctorId, Guid? templateId, bool isFirstVisit, DoctorSchedule doctorSchedule)
        //{
        //    if (templateId.HasValue)
        //    {
        //        // 1 — سعر خاص بالطبيب لهذا القالب بالذات (الأكثر تحديداً)
        //        var doctorSetting = await _db.DoctorTemplateSettings
        //            .FirstOrDefaultAsync(s => s.DoctorId == doctorId && s.TemplateId == templateId && s.IsActive);

        //        if (doctorSetting != null)
        //        {
        //            var customPrice = isFirstVisit ? doctorSetting.FirstVisitPrice : doctorSetting.FollowUpPrice;
        //            if (customPrice.HasValue) return customPrice;
        //        }

        //        // 2 — الإعداد العام للطبيب (TemplateId = null) — سعره الشخصي الافتراضي،
        //        // يطبّق على أي قالب ما له استثناء خاص بالخطوة السابقة
        //        var doctorGeneralSetting = await _db.DoctorTemplateSettings
        //            .FirstOrDefaultAsync(s => s.DoctorId == doctorId && s.TemplateId == null && s.IsActive);

        //        if (doctorGeneralSetting != null)
        //        {
        //            var generalPrice = isFirstVisit ? doctorGeneralSetting.FirstVisitPrice : doctorGeneralSetting.FollowUpPrice;
        //            if (generalPrice.HasValue) return generalPrice;
        //        }

        //        // 3 — سعر القالب العام (لو الطبيب ما له أي سعر شخصي إطلاقاً)
        //        var template = await _db.TreatmentPlanTemplates.FindAsync(templateId.Value);
        //        if (template != null)
        //        {
        //            var templatePrice = isFirstVisit ? template.FirstVisitPrice : template.FollowUpPrice;
        //            if (templatePrice.HasValue) return templatePrice;
        //        }
        //    }

        //    // 4 — fallback: سعر دوام الطبيب الافتراضي (النظام القديم، يبقى شغّال للمواعيد بدون قالب)
        //    return isFirstVisit ? doctorSchedule.FirstVisitPrice : doctorSchedule.FollowUpPrice;
        //}

        private async Task<decimal?> ResolveDoctorCommission(Guid doctorId, Guid? templateId, bool isFirstVisit, decimal chargedAmount)
        {
            string? commissionType = null;
            decimal? rate = null;

            // 1 — استثناء حصة خاص لهذا القالب بالذات — بس لو فعليًا فيه نسبة مُدخلة
            // (لو الاستثناء موجود لكن نسبته فاضية، نتجاهله ونكمل للإعداد العام تحت —
            // هذا كان الخلل: كنا نتوقف عند أول سطر مطابق حتى لو نسبته فاضية)
            if (templateId.HasValue)
            {
                var specific = await _db.DoctorTemplateSettings
                    .FirstOrDefaultAsync(s => s.DoctorId == doctorId && s.TemplateId == templateId && s.IsActive);

                if (specific != null)
                {
                    var specificRate = isFirstVisit ? specific.FirstVisitCommissionRate : specific.FollowUpCommissionRate;
                    if (specificRate.HasValue)
                    {
                        rate = specificRate;
                        commissionType = specific.CommissionType;
                    }
                }
            }

            // 2 — وإلا، الإعداد العام لهذا الطبيب (TemplateId = null)
            if (!rate.HasValue)
            {
                var general = await _db.DoctorTemplateSettings
                    .FirstOrDefaultAsync(s => s.DoctorId == doctorId && s.TemplateId == null && s.IsActive);

                if (general != null)
                {
                    var generalRate = isFirstVisit ? general.FirstVisitCommissionRate : general.FollowUpCommissionRate;
                    if (generalRate.HasValue)
                    {
                        rate = generalRate;
                        commissionType = general.CommissionType;
                    }
                }
            }

            // 3 — ما فيه أي نسبة مُدخلة (لا خاصة ولا عامة) — طبيعي وسليم، ما فيه حصة تُحسب
            if (!rate.HasValue || commissionType == null) return null;

            return commissionType == "fixed"
                ? rate.Value
                : Math.Round(chargedAmount * rate.Value / 100, 3);
        }

        private static AppointmentResponseDto ToResponse(Appointment a) => new AppointmentResponseDto
        {
            Id = a.Id,
            PatientId = a.PatientId,
            PatientName = a.Patient.FullName,
            PatientNumber = a.Patient.PatientNumber,
            AppointmentDate = a.AppointmentDate,
            DoctorId = a.DoctorId,
            DoctorName = a.Doctor?.FullName,
            TemplateId = a.TemplateId,   // ✅ جديد
            Type = a.Type,
            Price = a.Price,
            Status = a.Status,
            Notes = a.Notes,
            Notes2 = a.Notes2,
            Notes3 = a.Notes3,
            CreatedAt = a.CreatedAt,
            CheckInTime = a.CheckInTime,
            CheckOutTime = a.CheckOutTime,
            DoctorCommissionAmount = a.DoctorCommissionAmount,   // ✅ جديد
        };
    }

    public class UpdateVisitTypeDto
    {
        public Guid? TemplateId { get; set; }
        public string? Type { get; set; }
    }

    public class SaveVisitTypesDto
    {
        public List<VisitTypeItemDto>? Items { get; set; }
    }

    public class VisitTypeItemDto
    {
        public Guid TemplateId { get; set; }
        public decimal Price { get; set; }
        public decimal InsuranceRate { get; set; }
        public decimal InsuranceAmount { get; set; }
    }
}