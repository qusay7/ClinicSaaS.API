using ClinicSaaS.API.Data;
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
    public class AbsencesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly IPdfExportService _pdfExport;
        private readonly IExcelExportService _excelExport;
        private readonly IWebHostEnvironment _env;

        public AbsencesController(ApplicationDbContext db, IClinicContext clinicContext,
            IPdfExportService pdfExport, IExcelExportService excelExport, IWebHostEnvironment env)
        {
            _db = db;
            _clinicContext = clinicContext;
            _pdfExport = pdfExport;
            _excelExport = excelExport;
            _env = env;
        }

        private static string Msg(string lang, string ar, string en)
            => lang == "ar" ? ar : en;

        private string? ResolveLogoPath(string? logoUrl)
        {
            if (string.IsNullOrEmpty(logoUrl)) return null;
            var cleanPath = logoUrl.Split('?')[0].TrimStart('/');
            var fullPath = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), cleanPath.Replace("logos/", "logos" + Path.DirectorySeparatorChar));
            return System.IO.File.Exists(fullPath) ? fullPath : null;
        }

        private static string GetDayTypeLabel(string type, bool isRtl)
        {
            return type switch
            {
                "holiday" => isRtl ? "عطلة" : "Holiday",
                "vacation" => isRtl ? "إجازة" : "Vacation",
                "meeting" => isRtl ? "اجتماع" : "Meeting",
                "break" => isRtl ? "استراحة" : "Break",
                _ => isRtl ? "أخرى" : "Other",
            };
        }

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

        // ✅ GET: api/absences/export?format=pdf|excel&doctorId=
        [HttpGet("export")]
        public async Task<ActionResult> Export([FromQuery] string? doctorId, [FromQuery] string format = "pdf", [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var isRtl = lang == "ar";

            var query = _db.Absences
                .Include(a => a.Doctor)
                .Where(a => a.ClinicId == _clinicContext.ClinicId);

            if (!string.IsNullOrEmpty(doctorId) && Guid.TryParse(doctorId, out var docGuid))
                query = query.Where(a => a.DoctorId == docGuid);

            var absences = await query.OrderByDescending(a => a.StartDate).ToListAsync();

            var rows = absences.Select(a => new List<string> {
                a.Doctor?.FullName ?? (isRtl ? "العيادة كاملة" : "Whole Clinic"),
                GetDayTypeLabel(a.Type, isRtl),
                $"{a.StartDate:yyyy-MM-dd} — {a.EndDate:yyyy-MM-dd}",
                a.StartTime == null ? (isRtl ? "يوم كامل" : "Full day") : $"{a.StartTime:HH\\:mm} — {a.EndTime:HH\\:mm}",
                a.Notes ?? "—",
            }).ToList();

            var columns = isRtl
                ? new List<string> { "الطبيب/العيادة", "النوع", "الفترة", "الوقت", "ملاحظات" }
                : new List<string> { "Doctor/Clinic", "Type", "Period", "Time", "Notes" };

            var clinic = await _db.Clinics.FindAsync(_clinicContext.ClinicId.Value);

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "الإجازات" : "Absences",
                    Title = isRtl ? "جدول الإجازات" : "Absences Schedule",
                    Columns = columns,
                    Rows = rows,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "absences.xlsx");
            }
            else
            {
                var bytes = _pdfExport.GenerateTableReport(new PdfReportRequest
                {
                    Title = isRtl ? "جدول الإجازات" : "Absences Schedule",
                    ClinicName = clinic?.Name ?? "",
                    LogoPath = ResolveLogoPath(clinic?.Logo),
                    IsRtl = isRtl,
                    Columns = columns,
                    Rows = rows,
                });
                return File(bytes, "application/pdf", "absences.pdf");
            }
        }

        // POST: api/absences
        [HttpPost]
        public async Task<ActionResult> Create([FromBody] CreateAbsenceDto dto,
            [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("schedules.absence.add")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            if (dto.DoctorId.HasValue)
            {
                var doctor = await _db.Doctors.FindAsync(dto.DoctorId.Value);
                if (doctor == null || doctor.IsDeleted)
                    return BadRequest(Msg(lang, "الطبيب غير موجود", "Doctor not found"));
                if (doctor.ClinicId != _clinicContext.ClinicId)
                    return Forbid();
            }

            if (dto.EndDate < dto.StartDate)
                return BadRequest(Msg(lang,
                    "تاريخ النهاية يجب أن يكون بعد تاريخ البداية",
                    "End date must be after start date"));

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
            if (!_clinicContext.HasPermission("schedules.absence.delete")) return Forbid();

            var absence = await _db.Absences.FindAsync(id);
            if (absence == null) return NotFound();
            if (absence.ClinicId != _clinicContext.ClinicId) return Forbid();

            absence.IsDeleted = true;
            await _db.SaveChangesAsync();

            return Ok(new { message = Msg(lang, "تم حذف الإجازة", "Absence deleted") });
        }

        // GET: api/absences/check?doctorId=...&date=2026-07-01&time=09:00&lang=ar
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
        public Guid? DoctorId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsFullDay { get; set; } = true;
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public string? Type { get; set; }
        public string? Notes { get; set; }
    }
}