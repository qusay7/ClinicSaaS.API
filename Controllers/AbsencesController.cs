using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AbsencesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public AbsencesController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        private static string Msg(string lang, string ar, string en)
            => lang == "ar" ? ar : en;

        // GET: api/absences
        [HttpGet]
        public async Task<ActionResult> GetAll([FromQuery] string? doctorId)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var query = _db.Absences
                .Include(a => a.Doctor)
                .Where(a => a.ClinicId == _clinicContext.ClinicId);

            if (!string.IsNullOrEmpty(doctorId) && Guid.TryParse(doctorId, out var docGuid))
                query = query.Where(a => a.DoctorId == docGuid);

            var absences = await query
                .OrderByDescending(a => a.StartDate)
                .ToListAsync();

            return Ok(absences.Select(a => new
            {
                a.Id,
                a.DoctorId,
                doctorName = a.Doctor?.FullName,
                startDate = a.StartDate.ToString("yyyy-MM-dd"),
                endDate = a.EndDate.ToString("yyyy-MM-dd"),
                startTime = a.StartTime?.ToString("HH:mm"),
                endTime = a.EndTime?.ToString("HH:mm"),
                isFullDay = a.StartTime == null,
                a.Type,
                a.Notes,
                a.CreatedAt,
            }));
        }

        // POST: api/absences
        [HttpPost]
        public async Task<ActionResult> Create([FromBody] CreateAbsenceDto dto,
            [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("schedules.manage")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            // تحقق من الطبيب إذا كان محدداً
            if (dto.DoctorId.HasValue)
            {
                var doctor = await _db.Doctors.FindAsync(dto.DoctorId.Value);
                if (doctor == null || doctor.IsDeleted )
                    return BadRequest(Msg(lang, "الطبيب غير موجود", "Doctor not found"));
                if (doctor.ClinicId != _clinicContext.ClinicId)
                    return Forbid();
            }

            if (dto.EndDate < dto.StartDate)
                return BadRequest(Msg(lang,
                    "تاريخ النهاية يجب أن يكون بعد تاريخ البداية",
                    "End date must be after start date"));

            // إذا فترة محددة — تحقق من الأوقات
            if (!dto.IsFullDay)
            {
                if (dto.StartTime == null || dto.EndTime == null)
                    return BadRequest(Msg(lang,
                        "يرجى تحديد وقت البداية والنهاية",
                        "Please specify start and end times"));

                if (dto.EndTime <= dto.StartTime)
                    return BadRequest(Msg(lang,
                        "وقت النهاية يجب أن يكون بعد وقت البداية",
                        "End time must be after start time"));
            }

            var absence = new Absence
            {
                Id = Guid.NewGuid(),
                ClinicId = _clinicContext.ClinicId.Value,
                DoctorId = dto.DoctorId,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                StartTime = dto.IsFullDay ? null : dto.StartTime,
                EndTime = dto.IsFullDay ? null : dto.EndTime,
                Type = dto.Type ?? "holiday",
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow,
            };

            _db.Absences.Add(absence);
            await _db.SaveChangesAsync();

            if (absence.DoctorId.HasValue)
                await _db.Entry(absence).Reference(a => a.Doctor).LoadAsync();

            return Ok(new
            {
                absence.Id,
                message = Msg(lang, "تم إضافة الإجازة بنجاح", "Absence added successfully"),
            });
        }

        // DELETE: api/absences/{id}
        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(Guid id, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("schedules.manage")) return Forbid();

            var absence = await _db.Absences.FindAsync(id);
            if (absence == null) return NotFound();
            if (absence.ClinicId != _clinicContext.ClinicId) return Forbid();

            absence.IsDeleted = true;   // ✅ حذف منطقي بدل الحذف الفعلي
            await _db.SaveChangesAsync();

            return Ok(new { message = Msg(lang, "تم حذف الإجازة", "Absence deleted") });
        }

        // GET: api/absences/check?doctorId=...&date=2026-07-01&time=09:00&lang=ar
        // للتحقق عند الحجز
        [HttpGet("check")]
        public async Task<ActionResult> Check(
            [FromQuery] Guid? doctorId,
            [FromQuery] string date,
            [FromQuery] string? time,
            [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            if (!DateOnly.TryParse(date, out var dateOnly))
                return BadRequest(Msg(lang, "تاريخ غير صحيح", "Invalid date"));

            var dateValue = dateOnly.ToDateTime(TimeOnly.MinValue);

            // تحقق إجازة العيادة
            var clinicAbsent = await _db.Absences.AnyAsync(a =>
                a.ClinicId == _clinicContext.ClinicId &&
                a.DoctorId == null &&
                a.StartDate.Date <= dateValue.Date &&
                a.EndDate.Date >= dateValue.Date &&
                a.StartTime == null);

            if (clinicAbsent)
                return Ok(new
                {
                    available = false,
                    reason = "clinic",
                    message = Msg(lang, "العيادة في إجازة في هذا اليوم", "Clinic is on holiday on this day"),
                });

            // تحقق إجازة الطبيب
            if (doctorId.HasValue)
            {
                var doctorAbsent = await _db.Absences.AnyAsync(a =>
                    a.ClinicId == _clinicContext.ClinicId &&
                    a.DoctorId == doctorId &&
                    a.StartDate.Date <= dateValue.Date &&
                    a.EndDate.Date >= dateValue.Date &&
                    a.StartTime == null);

                if (doctorAbsent)
                    return Ok(new
                    {
                        available = false,
                        reason = "doctor",
                        message = Msg(lang, "الطبيب في إجازة في هذا اليوم", "Doctor is on leave on this day"),
                    });

                // تحقق إذا كان وقت محدد
                if (!string.IsNullOrEmpty(time) && TimeOnly.TryParse(time, out var timeOnly))
                {
                    var partialAbsent = await _db.Absences.AnyAsync(a =>
                        a.ClinicId == _clinicContext.ClinicId &&
                        a.DoctorId == doctorId &&
                        a.StartDate.Date <= dateValue.Date &&
                        a.EndDate.Date >= dateValue.Date &&
                        a.StartTime != null &&
                        a.StartTime <= timeOnly &&
                        a.EndTime >= timeOnly);

                    if (partialAbsent)
                        return Ok(new
                        {
                            available = false,
                            reason = "doctor_busy",
                            message = Msg(lang, "الطبيب غير متاح في هذا الوقت", "Doctor is unavailable at this time"),
                        });
                }
            }

            return Ok(new { available = true });
        }
    }

    public class CreateAbsenceDto
    {
        public Guid? DoctorId { get; set; }        // null = إجازة العيادة
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsFullDay { get; set; } = true;
        public TimeOnly? StartTime { get; set; }   // للفترة المحددة
        public TimeOnly? EndTime { get; set; }
        public string? Type { get; set; }          // holiday/vacation/meeting/break/other
        public string? Notes { get; set; }
    }
}