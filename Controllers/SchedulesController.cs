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

        public SchedulesController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
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

        [HttpPost("clinic")]
        public async Task<ActionResult<ClinicScheduleResponseDto>> AddClinicDay(
            [FromBody] CreateClinicScheduleDto dto,
            [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("schedules.manage")) return Forbid();
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
            if (!_clinicContext.HasPermission("schedules.manage")) return Forbid();
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

        [HttpPost("doctor")]
        public async Task<ActionResult> AddDoctorDay(
            [FromBody] CreateDoctorScheduleDto dto,
            [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("schedules.manage")) return Forbid();
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
            if (!_clinicContext.HasPermission("schedules.manage")) return Forbid();
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
                var isAbsent = partialAbsences.Any(a => a.StartTime <= timeOnly && a.EndTime >= timeOnly);

                slots.Add(new SlotDto
                {
                    Time = timeStr,
                    DateTime = current.ToString("yyyy-MM-ddTHH:mm:ss"),
                    IsBooked = isBooked,
                    IsAbsent = isAbsent,
                    IsAvailable = !isBooked && !isAbsent && current > nowLocal,
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

        // ═══════ HELPERS ═══════

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