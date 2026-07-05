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
    public class ReportsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public ReportsController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        // ═══════════════════════════════════════
        // GET: api/reports
        // التقرير الرئيسي — Dashboard
        // ═══════════════════════════════════════
        [HttpGet]
        public async Task<ActionResult> GetReports()
        {
            if (!_clinicContext.HasPermission("reports.view")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var clinicId = _clinicContext.ClinicId.Value;
            var now = DateTime.UtcNow;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var startOfYear = new DateTime(now.Year, 1, 1);
            var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
            var yesterday = now.Date.AddDays(-1);
            var lastMonth = startOfMonth.AddMonths(-1);
            var lastMonthEnd = startOfMonth.AddDays(-1);

            var appointments = await _db.Appointments
                .Include(a => a.Doctor)
                .Include(a => a.Patient)
                .Where(a => a.ClinicId == clinicId && !a.isdeleted)
                .ToListAsync();

            // ── إجماليات ──
            var totalPatients = await _db.Patients.CountAsync(p => p.ClinicId == clinicId && !p.isdeleted);
            var totalDoctors = await _db.Doctors.CountAsync(d => d.ClinicId == clinicId && !d.isdeleted && d.IsActive);
            var totalAppointments = appointments.Count;
            var totalRevenue = appointments.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0);

            var completedCount = appointments.Count(a => a.Status == "completed");
            var cancelledCount = appointments.Count(a => a.Status == "cancelled");
            var scheduledCount = appointments.Count(a => a.Status is "scheduled" or "confirmed");
            var completionRate = totalAppointments > 0 ? Math.Round((double)completedCount / totalAppointments * 100, 1) : 0;
            var cancellationRate = totalAppointments > 0 ? Math.Round((double)cancelledCount / totalAppointments * 100, 1) : 0;

            // ── هذا الشهر ──
            var thisMonthAppts = appointments.Where(a => a.AppointmentDate >= startOfMonth).ToList();
            var lastMonthAppts = appointments.Where(a => a.AppointmentDate >= lastMonth && a.AppointmentDate < startOfMonth).ToList();
            var revenueThisMonth = thisMonthAppts.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0);
            var revenueLastMonth = lastMonthAppts.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0);
            var revenueGrowth = revenueLastMonth > 0 ? Math.Round((double)(revenueThisMonth - revenueLastMonth) / (double)revenueLastMonth * 100, 1) : 0;

            var newPatientsThisMonth = await _db.Patients.CountAsync(p => p.ClinicId == clinicId && !p.isdeleted && p.CreatedAt >= startOfMonth);
            var newPatientsLastMonth = await _db.Patients.CountAsync(p => p.ClinicId == clinicId && !p.isdeleted && p.CreatedAt >= lastMonth && p.CreatedAt < startOfMonth);
            var patientsGrowth = newPatientsLastMonth > 0 ? Math.Round((double)(newPatientsThisMonth - newPatientsLastMonth) / newPatientsLastMonth * 100, 1) : 0;

            // ── هذا الأسبوع ──
            var thisWeekAppts = appointments.Where(a => a.AppointmentDate >= startOfWeek).ToList();
            var revenueThisWeek = thisWeekAppts.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0);

            // ── اليوم ──
            var todayAppts = appointments.Where(a => a.AppointmentDate.Date == now.Date).ToList();
            var revenueToday = todayAppts.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0);

            // ── أداء الأطباء ──
            var doctorPerformance = appointments
                .Where(a => a.DoctorId != null && a.Doctor != null)
                .GroupBy(a => new { a.DoctorId, a.Doctor!.FullName, a.Doctor.Specialty })
                .Select(g => new {
                    doctorId = g.Key.DoctorId,
                    doctorName = g.Key.FullName,
                    specialty = g.Key.Specialty,
                    totalAppts = g.Count(),
                    completedAppts = g.Count(a => a.Status == "completed"),
                    cancelledAppts = g.Count(a => a.Status == "cancelled"),
                    revenue = g.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0),
                    completionRate = g.Count() > 0
                        ? Math.Round((double)g.Count(a => a.Status == "completed") / g.Count() * 100, 1) : 0,
                    thisMonthAppts = g.Count(a => a.AppointmentDate >= startOfMonth),
                    thisMonthRev = g.Where(a => a.Status == "completed" && a.AppointmentDate >= startOfMonth).Sum(a => a.Price ?? 0),
                })
                .OrderByDescending(d => d.totalAppts)
                .ToList();

            // ── مخطط الإيرادات والمواعيد — آخر 12 شهر ──
            var twelveMonthsAgo = new DateTime(now.AddMonths(-11).Year, now.AddMonths(-11).Month, 1);
            var monthlyTrend = appointments
                .Where(a => a.AppointmentDate >= twelveMonthsAgo)
                .GroupBy(a => new { a.AppointmentDate.Year, a.AppointmentDate.Month })
                .Select(g => new {
                    year = g.Key.Year,
                    month = g.Key.Month,
                    count = g.Count(),
                    completed = g.Count(a => a.Status == "completed"),
                    cancelled = g.Count(a => a.Status == "cancelled"),
                    revenue = g.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0),
                })
                .OrderBy(g => g.year).ThenBy(g => g.month)
                .ToList();

            // ── أوقات الذروة — توزيع المواعيد حسب اليوم ──
            var byDayOfWeek = appointments
                .GroupBy(a => a.AppointmentDate.DayOfWeek)
                .Select(g => new {
                    day = g.Key.ToString(),
                    dayAr = DayNameAr(g.Key),
                    count = g.Count(),
                })
                .OrderBy(d => (int)(DayOfWeek)Enum.Parse(typeof(DayOfWeek), d.day))
                .ToList();

            // ── أوقات الذروة — توزيع المواعيد حسب الساعة ──
            var byHour = appointments
                .GroupBy(a => a.AppointmentDate.Hour)
                .Select(g => new { hour = g.Key, count = g.Count() })
                .OrderBy(h => h.hour)
                .ToList();

            // ── نمو المرضى — آخر 12 شهر ──
            var patientGrowth = await _db.Patients
                .Where(p => p.ClinicId == clinicId && !p.isdeleted && p.CreatedAt >= twelveMonthsAgo)
                .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month })
                .Select(g => new { year = g.Key.Year, month = g.Key.Month, newPatients = g.Count() })
                .OrderBy(g => g.year).ThenBy(g => g.month)
                .ToListAsync();

            // ── المرضى المتكررون ──
            var returningPatients = appointments
                .GroupBy(a => a.PatientId)
                .Count(g => g.Count() > 1);
            var returningRate = totalPatients > 0 ? Math.Round((double)returningPatients / totalPatients * 100, 1) : 0;

            // ── توزيع المرضى حسب الجنس ──
            var genderDist = await _db.Patients
                .Where(p => p.ClinicId == clinicId && !p.isdeleted)
                .GroupBy(p => p.Gender ?? "غير محدد")
                .Select(g => new { gender = g.Key, count = g.Count() })
                .ToListAsync();

            // ── توزيع أنواع الزيارات ──
            var visitTypes = appointments
                .GroupBy(a => a.Type ?? "غير محدد")
                .Select(g => new { type = g.Key, count = g.Count() })
                .OrderByDescending(t => t.count)
                .ToList();

            // ── متوسط وقت الانتظار (CheckIn → AppointmentDate) ──
            var waitTimes = appointments
                .Where(a => a.CheckInTime.HasValue)
                .Select(a => (a.CheckInTime!.Value - a.AppointmentDate).TotalMinutes)
                .ToList();
            var avgWaitMinutes = waitTimes.Any() ? Math.Round(waitTimes.Average(), 1) : 0;

            // ── متوسط مدة الزيارة (CheckIn → CheckOut) ──
            var visitDurations = appointments
                .Where(a => a.CheckInTime.HasValue && a.CheckOutTime.HasValue)
                .Select(a => (a.CheckOutTime!.Value - a.CheckInTime!.Value).TotalMinutes)
                .ToList();
            var avgVisitMinutes = visitDurations.Any() ? Math.Round(visitDurations.Average(), 1) : 0;

            // ── أعلى 10 مرضى زيارةً ──
            var topPatients = appointments
                .GroupBy(a => new { a.PatientId, Name = a.Patient?.FullName ?? "—" })
                .Select(g => new {
                    patientId = g.Key.PatientId,
                    patientName = g.Key.Name,
                    visits = g.Count(),
                    totalSpent = g.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0),
                    lastVisit = g.Max(a => a.AppointmentDate),
                })
                .OrderByDescending(p => p.visits)
                .Take(10)
                .ToList();

            // ── إحصائيات الإجازات ──
            var absencesCount = await _db.Absences
                .CountAsync(a => a.ClinicId == clinicId);
            var absencesByType = await _db.Absences
                .Where(a => a.ClinicId == clinicId)
                .GroupBy(a => a.Type)
                .Select(g => new { type = g.Key, count = g.Count() })
                .ToListAsync();

            return Ok(new
            {
                // إجماليات
                totalPatients,
                totalDoctors,
                totalAppointments,
                totalRevenue,
                completedCount,
                cancelledCount,
                scheduledCount,
                completionRate,
                cancellationRate,

                // هذا الشهر
                revenueThisMonth,
                revenueLastMonth,
                revenueGrowth,
                newPatientsThisMonth,
                newPatientsLastMonth,
                patientsGrowth,
                appointmentsThisMonth = thisMonthAppts.Count,

                // هذا الأسبوع
                appointmentsThisWeek = thisWeekAppts.Count,
                revenueThisWeek,

                // اليوم
                appointmentsToday = todayAppts.Count,
                revenueToday,

                // مخططات
                monthlyTrend,
                patientGrowth,
                byDayOfWeek,
                byHour,
                visitTypes,
                genderDist,

                // أداء
                doctorPerformance,
                topPatients,
                returningPatients,
                returningRate,
                avgWaitMinutes,
                avgVisitMinutes,

                // إجازات
                absencesCount,
                absencesByType,
            });
        }

        // ═══════════════════════════════════════
        // GET: api/reports/range?from=2026-01-01&to=2026-06-30
        // تقرير بنطاق تاريخ مخصص
        // ═══════════════════════════════════════
        [HttpGet("range")]
        public async Task<ActionResult> GetByRange(
            [FromQuery] string from,
            [FromQuery] string to)
        {
            if (!_clinicContext.HasPermission("reports.view")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            if (!DateTime.TryParse(from, out var fromDate) || !DateTime.TryParse(to, out var toDate))
                return BadRequest("تاريخ غير صحيح");

            toDate = toDate.Date.AddDays(1); // نهاية اليوم
            var clinicId = _clinicContext.ClinicId.Value;

            var appointments = await _db.Appointments
                .Include(a => a.Doctor)
                .Include(a => a.Patient)
                .Where(a => a.ClinicId == clinicId && !a.isdeleted
                    && a.AppointmentDate >= fromDate && a.AppointmentDate < toDate)
                .ToListAsync();

            var newPatients = await _db.Patients
                .CountAsync(p => p.ClinicId == clinicId && !p.isdeleted
                    && p.CreatedAt >= fromDate && p.CreatedAt < toDate);

            var revenue = appointments.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0);
            var completed = appointments.Count(a => a.Status == "completed");
            var cancelled = appointments.Count(a => a.Status == "cancelled");

            var byDay = appointments
                .GroupBy(a => a.AppointmentDate.Date)
                .Select(g => new {
                    date = g.Key.ToString("yyyy-MM-dd"),
                    count = g.Count(),
                    completed = g.Count(a => a.Status == "completed"),
                    revenue = g.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0),
                })
                .OrderBy(d => d.date)
                .ToList();

            var doctorPerformance = appointments
                .Where(a => a.DoctorId != null && a.Doctor != null)
                .GroupBy(a => new { a.DoctorId, a.Doctor!.FullName })
                .Select(g => new {
                    doctorName = g.Key.FullName,
                    totalAppts = g.Count(),
                    completedAppts = g.Count(a => a.Status == "completed"),
                    revenue = g.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0),
                })
                .OrderByDescending(d => d.revenue)
                .ToList();

            return Ok(new
            {
                from = fromDate.ToString("yyyy-MM-dd"),
                to = toDate.AddDays(-1).ToString("yyyy-MM-dd"),
                totalAppointments = appointments.Count,
                completed,
                cancelled,
                revenue,
                newPatients,
                completionRate = appointments.Count > 0
                    ? Math.Round((double)completed / appointments.Count * 100, 1) : 0,
                byDay,
                doctorPerformance,
            });
        }

        // ═══════════════════════════════════════
        // GET: api/reports/doctor/{doctorId}
        // تقرير طبيب محدد
        // ═══════════════════════════════════════
        [HttpGet("doctor/{doctorId}")]
        public async Task<ActionResult> GetDoctorReport(Guid doctorId)
        {
            if (!_clinicContext.HasPermission("reports.view")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var clinicId = _clinicContext.ClinicId.Value;
            var now = DateTime.UtcNow;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);

            var doctor = await _db.Doctors.FindAsync(doctorId);
            if (doctor == null || doctor.ClinicId != clinicId) return NotFound();

            var appointments = await _db.Appointments
                .Include(a => a.Patient)
                .Where(a => a.DoctorId == doctorId && a.ClinicId == clinicId && !a.isdeleted)
                .OrderByDescending(a => a.AppointmentDate)
                .ToListAsync();

            var monthly = appointments
                .GroupBy(a => new { a.AppointmentDate.Year, a.AppointmentDate.Month })
                .Select(g => new {
                    year = g.Key.Year,
                    month = g.Key.Month,
                    count = g.Count(),
                    completed = g.Count(a => a.Status == "completed"),
                    revenue = g.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0),
                })
                .OrderBy(g => g.year).ThenBy(g => g.month)
                .ToList();

            return Ok(new
            {
                doctorId,
                doctorName = doctor.FullName,
                specialty = doctor.Specialty,
                totalAppts = appointments.Count,
                completedAppts = appointments.Count(a => a.Status == "completed"),
                cancelledAppts = appointments.Count(a => a.Status == "cancelled"),
                totalRevenue = appointments.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0),
                thisMonthAppts = appointments.Count(a => a.AppointmentDate >= startOfMonth),
                thisMonthRev = appointments.Where(a => a.Status == "completed" && a.AppointmentDate >= startOfMonth).Sum(a => a.Price ?? 0),
                uniquePatients = appointments.Select(a => a.PatientId).Distinct().Count(),
                completionRate = appointments.Count > 0
                    ? Math.Round((double)appointments.Count(a => a.Status == "completed") / appointments.Count * 100, 1) : 0,
                monthly,
                recentAppointments = appointments.Take(10).Select(a => new {
                    a.Id,
                    patientName = a.Patient?.FullName,
                    date = a.AppointmentDate.ToString("yyyy-MM-dd HH:mm"),
                    a.Status,
                    a.Price,
                }),
            });
        }

        // ═══════════════════════════════════════
        // GET: api/reports/patients
        // تقرير المرضى التفصيلي
        // ═══════════════════════════════════════
        [HttpGet("patients")]
        public async Task<ActionResult> GetPatientsReport()
        {
            if (!_clinicContext.HasPermission("reports.view")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var clinicId = _clinicContext.ClinicId.Value;
            var now = DateTime.UtcNow;

            var patients = await _db.Patients
                .Where(p => p.ClinicId == clinicId && !p.isdeleted)
                .ToListAsync();

            var appointments = await _db.Appointments
                .Where(a => a.ClinicId == clinicId && !a.isdeleted)
                .ToListAsync();

            // نمو شهري آخر 12 شهر
            var twelveAgo = new DateTime(now.AddMonths(-11).Year, now.AddMonths(-11).Month, 1);
            var monthly = patients
                .Where(p => p.CreatedAt >= twelveAgo)
                .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month })
                .Select(g => new { year = g.Key.Year, month = g.Key.Month, count = g.Count() })
                .OrderBy(g => g.year).ThenBy(g => g.month)
                .ToList();

            // توزيع الجنس
            var genderDist = patients
                .GroupBy(p => p.Gender ?? "غير محدد")
                .Select(g => new { gender = g.Key, count = g.Count() })
                .ToList();

            // توزيع فئات العمر
            var ageDist = patients
                .Where(p => p.DateOfBirth.HasValue)
                .GroupBy(p => {
                    var age = (now - p.DateOfBirth!.Value).Days / 365;
                    return age < 18 ? "أقل من 18" : age < 30 ? "18-29" : age < 45 ? "30-44" : age < 60 ? "45-59" : "60+";
                })
                .Select(g => new { ageGroup = g.Key, count = g.Count() })
                .ToList();

            // أكثر المرضى زيارةً
            var topPatients = appointments
                .GroupBy(a => a.PatientId)
                .Select(g => new {
                    patientId = g.Key,
                    visits = g.Count(),
                    totalSpent = g.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0),
                    lastVisit = g.Max(a => a.AppointmentDate).ToString("yyyy-MM-dd"),
                })
                .OrderByDescending(p => p.visits)
                .Take(10)
                .ToList();

            // المرضى الجدد هذا الشهر
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var newThisMonth = patients.Count(p => p.CreatedAt >= startOfMonth);

            return Ok(new
            {
                total = patients.Count,
                newThisMonth,
                active = appointments.Select(a => a.PatientId).Distinct().Count(),
                returning = appointments.GroupBy(a => a.PatientId).Count(g => g.Count() > 1),
                genderDist,
                ageDist,
                monthly,
                topPatients,
            });
        }

        // ═══════════════════════════════════════
        // GET: api/reports/appointments
        // تقرير المواعيد التفصيلي
        // ═══════════════════════════════════════
        [HttpGet("appointments")]
        public async Task<ActionResult> GetAppointmentsReport()
        {
            if (!_clinicContext.HasPermission("reports.view")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var clinicId = _clinicContext.ClinicId.Value;
            var appointments = await _db.Appointments
                .Where(a => a.ClinicId == clinicId && !a.isdeleted)
                .ToListAsync();

            var byStatus = appointments
                .GroupBy(a => a.Status)
                .Select(g => new { status = g.Key, count = g.Count() })
                .ToList();

            var byType = appointments
                .GroupBy(a => a.Type ?? "غير محدد")
                .Select(g => new { type = g.Key, count = g.Count() })
                .OrderByDescending(t => t.count)
                .ToList();

            var byDayOfWeek = appointments
                .GroupBy(a => a.AppointmentDate.DayOfWeek)
                .Select(g => new { day = DayNameAr(g.Key), count = g.Count() })
                .OrderByDescending(d => d.count)
                .ToList();

            var byHour = appointments
                .GroupBy(a => a.AppointmentDate.Hour)
                .Select(g => new { hour = $"{g.Key:00}:00", count = g.Count() })
                .OrderBy(h => h.hour)
                .ToList();

            var visitDurations = appointments
                .Where(a => a.CheckInTime.HasValue && a.CheckOutTime.HasValue)
                .Select(a => (a.CheckOutTime!.Value - a.CheckInTime!.Value).TotalMinutes)
                .ToList();

            return Ok(new
            {
                total = appointments.Count,
                completed = appointments.Count(a => a.Status == "completed"),
                cancelled = appointments.Count(a => a.Status == "cancelled"),
                scheduled = appointments.Count(a => a.Status is "scheduled" or "confirmed"),
                completionRate = appointments.Count > 0 ? Math.Round((double)appointments.Count(a => a.Status == "completed") / appointments.Count * 100, 1) : 0,
                cancellationRate = appointments.Count > 0 ? Math.Round((double)appointments.Count(a => a.Status == "cancelled") / appointments.Count * 100, 1) : 0,
                avgVisitMinutes = visitDurations.Any() ? Math.Round(visitDurations.Average(), 1) : 0,
                byStatus,
                byType,
                byDayOfWeek,
                byHour,
            });
        }

 
        // ═══════════════════════════════════════
        // GET: api/reports/detail
        // تقرير تفصيلي مرن بفلاتر متعددة
        // ?date=2026-07-01 &from= &to= &doctorId= &status= &patientId= &page= &pageSize=
        // ═══════════════════════════════════════
        [HttpGet("detail")]
        public async Task<ActionResult> GetDetail(
            [FromQuery] string? date,
            [FromQuery] string? from,
            [FromQuery] string? to,
            [FromQuery] Guid? doctorId,
            [FromQuery] string? status,
            [FromQuery] Guid? patientId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            if (!_clinicContext.HasPermission("reports.view")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var clinicId = _clinicContext.ClinicId.Value;
            var query = _db.Appointments
                .Include(a => a.Doctor)
                .Include(a => a.Patient)
                .Where(a => a.ClinicId == clinicId && !a.isdeleted);

            // ── فلتر التاريخ ──
            if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date, out var d))
            {
                query = query.Where(a => a.AppointmentDate.Date == d.Date);
            }
            else
            {
                if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fd))
                    query = query.Where(a => a.AppointmentDate >= fd);
                if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var td))
                    query = query.Where(a => a.AppointmentDate < td.Date.AddDays(1));
            }

            // ── فلتر الطبيب ──
            if (doctorId.HasValue)
                query = query.Where(a => a.DoctorId == doctorId);

            // ── فلتر الحالة ──
            if (!string.IsNullOrEmpty(status))
                query = query.Where(a => a.Status == status);

            // ── فلتر المريض ──
            if (patientId.HasValue)
                query = query.Where(a => a.PatientId == patientId);

            var total = await query.CountAsync();
            var totalRevenue = await query
                .Where(a => a.Status == "completed")
                .SumAsync(a => (decimal?)(a.Price ?? 0)) ?? 0;

            var items = await query
                .OrderByDescending(a => a.AppointmentDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new {
                    a.Id,
                    patientName = a.Patient != null ? a.Patient.FullName : "—",
                    patientNumber = a.Patient != null ? a.Patient.PatientNumber : 0,
                    doctorName = a.Doctor != null ? a.Doctor.FullName : "—",
                    doctorSpecialty = a.Doctor != null ? a.Doctor.Specialty : "",
                    date = a.AppointmentDate.ToString("yyyy-MM-dd"),
                    time = a.AppointmentDate.ToString("HH:mm"),
                    a.Status,
                    a.Type,
                    a.Price,
                    checkIn = a.CheckInTime.HasValue ? a.CheckInTime.Value.ToString("HH:mm") : null,
                    checkOut = a.CheckOutTime.HasValue ? a.CheckOutTime.Value.ToString("HH:mm") : null,
                    durationMin = a.CheckInTime.HasValue && a.CheckOutTime.HasValue
                        ? (int)(a.CheckOutTime.Value - a.CheckInTime.Value).TotalMinutes : (int?)null,
                    a.Notes,
                })
                .ToListAsync();

            // ── ملخص الفلترة ──
            var summary = new
            {
                total,
                totalRevenue,
                completed = await query.CountAsync(a => a.Status == "completed"),
                cancelled = await query.CountAsync(a => a.Status == "cancelled"),
                scheduled = await query.CountAsync(a => a.Status == "scheduled" || a.Status == "confirmed"),
                pages = (int)Math.Ceiling((double)total / pageSize),
                page,
                pageSize,
            };

            return Ok(new { summary, items });
        }

        // ═══════════════════════════════════════
        // GET: api/reports/patients-detail
        // تقرير تفصيلي للمرضى مع إحصائيات زياراتهم
        // ?search= &doctorId= &from= &to= &page= &pageSize=
        // ═══════════════════════════════════════
        [HttpGet("patients-detail")]
        public async Task<ActionResult> GetPatientsDetail(
            [FromQuery] string? search,
            [FromQuery] Guid? doctorId,
            [FromQuery] string? from,
            [FromQuery] string? to,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            if (!_clinicContext.HasPermission("reports.view")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var clinicId = _clinicContext.ClinicId.Value;

            var patientsQuery = _db.Patients
                .Where(p => p.ClinicId == clinicId && !p.isdeleted);

            if (!string.IsNullOrEmpty(search))
                patientsQuery = patientsQuery.Where(p =>
                    p.FullName.Contains(search) ||
                    (p.Phone != null && p.Phone.Contains(search)) ||
                    p.PatientNumber.ToString().Contains(search));

            var total = await patientsQuery.CountAsync();
            var patients = await patientsQuery
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var patientIds = patients.Select(p => p.Id).ToList();

            // جلب المواعيد لهؤلاء المرضى
            var apptsQuery = _db.Appointments
                .Where(a => a.ClinicId == clinicId && !a.isdeleted && patientIds.Contains(a.PatientId));

            if (doctorId.HasValue)
                apptsQuery = apptsQuery.Where(a => a.DoctorId == doctorId);
            if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fd))
                apptsQuery = apptsQuery.Where(a => a.AppointmentDate >= fd);
            if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var td))
                apptsQuery = apptsQuery.Where(a => a.AppointmentDate < td.Date.AddDays(1));

            var appts = await apptsQuery
                .Include(a => a.Doctor)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var items = patients.Select(p => {
                var pAppts = appts.Where(a => a.PatientId == p.Id).ToList();
                return new
                {
                    p.Id,
                    p.PatientNumber,
                    p.FullName,
                    p.Phone,
                    p.Gender,
                    age = p.DateOfBirth.HasValue ? (now - p.DateOfBirth.Value).Days / 365 : (int?)null,
                    registeredAt = p.CreatedAt.ToString("yyyy-MM-dd"),
                    totalVisits = pAppts.Count,
                    completedVisits = pAppts.Count(a => a.Status == "completed"),
                    cancelledVisits = pAppts.Count(a => a.Status == "cancelled"),
                    totalSpent = pAppts.Where(a => a.Status == "completed").Sum(a => a.Price ?? 0),
                    firstVisit = pAppts.Any() ? pAppts.Min(a => a.AppointmentDate).ToString("yyyy-MM-dd") : null,
                    lastVisit = pAppts.Any() ? pAppts.Max(a => a.AppointmentDate).ToString("yyyy-MM-dd") : null,
                    doctors = pAppts.Where(a => a.Doctor != null).Select(a => a.Doctor!.FullName).Distinct().ToList(),
                };
            }).ToList();

            return Ok(new
            {
                total,
                pages = (int)Math.Ceiling((double)total / pageSize),
                page,
                pageSize,
                items,
            });
        }

        // ─── دالة مساعدة ───
        private static string DayNameAr(DayOfWeek day) => day switch
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
    }
}