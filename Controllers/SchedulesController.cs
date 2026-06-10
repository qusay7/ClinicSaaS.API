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

        // ═══════════════════════════════════════
        // جدول العيادة
        // ═══════════════════════════════════════

        // GET: api/schedules/clinic
        // جلب جدول دوام العيادة الحالية
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

            return Ok(schedules.Select(s => ToClinicResponse(s)).ToList());
        }

        // POST: api/schedules/clinic
        // إضافة يوم عمل للعيادة
        [HttpPost("clinic")]
        public async Task<ActionResult<ClinicScheduleResponseDto>> AddClinicDay(
            [FromBody] CreateClinicScheduleDto dto)
        {
            if (!_clinicContext.HasPermission("schedules.manage"))
                return Forbid();

            if (_clinicContext.ClinicId == null)
                return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

            var clinicId = _clinicContext.ClinicId.Value;

            // تحقق أن اليوم غير مكرر
            var exists = await _db.ClinicSchedules
                .AnyAsync(s => s.ClinicId == clinicId && s.DayOfWeek == dto.DayOfWeek);

            if (exists)
                return BadRequest("هذا اليوم موجود مسبقاً في جدول العيادة");

            // تحقق أن وقت الفتح قبل الإغلاق
            if (dto.OpenTime >= dto.CloseTime)
                return BadRequest("وقت الفتح يجب أن يكون قبل وقت الإغلاق");

            var schedule = new ClinicSchedule
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                DayOfWeek = dto.DayOfWeek,
                OpenTime = dto.OpenTime,
                CloseTime = dto.CloseTime,
                IsActive = true
            };

            _db.ClinicSchedules.Add(schedule);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetClinicSchedule), ToClinicResponse(schedule));
        }

        // PUT: api/schedules/clinic/{id}
        // تعديل يوم عمل العيادة
        [HttpPut("clinic/{id}")]
        public async Task<ActionResult<ClinicScheduleResponseDto>> UpdateClinicDay(
            Guid id, [FromBody] CreateClinicScheduleDto dto)
        {
            var schedule = await _db.ClinicSchedules.FindAsync(id);

            if (schedule == null)
                return NotFound();

            if (schedule.ClinicId != _clinicContext.ClinicId && !_clinicContext.IsCompanyStaff)
                return Forbid();

            if (dto.OpenTime >= dto.CloseTime)
                return BadRequest("وقت الفتح يجب أن يكون قبل وقت الإغلاق");

            schedule.OpenTime = dto.OpenTime;
            schedule.CloseTime = dto.CloseTime;

            await _db.SaveChangesAsync();
            return Ok(ToClinicResponse(schedule));
        }

        // DELETE: api/schedules/clinic/{id}
        // حذف يوم عمل من جدول العيادة
        [HttpDelete("clinic/{id}")]
        public async Task<ActionResult> DeleteClinicDay(Guid id)
        {
            if (!_clinicContext.HasPermission("schedules.manage"))
                return Forbid();

            var schedule = await _db.ClinicSchedules.FindAsync(id);

            if (schedule == null)
                return NotFound();

            if (schedule.ClinicId != _clinicContext.ClinicId && !_clinicContext.IsCompanyStaff)
                return Forbid();

            _db.ClinicSchedules.Remove(schedule);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ═══════════════════════════════════════
        // جدول الطبيب
        // ═══════════════════════════════════════

        // GET: api/schedules/doctor/{doctorId}
        // جلب جدول دوام طبيب معين
        // GET: api/schedules/doctor/{doctorId}
        [HttpGet("doctor/{doctorId}")]
        public async Task<ActionResult<IEnumerable<DoctorScheduleResponseDto>>> GetDoctorSchedule(Guid doctorId)
        {
            var doctor = await _db.Doctors.FindAsync(doctorId);
            if (doctor == null || doctor.isdeleted)
                return NotFound("الطبيب غير موجود");

            if (!_clinicContext.IsCompanyStaff && doctor.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            // ✅ الطبيب يرى جدوله فقط
            if (_clinicContext.Role == "Doctor")
            {
                var userEmail = await _db.Users
                    .Where(u => u.Id == _clinicContext.UserId)
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync();

                var doctorRecord = await _db.Doctors
                    .FirstOrDefaultAsync(d => d.Email == userEmail
                        && d.ClinicId == _clinicContext.ClinicId
                        && !d.isdeleted);

                if (doctorRecord == null || doctorRecord.Id != doctorId)
                    return Forbid();
            }

            var schedules = await _db.DoctorSchedules
                .Include(s => s.Doctor)
                .Where(s => s.DoctorId == doctorId)
                .OrderBy(s => s.DayOfWeek)
                .ToListAsync();

            return Ok(schedules.Select(s => ToDoctorResponse(s)).ToList());
        }

        // POST: api/schedules/doctor
        // إضافة يوم عمل لطبيب
        [HttpPost("doctor")]
        public async Task<ActionResult<DoctorScheduleResponseDto>> AddDoctorDay(
            [FromBody] CreateDoctorScheduleDto dto)
        {
          

            if (!_clinicContext.HasPermission("schedules.manage"))
                return Forbid();

            if (_clinicContext.ClinicId == null && !_clinicContext.IsCompanyStaff)
                return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

            // تحقق أن الطبيب موجود
            var doctor = await _db.Doctors.FindAsync(dto.DoctorId);
            if (doctor == null || doctor.isdeleted)
                return NotFound("الطبيب غير موجود");

            if (!_clinicContext.IsCompanyStaff && doctor.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            // تحقق أن اليوم غير مكرر لنفس الطبيب
            var exists = await _db.DoctorSchedules
                .AnyAsync(s => s.DoctorId == dto.DoctorId && s.DayOfWeek == dto.DayOfWeek);

            if (exists)
                return BadRequest("هذا اليوم موجود مسبقاً في جدول الطبيب");

            // تحقق أن وقت البدء قبل الانتهاء
            if (dto.StartTime >= dto.EndTime)
                return BadRequest("وقت البدء يجب أن يكون قبل وقت الانتهاء");

            // تحقق أن الجدول ضمن دوام العيادة
            var clinicId = doctor.ClinicId;
            var clinicSchedule = await _db.ClinicSchedules
                .FirstOrDefaultAsync(s => s.ClinicId == clinicId
                    && s.DayOfWeek == dto.DayOfWeek
                    && s.IsActive);

            if (clinicSchedule == null)
                return BadRequest("العيادة مغلقة في هذا اليوم");

            if (dto.StartTime < clinicSchedule.OpenTime || dto.EndTime > clinicSchedule.CloseTime)
                return BadRequest($"وقت الطبيب يجب أن يكون ضمن دوام العيادة ({clinicSchedule.OpenTime} - {clinicSchedule.CloseTime})");

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
                IsActive = true
            };

            _db.DoctorSchedules.Add(schedule);
            await _db.SaveChangesAsync();

            await _db.Entry(schedule).Reference(s => s.Doctor).LoadAsync();
            return Ok(ToDoctorResponse(schedule));
        }

        // PUT: api/schedules/doctor/{id}
        [HttpPut("doctor/{id}")]
        public async Task<ActionResult<DoctorScheduleResponseDto>> UpdateDoctorDay(
            Guid id, [FromBody] CreateDoctorScheduleDto dto)
        {
            var schedule = await _db.DoctorSchedules
                .Include(s => s.Doctor)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (schedule == null)
                return NotFound();

            if (!_clinicContext.IsCompanyStaff && schedule.Doctor.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            if (dto.StartTime >= dto.EndTime)
                return BadRequest("وقت البدء يجب أن يكون قبل وقت الانتهاء");

            schedule.StartTime = dto.StartTime;
            schedule.EndTime = dto.EndTime;
            schedule.SlotDuration = dto.SlotDuration;
            schedule.FirstVisitPrice = dto.FirstVisitPrice;
            schedule.FollowUpPrice = dto.FollowUpPrice;

            await _db.SaveChangesAsync();
            return Ok(ToDoctorResponse(schedule));
        }

        // DELETE: api/schedules/doctor/{id}
        [HttpDelete("doctor/{id}")]
        public async Task<ActionResult> DeleteDoctorDay(Guid id)
        {
            if (!_clinicContext.HasPermission("schedules.manage"))
                return Forbid();

            var schedule = await _db.DoctorSchedules
                .Include(s => s.Doctor)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (schedule == null)
                return NotFound();

            if (!_clinicContext.IsCompanyStaff && schedule.Doctor.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            _db.DoctorSchedules.Remove(schedule);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ═══════════════════════════════════════
        // المواعيد المتاحة
        // ═══════════════════════════════════════

        // GET: api/schedules/available-slots?doctorId=...&date=2026-06-08
        [HttpGet("available-slots")]
        public async Task<ActionResult> GetAvailableSlots(
            [FromQuery] Guid doctorId,
            [FromQuery] DateTime date)
        {
            var doctor = await _db.Doctors.FindAsync(doctorId);
            if (doctor == null || doctor.isdeleted)
                return NotFound("الطبيب غير موجود");

            if (!_clinicContext.IsCompanyStaff && doctor.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            var dayOfWeek = date.DayOfWeek;

            // 1 — تحقق أن العيادة مفتوحة
            var clinicSchedule = await _db.ClinicSchedules
                .FirstOrDefaultAsync(s => s.ClinicId == doctor.ClinicId
                    && s.DayOfWeek == dayOfWeek
                    && s.IsActive);

            if (clinicSchedule == null)
                return Ok(new { available = false, reason = "العيادة مغلقة في هذا اليوم", slots = new List<object>() });

            // 2 — تحقق أن الطبيب يعمل
            var doctorSchedule = await _db.DoctorSchedules
                .FirstOrDefaultAsync(s => s.DoctorId == doctorId
                    && s.DayOfWeek == dayOfWeek
                    && s.IsActive);

            if (doctorSchedule == null)
                return Ok(new { available = false, reason = "الطبيب لا يعمل في هذا اليوم", slots = new List<object>() });

            // 3 — جلب المواعيد المحجوزة في هذا اليوم
            var startOfDay = date.Date;
            var endOfDay = startOfDay.AddDays(1);

            var bookedSlots = await _db.Appointments
                .Where(a => a.DoctorId == doctorId
                    && !a.isdeleted
                    && a.AppointmentDate >= startOfDay
                    && a.AppointmentDate < endOfDay
                    && a.Status != "cancelled")
                .Select(a => a.AppointmentDate)
                .ToListAsync();

            // 4 — توليد المواعيد المتاحة
            var slots = new List<object>();
            var current = date.Date
                .Add(doctorSchedule.StartTime.ToTimeSpan());
            var end = date.Date
                .Add(doctorSchedule.EndTime.ToTimeSpan());

            while (current.AddMinutes(doctorSchedule.SlotDuration) <= end)
            {
                var isBooked = bookedSlots.Any(b =>
                    b >= current &&
                    b < current.AddMinutes(doctorSchedule.SlotDuration));

                slots.Add(new
                {
                    time = current.ToString("HH:mm"),
                    dateTime = current,
                    isBooked = isBooked,
                    isAvailable = !isBooked && current > DateTime.Now
                });

                current = current.AddMinutes(doctorSchedule.SlotDuration);
            }

            return Ok(new
            {
                available = true,
                date = date.ToString("yyyy-MM-dd"),
                doctorName = doctor.FullName,
                workStart = doctorSchedule.StartTime.ToString("HH:mm"),
                workEnd = doctorSchedule.EndTime.ToString("HH:mm"),
                slotDuration = doctorSchedule.SlotDuration,
                firstVisitPrice = doctorSchedule.FirstVisitPrice,
                followUpPrice = doctorSchedule.FollowUpPrice,
                totalSlots = slots.Count,
                availableSlots = slots.Count(s => (bool)s.GetType().GetProperty("isAvailable")!.GetValue(s)!),
                slots = slots
            });
        }

        // ═══════════════════════════════════════
        // دوال مساعدة
        // ═══════════════════════════════════════

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

        private static ClinicScheduleResponseDto ToClinicResponse(ClinicSchedule s) =>
            new ClinicScheduleResponseDto
            {
                Id = s.Id,
                DayOfWeek = s.DayOfWeek,
                DayName = GetDayName(s.DayOfWeek),
                OpenTime = s.OpenTime,
                CloseTime = s.CloseTime,
                IsActive = s.IsActive
            };

        private static DoctorScheduleResponseDto ToDoctorResponse(DoctorSchedule s) =>
            new DoctorScheduleResponseDto
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
                IsActive = s.IsActive
            };
    }
}