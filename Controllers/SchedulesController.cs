using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Schedules;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SchedulesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly IPdfExportService _pdfExport;
        private readonly IExcelExportService _excelExport;
        private readonly IWebHostEnvironment _env;

        public SchedulesController(ApplicationDbContext db, IClinicContext clinicContext,
            IPdfExportService pdfExport, IExcelExportService excelExport, IWebHostEnvironment env)
        {
            _db = db;
            _clinicContext = clinicContext;
            _pdfExport = pdfExport;
            _excelExport = excelExport;
            _env = env;
        }

        private string? ResolveLogoPath(string? logoUrl)
        {
            if (string.IsNullOrEmpty(logoUrl)) return null;
            var cleanPath = logoUrl.Split('?')[0].TrimStart('/');
            var fullPath = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), cleanPath.Replace("logos/", "logos" + Path.DirectorySeparatorChar));
            return System.IO.File.Exists(fullPath) ? fullPath : null;
        }

        private static string Msg(string lang, string ar, string en)
            => lang == "ar" ? ar : en;

        // ✅ يحسب الوقت الحالي بتوقيت العيادة المحلي بدل توقيت ثابت
        private async Task<DateTime> GetClinicNow(Guid clinicId)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            var tzId = clinic?.TimeZone ?? "Asia/Amman";
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
            catch
            {
                return DateTime.UtcNow;
            }
        }

        // ═══════ CLINIC ═══════

        [HttpGet("clinic")]
        public async Task<ActionResult<IEnumerable<ClinicScheduleResponseDto>>> GetClinicSchedule()
        {
            if (_clinicContext.ClinicId == null && !_clinicContext.IsSuperAdmin)
                return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

            var clinicId = _clinicContext.ClinicId!.Value;
            var schedules = await _db.ClinicSchedules
                .Where(s => s.ClinicId == clinicId)
                .OrderBy(s => s.DayOfWeek)
                .ToListAsync();

            return Ok(schedules.Select(ToClinicResponse).ToList());
        }

        // ✅ GET: api/schedules/clinic/export?format=pdf|excel
        [HttpGet("clinic/export")]
        public async Task<ActionResult> ExportClinicSchedule([FromQuery] string format = "pdf", [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null && !_clinicContext.IsSuperAdmin) return Unauthorized();
            var isRtl = lang == "ar";
            var clinicId = _clinicContext.ClinicId!.Value;

            var schedules = await _db.ClinicSchedules
                .Where(s => s.ClinicId == clinicId)
                .OrderBy(s => s.DayOfWeek)
                .ToListAsync();

            var rows = schedules.Select(s => new List<string> {
                isRtl ? GetDayName(s.DayOfWeek) : s.DayOfWeek.ToString(),
                s.OpenTime.ToString("HH:mm"), s.CloseTime.ToString("HH:mm"),
                s.IsActive ? (isRtl ? "نشط" : "Active") : (isRtl ? "غير نشط" : "Inactive"),
            }).ToList();

            var columns = isRtl
                ? new List<string> { "اليوم", "وقت الفتح", "وقت الإغلاق", "الحالة" }
                : new List<string> { "Day", "Open", "Close", "Status" };

            var clinic = await _db.Clinics.FindAsync(clinicId);

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "دوام العيادة" : "Clinic Schedule",
                    Title = isRtl ? "جدول دوام العيادة" : "Clinic Schedule",
                    Columns = columns,
                    Rows = rows,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "clinic-schedule.xlsx");
            }
            else
            {
                var bytes = _pdfExport.GenerateTableReport(new PdfReportRequest
                {
                    Title = isRtl ? "جدول دوام العيادة" : "Clinic Schedule",
                    ClinicName = clinic?.Name ?? "",
                    LogoPath = ResolveLogoPath(clinic?.Logo),
                    IsRtl = isRtl,
                    Columns = columns,
                    Rows = rows,
                });
                return File(bytes, "application/pdf", "clinic-schedule.pdf");
            }
        }

        [HttpPost("clinic")]
        public async Task<ActionResult<ClinicScheduleResponseDto>> AddClinicDay(
            [FromBody] CreateClinicScheduleDto dto,
            [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("schedules.clinic.add")) return Forbid();
            if (_clinicContext.ClinicId == null)
                return Unauthorized(Msg(lang, "لا توجد عيادة مرتبطة بهذا المستخدم", "No clinic associated"));

            var clinicId = _clinicContext.ClinicId.Value;

            var exists = await _db.ClinicSchedules
                .AnyAsync(s => s.ClinicId == clinicId && s.DayOfWeek == dto.DayOfWeek);
            if (exists)
                return BadRequest(Msg(lang, "هذا اليوم موجود مسبقاً في جدول العيادة", "This day already exists in clinic schedule"));

            if (dto.OpenTime >= dto.CloseTime)
                return BadRequest(Msg(lang, "وقت الفتح يجب أن يكون قبل وقت الإغلاق", "Open time must be before close time"));

            var schedule = new ClinicSchedule
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                DayOfWeek = dto.DayOfWeek,
                OpenTime = dto.OpenTime,
                CloseTime = dto.CloseTime,
                IsActive = true,
            };
            _db.ClinicSchedules.Add(schedule);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetClinicSchedule), ToClinicResponse(schedule));
        }

        [HttpPut("clinic/{id}")]
        public async Task<ActionResult<ClinicScheduleResponseDto>> UpdateClinicDay(
            Guid id, [FromBody] CreateClinicScheduleDto dto,
            [FromQuery] string lang = "ar")
        {
            var schedule = await _db.ClinicSchedules.FindAsync(id);
            if (schedule == null) return NotFound();
            if (schedule.ClinicId != _clinicContext.ClinicId && !_clinicContext.IsCompanyStaff) return Forbid();

            if (dto.OpenTime >= dto.CloseTime)
                return BadRequest(Msg(lang, "وقت الفتح يجب أن يكون قبل وقت الإغلاق", "Open time must be before close time"));

            schedule.OpenTime = dto.OpenTime;
            schedule.CloseTime = dto.CloseTime;
            await _db.SaveChangesAsync();
            return Ok(ToClinicResponse(schedule));
        }

        [HttpDelete("clinic/{id}")]
        public async Task<ActionResult> DeleteClinicDay(Guid id, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("schedules.clinic.delete")) return Forbid();
            var schedule = await _db.ClinicSchedules.FindAsync(id);
            if (schedule == null) return NotFound();
            if (schedule.ClinicId != _clinicContext.ClinicId && !_clinicContext.IsCompanyStaff) return Forbid();

            // ✅ تحقق من وجود مواعيد مستقبلية بالعيادة بنفس يوم الأسبوع
            // (نجيب المواعيد المرشّحة أولاً، ونقارن DayOfWeek بالذاكرة — EF Core عاجز يترجم
            // هذا التعبير مباشرة لـ SQL بمزوّد SQL Server ضمن شرط مركّب زي هذا)
            var upcomingDates = await _db.Appointments
                .Where(a => a.ClinicId == schedule.ClinicId &&
                    !a.IsDeleted &&
                    a.Status != "cancelled" &&
                    a.AppointmentDate > DateTime.UtcNow)
                .Select(a => a.AppointmentDate)
                .ToListAsync();

            var hasUpcoming = upcomingDates.Any(d => d.DayOfWeek == schedule.DayOfWeek);

            if (hasUpcoming)
                return BadRequest(Msg(lang,
                    "لا يمكن حذف هذا اليوم لوجود مواعيد مستقبلية مرتبطة به",
                    "Cannot delete this day — future appointments are linked to it"));

            _db.ClinicSchedules.Remove(schedule);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ═══════ DOCTOR ═══════

        [HttpGet("doctor/{doctorId}")]
        public async Task<ActionResult<IEnumerable<DoctorScheduleResponseDto>>> GetDoctorSchedule(Guid doctorId)
        {
            var doctor = await _db.Doctors.FindAsync(doctorId);
            if (doctor == null || doctor.IsDeleted) return NotFound("الطبيب غير موجود");
            if (!_clinicContext.IsCompanyStaff && doctor.ClinicId != _clinicContext.ClinicId) return Forbid();

            if (_clinicContext.Role == "Doctor")
            {
                var userEmail = await _db.Users
                    .Where(u => u.Id == _clinicContext.UserId)
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync();
                var doctorRecord = await _db.Doctors
                    .FirstOrDefaultAsync(d => d.Email == userEmail
                        && d.ClinicId == _clinicContext.ClinicId && !d.IsDeleted);
                if (doctorRecord == null || doctorRecord.Id != doctorId) return Forbid();
            }

            var schedules = await _db.DoctorSchedules
                .Include(s => s.Doctor)
                .Where(s => s.DoctorId == doctorId)
                .OrderBy(s => s.DayOfWeek)
                .ToListAsync();

            return Ok(schedules.Select(ToDoctorResponse).ToList());
        }

        // ✅ GET: api/schedules/doctor/{doctorId}/export?format=pdf|excel
        [HttpGet("doctor/{doctorId}/export")]
        public async Task<ActionResult> ExportDoctorSchedule(Guid doctorId, [FromQuery] string format = "pdf", [FromQuery] string lang = "ar")
        {
            var doctor = await _db.Doctors.FindAsync(doctorId);
            if (doctor == null || doctor.IsDeleted) return NotFound();
            if (!_clinicContext.IsCompanyStaff && doctor.ClinicId != _clinicContext.ClinicId) return Forbid();

            var isRtl = lang == "ar";
            var schedules = await _db.DoctorSchedules
                .Where(s => s.DoctorId == doctorId)
                .OrderBy(s => s.DayOfWeek)
                .ToListAsync();

            var rows = schedules.Select(s => new List<string> {
                isRtl ? GetDayName(s.DayOfWeek) : s.DayOfWeek.ToString(),
                s.StartTime.ToString("HH:mm"), s.EndTime.ToString("HH:mm"),
                $"{s.SlotDuration} {(isRtl ? "دقيقة" : "min")}",
                s.IsActive ? (isRtl ? "نشط" : "Active") : (isRtl ? "غير نشط" : "Inactive"),
            }).ToList();

            var columns = isRtl
                ? new List<string> { "اليوم", "من", "إلى", "مدة الموعد", "الحالة" }
                : new List<string> { "Day", "From", "To", "Slot Duration", "Status" };

            var clinic = await _db.Clinics.FindAsync(doctor.ClinicId);

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "دوام الطبيب" : "Doctor Schedule",
                    Title = isRtl ? $"جدول دوام — {doctor.FullName}" : $"Schedule — {doctor.FullName}",
                    Columns = columns,
                    Rows = rows,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "doctor-schedule.xlsx");
            }
            else
            {
                var bytes = _pdfExport.GenerateTableReport(new PdfReportRequest
                {
                    Title = isRtl ? "جدول دوام الطبيب" : "Doctor Schedule",
                    Subtitle = doctor.FullName,
                    ClinicName = clinic?.Name ?? "",
                    LogoPath = ResolveLogoPath(clinic?.Logo),
                    IsRtl = isRtl,
                    Columns = columns,
                    Rows = rows,
                });
                return File(bytes, "application/pdf", "doctor-schedule.pdf");
            }
        }

        [HttpPost("doctor")]
        public async Task<ActionResult> AddDoctorDay(
            [FromBody] CreateDoctorScheduleDto dto,
            [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("schedules.doctor.add")) return Forbid();
            if (_clinicContext.ClinicId == null && !_clinicContext.IsCompanyStaff)
                return Unauthorized(Msg(lang, "لا توجد عيادة مرتبطة بهذا المستخدم", "No clinic associated"));

            var doctor = await _db.Doctors.FindAsync(dto.DoctorId);
            if (doctor == null || doctor.IsDeleted)
                return NotFound(Msg(lang, "الطبيب غير موجود", "Doctor not found"));
            if (!_clinicContext.IsCompanyStaff && doctor.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            // تحقق أن اليوم غير مكرر
            var exists = await _db.DoctorSchedules
                .AnyAsync(s => s.DoctorId == dto.DoctorId && s.DayOfWeek == dto.DayOfWeek);
            if (exists)
                return BadRequest(Msg(lang,
                    "هذا اليوم موجود مسبقاً في جدول الطبيب",
                    "This day already exists in doctor's schedule"));

            // تحقق أن وقت البدء قبل الانتهاء
            if (dto.StartTime >= dto.EndTime)
                return BadRequest(Msg(lang,
                    "وقت البدء يجب أن يكون قبل وقت الانتهاء",
                    "Start time must be before end time"));

            // تحقق من جدول العيادة — تحذير فقط وليس منع
            var clinicSchedule = await _db.ClinicSchedules
                .FirstOrDefaultAsync(s => s.ClinicId == doctor.ClinicId
                    && s.DayOfWeek == dto.DayOfWeek && s.IsActive);

            var schedule = new DoctorSchedule
            {

                Id = Guid.NewGuid(),
                DoctorId = dto.DoctorId,
                DayOfWeek = dto.DayOfWeek,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                SlotDuration = dto.SlotDuration,
                FirstVisitPrice = dto.FirstVisitPrice,
                FollowUpPrice = dto.FollowUpPrice,
                IsActive = true,
            };
            _db.DoctorSchedules.Add(schedule);
            await _db.SaveChangesAsync();
            await _db.Entry(schedule).Reference(s => s.Doctor).LoadAsync();

            return Ok(new
            {
                schedule = ToDoctorResponse(schedule),
                warning = clinicSchedule == null
                    ? Msg(lang,
                        "تنبيه: العيادة لا تملك جدول دوام لهذا اليوم بعد — يُنصح بإضافته من تبويب دوام العيادة",
                        "Warning: Clinic has no schedule for this day yet — consider adding it from Clinic Hours tab")
                    : null,
            });
        }

        [HttpPut("doctor/{id}")]
        public async Task<ActionResult<DoctorScheduleResponseDto>> UpdateDoctorDay(
            Guid id, [FromBody] CreateDoctorScheduleDto dto,
            [FromQuery] string lang = "ar")
        {
            var schedule = await _db.DoctorSchedules
                .Include(s => s.Doctor)
                .FirstOrDefaultAsync(s => s.Id == id);
            if (schedule == null) return NotFound();
            if (!_clinicContext.IsCompanyStaff && schedule.Doctor.ClinicId != _clinicContext.ClinicId) return Forbid();

            if (dto.StartTime >= dto.EndTime)
                return BadRequest(Msg(lang,
                    "وقت البدء يجب أن يكون قبل وقت الانتهاء",
                    "Start time must be before end time"));

            schedule.StartTime = dto.StartTime;
            schedule.EndTime = dto.EndTime;
            schedule.SlotDuration = dto.SlotDuration;
            schedule.FirstVisitPrice = dto.FirstVisitPrice;
            schedule.FollowUpPrice = dto.FollowUpPrice;
            await _db.SaveChangesAsync();
            return Ok(ToDoctorResponse(schedule));
        }

        [HttpDelete("doctor/{id}")]
        public async Task<ActionResult> DeleteDoctorDay(Guid id, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("schedules.doctor.delete")) return Forbid();
            var schedule = await _db.DoctorSchedules
                .Include(s => s.Doctor)
                .FirstOrDefaultAsync(s => s.Id == id);
            if (schedule == null) return NotFound();
            if (!_clinicContext.IsCompanyStaff && schedule.Doctor.ClinicId != _clinicContext.ClinicId) return Forbid();

            // ✅ تحقق من وجود مواعيد مستقبلية بنفس يوم الأسبوع لهذا الطبيب
            var upcomingDates = await _db.Appointments
                .Where(a => a.DoctorId == schedule.DoctorId &&
                    !a.IsDeleted &&
                    a.Status != "cancelled" &&
                    a.AppointmentDate > DateTime.UtcNow)
                .Select(a => a.AppointmentDate)
                .ToListAsync();

            var hasUpcoming = upcomingDates.Any(d => d.DayOfWeek == schedule.DayOfWeek);

            if (hasUpcoming)
                return BadRequest(Msg(lang,
                    "لا يمكن حذف هذا اليوم لوجود مواعيد مستقبلية مرتبطة به",
                    "Cannot delete this day — future appointments are linked to it"));

            _db.DoctorSchedules.Remove(schedule);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ═══════ AVAILABLE SLOTS ═══════

        [HttpGet("available-slots")]
        public async Task<ActionResult> GetAvailableSlots(
            [FromQuery] Guid doctorId,
            [FromQuery] string date)
        {

            if (!DateOnly.TryParse(date, out var dateOnly))
                return BadRequest("تاريخ غير صحيح");

            var dateValue = dateOnly.ToDateTime(TimeOnly.MinValue);
            var dayOfWeek = dateValue.DayOfWeek;

            var doctor = await _db.Doctors.FindAsync(doctorId);
            if (doctor == null || doctor.IsDeleted) return NotFound("الطبيب غير موجود");
            if (!_clinicContext.IsCompanyStaff && doctor.ClinicId != _clinicContext.ClinicId) return Forbid();

            // ✅ تحقق من إجازة العيادة (يوم كامل)
            var clinicAbsent = await _db.Absences.AnyAsync(a =>
                a.ClinicId == doctor.ClinicId &&
                a.DoctorId == null &&
                a.StartDate.Date <= dateValue.Date &&
                a.EndDate.Date >= dateValue.Date &&
                a.StartTime == null);

            if (clinicAbsent)
                return Ok(new { available = false, reason = "🏥 العيادة في إجازة في هذا اليوم", slots = new List<object>() });

            // ✅ تحقق من إجازة الطبيب (يوم كامل)
            var doctorAbsent = await _db.Absences.AnyAsync(a =>
                a.ClinicId == doctor.ClinicId &&
                a.DoctorId == doctorId &&
                a.StartDate.Date <= dateValue.Date &&
                a.EndDate.Date >= dateValue.Date &&
                a.StartTime == null);

            if (doctorAbsent)
                return Ok(new { available = false, reason = "🌴 الطبيب في إجازة في هذا اليوم", slots = new List<object>() });

            var clinicSchedule = await _db.ClinicSchedules
                .FirstOrDefaultAsync(s => s.ClinicId == doctor.ClinicId
                    && s.DayOfWeek == dayOfWeek && s.IsActive);
            if (clinicSchedule == null)
                return Ok(new { available = false, reason = "العيادة مغلقة في هذا اليوم", slots = new List<object>() });

            var doctorSchedule = await _db.DoctorSchedules
                .FirstOrDefaultAsync(s => s.DoctorId == doctorId
                    && s.DayOfWeek == dayOfWeek && s.IsActive);
            if (doctorSchedule == null)
                return Ok(new { available = false, reason = "الطبيب لا يعمل في هذا اليوم", slots = new List<object>() });

            var startOfDay = dateValue.Date;
            var endOfDay = startOfDay.AddDays(1);
            var current = dateValue.Date.Add(doctorSchedule.StartTime.ToTimeSpan());
            var end = dateValue.Date.Add(doctorSchedule.EndTime.ToTimeSpan());

            var bookedSlots = await _db.Appointments
                .Where(a => a.DoctorId == doctorId && !a.IsDeleted
                    && a.AppointmentDate >= startOfDay && a.AppointmentDate < endOfDay
                    && a.Status != "cancelled")
                .Select(a => a.AppointmentDate)
                .ToListAsync();
            var bookedTimes = new HashSet<string>(bookedSlots.Select(b => b.ToString("HH:mm")));

            // ✅ جلب إجازات الفترة المحددة (اجتماع/استراحة) لهذا اليوم
            var partialAbsences = await _db.Absences
                .Where(a => a.ClinicId == doctor.ClinicId
                    && a.DoctorId == doctorId
                    && a.StartDate.Date <= dateValue.Date
                    && a.EndDate.Date >= dateValue.Date
                    && a.StartTime != null)
                .ToListAsync();

            // ✅ الوقت الحالي بتوقيت العيادة الفعلي، لا توقيت ثابت
            var nowLocal = await GetClinicNow(doctor.ClinicId);

            var slots = new List<SlotDto>();
            while (current.AddMinutes(doctorSchedule.SlotDuration) <= end)
            {
                var timeStr = current.ToString("HH:mm");
                var timeOnly = TimeOnly.FromDateTime(current);
                var isBooked = bookedTimes.Contains(timeStr);

                // ✅ تحقق إذا الوقت يقع ضمن إجازة جزئية
                var slotEndTime = timeOnly.AddMinutes(doctorSchedule.SlotDuration);

                var isAbsent = partialAbsences.Any(a =>
                    a.StartTime.HasValue &&
                    a.EndTime.HasValue &&
                    timeOnly < a.EndTime.Value &&
                    slotEndTime > a.StartTime.Value);
                var isPast =
    dateOnly < DateOnly.FromDateTime(nowLocal) ||
    (dateOnly == DateOnly.FromDateTime(nowLocal) &&
     timeOnly <= TimeOnly.FromDateTime(nowLocal));

                var isAvailable =
                    !isBooked &&
                    !isAbsent &&
                    !isPast;
                slots.Add(new SlotDto
                {
                    Time = timeStr,
                    DateTime = current.ToString("yyyy-MM-ddTHH:mm:ss"),
                    IsBooked = isBooked,
                    IsAbsent = isAbsent,
                    IsAvailable = isAvailable,
                });
                current = current.AddMinutes(doctorSchedule.SlotDuration);
            }


            return Ok(new
            {
                available = true,
                date = dateOnly.ToString("yyyy-MM-dd"),
                doctorName = doctor.FullName,
                workStart = doctorSchedule.StartTime.ToString("HH:mm"),
                workEnd = doctorSchedule.EndTime.ToString("HH:mm"),
                slotDuration = doctorSchedule.SlotDuration,
                firstVisitPrice = doctorSchedule.FirstVisitPrice,
                followUpPrice = doctorSchedule.FollowUpPrice,
                totalSlots = slots.Count,
                availableSlots = slots.Count(s => s.IsAvailable),
                slots,
            });
        }


        
        // GET: api/schedules/doctor/{doctorId}/calendar?from=2026-08-28&to=2026-09-03
        [HttpGet("doctor/{doctorId}/calendar")]
        public async Task<ActionResult<DoctorCalendarDto>> GetDoctorCalendar(
    Guid doctorId,
    [FromQuery] DateTime from,
    [FromQuery] DateTime to)
        {
            // =========================================================
            // 1. التحقق من العيادة
            // =========================================================

            if (_clinicContext.ClinicId == null)
                return Unauthorized("لا توجد عيادة مرتبطة بالمستخدم");

            if (to < from)
                return BadRequest("تاريخ النهاية يجب أن يكون بعد أو يساوي تاريخ البداية");

            // حماية من طلب فترة ضخمة
            if ((to.Date - from.Date).TotalDays > 366)
                return BadRequest("الفترة القصوى هي سنة واحدة");

            var clinicId = _clinicContext.ClinicId.Value;

            var fromDate = from.Date;
            var toDate = to.Date;

            // =========================================================
            // 2. التحقق من الطبيب
            // =========================================================

            var doctor = await _db.Doctors
                .AsNoTracking()
                .FirstOrDefaultAsync(d =>
                    d.Id == doctorId &&
                    d.ClinicId == clinicId &&
                    d.IsActive &&
                    !d.IsDeleted);

            if (doctor == null)
                return NotFound("الطبيب غير موجود");

            // الطبيب نفسه يستطيع رؤية تقويمه فقط
            if (_clinicContext.Role == "Doctor")
            {
                var userEmail = await _db.Users
                    .Where(u => u.Id == _clinicContext.UserId)
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync();

                var doctorRecord = await _db.Doctors
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d =>
                        d.Email == userEmail &&
                        d.ClinicId == clinicId &&
                        !d.IsDeleted);

                if (doctorRecord == null || doctorRecord.Id != doctorId)
                    return Forbid();
            }

            // =========================================================
            // 3. جدول دوام العيادة
            // =========================================================

            var clinicSchedules = await _db.ClinicSchedules
                .AsNoTracking()
                .Where(s =>
                    s.ClinicId == clinicId &&
                    s.IsActive)
                .ToListAsync();

            // =========================================================
            // 4. جدول دوام الطبيب
            // =========================================================

            var doctorSchedules = await _db.DoctorSchedules
                .AsNoTracking()
                .Where(s =>
                    s.DoctorId == doctorId &&
                    s.IsActive)
                .ToListAsync();

            // =========================================================
            // 5. المواعيد
            // =========================================================

            var appointments = await _db.Appointments
                .AsNoTracking()
                .Where(a =>
                    a.ClinicId == clinicId &&
                    a.DoctorId == doctorId &&
                    !a.IsDeleted &&
                    a.Status != "cancelled" &&
                    a.AppointmentDate >= fromDate &&
                    a.AppointmentDate < toDate.AddDays(1))
                .Include(a => a.Patient)
                .OrderBy(a => a.AppointmentDate)
                .ToListAsync();

            // =========================================================
            // 6. الإجازات
            // =========================================================

            var absences = await _db.Absences
                .AsNoTracking()
                .Where(a =>
                    a.ClinicId == clinicId &&
                    !a.IsDeleted &&
                    (a.DoctorId == null || a.DoctorId == doctorId) &&
                    a.StartDate.Date <= toDate &&
                    a.EndDate.Date >= fromDate)
                .ToListAsync();

            // =========================================================
            // 7. الوقت المحلي للعيادة
            // =========================================================

            var nowLocal = await GetClinicNow(clinicId);

            // =========================================================
            // 8. النتيجة
            // =========================================================

            var result = new DoctorCalendarDto
            {
                DoctorId = doctor.Id,
                DoctorName = doctor.FullName,
                From = fromDate,
                To = toDate
            };

            // =========================================================
            // 9. بناء الأيام
            // =========================================================

            for (var date = fromDate; date <= toDate; date = date.AddDays(1))
            {
                var dayOfWeek = date.DayOfWeek;

                // -----------------------------------------------------
                // دوام الطبيب
                // -----------------------------------------------------

                var doctorSchedule = doctorSchedules
                    .FirstOrDefault(s => s.DayOfWeek == dayOfWeek);

                // -----------------------------------------------------
                // دوام العيادة
                // -----------------------------------------------------

                var clinicSchedule = clinicSchedules
                    .FirstOrDefault(s => s.DayOfWeek == dayOfWeek);

                var day = new DoctorCalendarDayDto
                {
                    Date = date,
                    DayOfWeek = (int)dayOfWeek,
                    IsWorkingDay = doctorSchedule != null && clinicSchedule != null
                };

                // -----------------------------------------------------
                // إجازات هذا اليوم
                // -----------------------------------------------------

                var dayAbsences = absences
                    .Where(a =>
                        a.StartDate.Date <= date &&
                        a.EndDate.Date >= date)
                    .ToList();

                // -----------------------------------------------------
                // لا يوجد دوام للطبيب
                // -----------------------------------------------------

                if (doctorSchedule == null)
                {
                    day.IsAbsent = dayAbsences.Any();

                    result.Days.Add(day);
                    continue;
                }

                // -----------------------------------------------------
                // العيادة مغلقة في هذا اليوم
                // -----------------------------------------------------

                if (clinicSchedule == null)
                {
                    day.IsWorkingDay = false;
                    day.IsAbsent = true;

                    result.Days.Add(day);
                    continue;
                }

                // -----------------------------------------------------
                // إجازة يوم كامل
                // -----------------------------------------------------

                var fullDayAbsence = dayAbsences.Any(a =>
                    a.StartTime == null ||
                    a.EndTime == null);

                if (fullDayAbsence)
                {
                    day.IsAbsent = true;

                    result.Days.Add(day);
                    continue;
                }

                // -----------------------------------------------------
                // تحديد وقت العمل الحقيقي
                //
                // الطبيب لا يستطيع العمل خارج دوام العيادة
                // -----------------------------------------------------

                var startTime = doctorSchedule.StartTime > clinicSchedule.OpenTime
                    ? doctorSchedule.StartTime
                    : clinicSchedule.OpenTime;

                var endTime = doctorSchedule.EndTime < clinicSchedule.CloseTime
                    ? doctorSchedule.EndTime
                    : clinicSchedule.CloseTime;

                // -----------------------------------------------------
                // إذا لا يوجد تقاطع بين دوام الطبيب والعيادة
                // -----------------------------------------------------

                if (startTime >= endTime)
                {
                    day.IsWorkingDay = false;

                    result.Days.Add(day);
                    continue;
                }

                // =====================================================
                // بناء Slots
                // =====================================================

                var slotDuration = doctorSchedule.SlotDuration;

                if (slotDuration <= 0)
                    slotDuration = 15;

                var current = startTime;

                while (current < endTime)
                {
                    var slotStart = current;
                    var slotEnd = current.AddMinutes(slotDuration);

                    // لا ننشئ Slot يتجاوز نهاية دوام الطبيب أو العيادة
                    if (slotEnd > endTime)
                        break;

                    // -------------------------------------------------
                    // DateTime للـ Slot
                    // -------------------------------------------------

                    var slotDateTime = date.Add(slotStart.ToTimeSpan());

                    var slotEndDateTime = date.Add(slotEnd.ToTimeSpan());

                    // -------------------------------------------------
                    // البحث عن موعد محجوز
                    //
                    // نستخدم فترة زمنية وليس مساواة مباشرة
                    // حتى لو كان AppointmentDate يحتوي ثواني
                    // -------------------------------------------------

                    var appointment = appointments.FirstOrDefault(a =>
                        a.AppointmentDate < slotEndDateTime &&
                        a.AppointmentDate.AddMinutes(slotDuration) > slotDateTime);

                    // -------------------------------------------------
                    // فحص الإجازات الجزئية
                    // -------------------------------------------------

                    var isAbsent = false;

                    foreach (var absence in dayAbsences)
                    {
                        // إجازة يوم كامل
                        if (absence.StartTime == null ||
                            absence.EndTime == null)
                        {
                            isAbsent = true;
                            break;
                        }

                        var absenceStart = absence.StartTime.Value;
                        var absenceEnd = absence.EndTime.Value;

                        // يوجد تداخل بين الـ Slot والإجازة
                        if (slotStart < absenceEnd &&
                            slotEnd > absenceStart)
                        {
                            isAbsent = true;
                            break;
                        }
                    }

                    // -------------------------------------------------
                    // هل الموعد في الماضي؟
                    // -------------------------------------------------

                    var isPast = false;

                    if (date.Date < nowLocal.Date)
                    {
                        isPast = true;
                    }
                    else if (date.Date == nowLocal.Date)
                    {
                        isPast = slotDateTime <= nowLocal;
                    }

                    // -------------------------------------------------
                    // إنشاء Slot
                    // -------------------------------------------------

                    var slot = new DoctorCalendarSlotDto
                    {
                        Start = slotStart.ToString("HH:mm"),
                        End = slotEnd.ToString("HH:mm")
                    };

                    // -------------------------------------------------
                    // تحديد الحالة
                    // -------------------------------------------------

                    if (isAbsent)
                    {
                        slot.Status = "absent";
                    }
                    else if (appointment != null)
                    {
                        slot.Status = "booked";
                        slot.AppointmentId = appointment.Id;
                        slot.PatientName = appointment.Patient?.FullName;
                        slot.AppointmentStatus = appointment.Status;
                    }
                    else if (isPast)
                    {
                        // الوقت انتهى ولا يمكن حجزه
                        slot.Status = "past";
                    }
                    else
                    {
                        slot.Status = "available";
                    }

                    day.Slots.Add(slot);

                    current = slotEnd;
                }

                result.Days.Add(day);
            }

            return Ok(result);
        }
        // ═══════ HELPERS ═══════
        // ✅ GET: api/schedules/doctor/{doctorId}/calendar/export?format=pdf|excel&from=&to=
        [HttpGet("doctor/{doctorId}/calendar/export")]
        public async Task<ActionResult> ExportDoctorCalendar(
            Guid doctorId,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to,
            [FromQuery] string format = "pdf",
            [FromQuery] string lang = "ar")
        {
            var calendarResult = await GetDoctorCalendar(doctorId, from, to);
            if (calendarResult.Result is not OkObjectResult ok || ok.Value is not DoctorCalendarDto cal)
                return calendarResult.Result ?? NotFound();

            var isRtl = lang == "ar";

            // أعمدة: الوقت + يوم لكل تاريخ
            var columns = new List<string> { isRtl ? "الوقت" : "Time" };
            foreach (var d in cal.Days)
                columns.Add($"{(isRtl ? GetDayName(d.Date.DayOfWeek) : d.Date.DayOfWeek.ToString())} {d.Date:dd/MM}");

            // صفوف: كل وقت بدء موجود بالمدى
            var times = cal.Days.SelectMany(d => d.Slots.Select(s => s.Start)).Distinct().OrderBy(x => x).ToList();

            var rows = times.Select(time =>
            {
                var row = new List<string> { time };
                foreach (var d in cal.Days)
                {
                    var slot = d.Slots.FirstOrDefault(s => s.Start == time);
                    row.Add(slot == null ? "—" : slot.Status switch
                    {
                        "booked" => slot.PatientName ?? (isRtl ? "محجوز" : "Booked"),
                        "absent" => isRtl ? "إجازة" : "Off",
                        "past" => "—",
                        _ => isRtl ? "متاح" : "Available",
                    });
                }
                return row;
            }).ToList();

            var summary = new List<(string, string)>
    {
        (isRtl ? "الفترة" : "Period", $"{cal.From:yyyy-MM-dd} — {cal.To:yyyy-MM-dd}"),
        (isRtl ? "المواعيد المحجوزة" : "Booked Slots",
            cal.Days.Sum(d => d.Slots.Count(s => s.Status == "booked")).ToString()),
    };

            var doctor = await _db.Doctors.FindAsync(doctorId);
            var clinic = await _db.Clinics.FindAsync(doctor!.ClinicId);

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "تقويم الطبيب" : "Doctor Calendar",
                    Title = isRtl ? $"تقويم — {cal.DoctorName}" : $"Calendar — {cal.DoctorName}",
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "doctor-calendar.xlsx");
            }

            var pdf = _pdfExport.GenerateTableReport(new PdfReportRequest
            {
                Title = isRtl ? "تقويم الطبيب" : "Doctor Calendar",
                Subtitle = cal.DoctorName,
                ClinicName = clinic?.Name ?? "",
                LogoPath = ResolveLogoPath(clinic?.Logo),
                IsRtl = isRtl,
                Columns = columns,
                Rows = rows,
                SummaryLines = summary,
            });
            return File(pdf, "application/pdf", "doctor-calendar.pdf");
        }
        private static string GetDayName(DayOfWeek day) => day switch
        {
            DayOfWeek.Sunday => "الأحد",
            DayOfWeek.Monday => "الاثنين",
            DayOfWeek.Tuesday => "الثلاثاء",
            DayOfWeek.Wednesday => "الأربعاء",
            DayOfWeek.Thursday => "الخميس",
            DayOfWeek.Friday => "الجمعة",
            DayOfWeek.Saturday => "السبت",
            _ => ""
        };

        private static ClinicScheduleResponseDto ToClinicResponse(ClinicSchedule s) => new()
        {
            Id = s.Id,
            DayOfWeek = s.DayOfWeek,
            DayName = GetDayName(s.DayOfWeek),
            OpenTime = s.OpenTime,
            CloseTime = s.CloseTime,
            IsActive = s.IsActive,
        };

        private static DoctorScheduleResponseDto ToDoctorResponse(DoctorSchedule s) => new()
        {
            Id = s.Id,
            DoctorId = s.DoctorId,
            DoctorName = s.Doctor?.FullName ?? "",
            DayOfWeek = s.DayOfWeek,
            DayName = GetDayName(s.DayOfWeek),
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            SlotDuration = s.SlotDuration,
            FirstVisitPrice = s.FirstVisitPrice,
            FollowUpPrice = s.FollowUpPrice,
            IsActive = s.IsActive,
        };
    }

    // ✅ بديل واضح ونوعي بدل الـ anonymous object + Reflection
    public class SlotDto
    {

        public string Time { get; set; } = default!;
        public string DateTime { get; set; } = default!;
        public bool IsBooked { get; set; }
        public bool IsAbsent { get; set; }
        public bool IsAvailable { get; set; }
    }
}