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

		// ✅ يحسب الوقت الحالي بتوقيت العيادة المحلي بدل UTC مباشرة
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
			var now = await GetClinicNow(clinicId);
			var startOfMonth = new DateTime(now.Year, now.Month, 1);
			var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
			var lastMonth = startOfMonth.AddMonths(-1);

			var baseQuery = _db.Appointments
				.Where(a => a.ClinicId == clinicId && !a.IsDeleted);

			// ── إجماليات — تُحسب مباشرة بقاعدة البيانات، بدون تحميل الصفوف كاملة ──
			var totalPatients = await _db.Patients.CountAsync(p => p.ClinicId == clinicId && !p.IsDeleted);
			var totalDoctors = await _db.Doctors.CountAsync(d => d.ClinicId == clinicId && !d.IsDeleted && d.IsActive);
			var totalAppointments = await baseQuery.CountAsync();
			var completedCount = await baseQuery.CountAsync(a => a.Status == "completed");
			var cancelledCount = await baseQuery.CountAsync(a => a.Status == "cancelled");
			var scheduledCount = await baseQuery.CountAsync(a => a.Status == "scheduled" || a.Status == "confirmed");
			var totalRevenue = await baseQuery
				.Where(a => a.Status == "completed")
				.SumAsync(a => (decimal?)(a.Price ?? 0)) ?? 0;

			var completionRate = totalAppointments > 0 ? Math.Round((double)completedCount / totalAppointments * 100, 1) : 0;
			var cancellationRate = totalAppointments > 0 ? Math.Round((double)cancelledCount / totalAppointments * 100, 1) : 0;

			// ── هذا الشهر / الشهر الماضي ──
			var appointmentsThisMonth = await baseQuery.CountAsync(a => a.AppointmentDate >= startOfMonth);
			var revenueThisMonth = await baseQuery
				.Where(a => a.Status == "completed" && a.AppointmentDate >= startOfMonth)
				.SumAsync(a => (decimal?)(a.Price ?? 0)) ?? 0;
			var revenueLastMonth = await baseQuery
				.Where(a => a.Status == "completed" && a.AppointmentDate >= lastMonth && a.AppointmentDate < startOfMonth)
				.SumAsync(a => (decimal?)(a.Price ?? 0)) ?? 0;
			var revenueGrowth = revenueLastMonth > 0
				? Math.Round((double)(revenueThisMonth - revenueLastMonth) / (double)revenueLastMonth * 100, 1) : 0;

			var newPatientsThisMonth = await _db.Patients.CountAsync(p => p.ClinicId == clinicId && !p.IsDeleted && p.CreatedAt >= startOfMonth);
			var newPatientsLastMonth = await _db.Patients.CountAsync(p => p.ClinicId == clinicId && !p.IsDeleted && p.CreatedAt >= lastMonth && p.CreatedAt < startOfMonth);
			var patientsGrowth = newPatientsLastMonth > 0
				? Math.Round((double)(newPatientsThisMonth - newPatientsLastMonth) / newPatientsLastMonth * 100, 1) : 0;

			// ── هذا الأسبوع ──
			var appointmentsThisWeek = await baseQuery.CountAsync(a => a.AppointmentDate >= startOfWeek);
			var revenueThisWeek = await baseQuery
				.Where(a => a.Status == "completed" && a.AppointmentDate >= startOfWeek)
				.SumAsync(a => (decimal?)(a.Price ?? 0)) ?? 0;

			// ── اليوم ──
			var appointmentsToday = await baseQuery.CountAsync(a => a.AppointmentDate.Date == now.Date);
			var revenueToday = await baseQuery
				.Where(a => a.Status == "completed" && a.AppointmentDate.Date == now.Date)
				.SumAsync(a => (decimal?)(a.Price ?? 0)) ?? 0;

			// ── نطاق محدود (آخر 12 شهر) لبيانات التجميع الزمني المعقدة فقط ──
			// هذا الجزء وحده يحتاج تحميل صفوف فعلية بالذاكرة، لكن محصور بفترة زمنية، مو كل تاريخ العيادة
			var twelveMonthsAgo = new DateTime(now.AddMonths(-11).Year, now.AddMonths(-11).Month, 1);
			var recentAppointments = await _db.Appointments
				.Include(a => a.Doctor)
				.Include(a => a.Patient)
				.Where(a => a.ClinicId == clinicId && !a.IsDeleted && a.AppointmentDate >= twelveMonthsAgo)
				.ToListAsync();

			// ── أداء الأطباء (على نطاق آخر 12 شهر) ──
			var doctorPerformance = recentAppointments
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
			var monthlyTrend = recentAppointments
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

			// ── أوقات الذروة — توزيع المواعيد حسب اليوم (آخر 12 شهر) ──
			var byDayOfWeek = recentAppointments
				.GroupBy(a => a.AppointmentDate.DayOfWeek)
				.OrderBy(g => (int)g.Key)   // ✅ رتب بالـ enum مباشرة بدل تحويل String → Enum
				.Select(g => new {
					day = g.Key.ToString(),
					dayAr = DayNameAr(g.Key),
					count = g.Count(),
				})
				.ToList();

			// ── أوقات الذروة — توزيع المواعيد حسب الساعة (آخر 12 شهر) ──
			var byHour = recentAppointments
				.GroupBy(a => a.AppointmentDate.Hour)
				.Select(g => new { hour = g.Key, count = g.Count() })
				.OrderBy(h => h.hour)
				.ToList();

			// ── نمو المرضى — آخر 12 شهر (SQL مباشرة) ──
			var patientGrowth = await _db.Patients
				.Where(p => p.ClinicId == clinicId && !p.IsDeleted && p.CreatedAt >= twelveMonthsAgo)
				.GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month })
				.Select(g => new { year = g.Key.Year, month = g.Key.Month, newPatients = g.Count() })
				.OrderBy(g => g.year).ThenBy(g => g.month)
				.ToListAsync();

			// ── المرضى المتكررون (على نطاق آخر 12 شهر) ──
			var returningPatients = recentAppointments
				.GroupBy(a => a.PatientId)
				.Count(g => g.Count() > 1);
			var returningRate = totalPatients > 0 ? Math.Round((double)returningPatients / totalPatients * 100, 1) : 0;

			// ── توزيع المرضى حسب الجنس (SQL مباشرة) ──
			var genderDist = await _db.Patients
				.Where(p => p.ClinicId == clinicId && !p.IsDeleted)
				.GroupBy(p => p.Gender ?? "غير محدد")
				.Select(g => new { gender = g.Key, count = g.Count() })
				.ToListAsync();

			// ── توزيع أنواع الزيارات (آخر 12 شهر) ──
			var visitTypes = recentAppointments
				.GroupBy(a => a.Type ?? "غير محدد")
				.Select(g => new { type = g.Key, count = g.Count() })
				.OrderByDescending(t => t.count)
				.ToList();

			// ── متوسط وقت الانتظار (آخر 12 شهر) ──
			var waitTimes = recentAppointments
				.Where(a => a.CheckInTime.HasValue)
				.Select(a => (a.CheckInTime!.Value - a.AppointmentDate).TotalMinutes)
				.ToList();
			var avgWaitMinutes = waitTimes.Any() ? Math.Round(waitTimes.Average(), 1) : 0;

			// ── متوسط مدة الزيارة (آخر 12 شهر) ──
			var visitDurations = recentAppointments
				.Where(a => a.CheckInTime.HasValue && a.CheckOutTime.HasValue)
				.Select(a => (a.CheckOutTime!.Value - a.CheckInTime!.Value).TotalMinutes)
				.ToList();
			var avgVisitMinutes = visitDurations.Any() ? Math.Round(visitDurations.Average(), 1) : 0;

			// ── أعلى 10 مرضى زيارةً (آخر 12 شهر) ──
			var topPatients = recentAppointments
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

			// ── إحصائيات الإجازات (SQL مباشرة) ──
			var absencesCount = await _db.Absences.CountAsync(a => a.ClinicId == clinicId);
			var absencesByType = await _db.Absences
				.Where(a => a.ClinicId == clinicId)
				.GroupBy(a => a.Type)
				.Select(g => new { type = g.Key, count = g.Count() })
				.ToListAsync();

			return Ok(new
			{
				totalPatients,
				totalDoctors,
				totalAppointments,
				totalRevenue,
				completedCount,
				cancelledCount,
				scheduledCount,
				completionRate,
				cancellationRate,

				revenueThisMonth,
				revenueLastMonth,
				revenueGrowth,
				newPatientsThisMonth,
				newPatientsLastMonth,
				patientsGrowth,
				appointmentsThisMonth,

				appointmentsThisWeek,
				revenueThisWeek,

				appointmentsToday,
				revenueToday,

				monthlyTrend,
				patientGrowth,
				byDayOfWeek,
				byHour,
				visitTypes,
				genderDist,

				doctorPerformance,
				topPatients,
				returningPatients,
				returningRate,
				avgWaitMinutes,
				avgVisitMinutes,

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

			// ✅ تحقق من صحة النطاق قبل الاستمرار
			if (fromDate > toDate)
				return BadRequest("تاريخ البداية يجب أن يكون قبل تاريخ النهاية");

			toDate = toDate.Date.AddDays(1); // نهاية اليوم
			var clinicId = _clinicContext.ClinicId.Value;

			var appointments = await _db.Appointments
				.Include(a => a.Doctor)
				.Include(a => a.Patient)
				.Where(a => a.ClinicId == clinicId && !a.IsDeleted
					&& a.AppointmentDate >= fromDate && a.AppointmentDate < toDate)
				.ToListAsync();

			var newPatients = await _db.Patients
				.CountAsync(p => p.ClinicId == clinicId && !p.IsDeleted
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
			var now = await GetClinicNow(clinicId);
			var startOfMonth = new DateTime(now.Year, now.Month, 1);

			var doctor = await _db.Doctors.FindAsync(doctorId);
			if (doctor == null || doctor.ClinicId != clinicId) return NotFound();

			var appointments = await _db.Appointments
				.Include(a => a.Patient)
				.Where(a => a.DoctorId == doctorId && a.ClinicId == clinicId && !a.IsDeleted)
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
			var now = await GetClinicNow(clinicId);

			var patients = await _db.Patients
				.Where(p => p.ClinicId == clinicId && !p.IsDeleted)
				.ToListAsync();

			// ✅ نجيب بس مواعيد آخر 12 شهر لحساب "المتكررين" و"النشطين"، مو كل تاريخ العيادة
			var twelveAgo = new DateTime(now.AddMonths(-11).Year, now.AddMonths(-11).Month, 1);
			var recentAppointments = await _db.Appointments
				.Where(a => a.ClinicId == clinicId && !a.IsDeleted && a.AppointmentDate >= twelveAgo)
				.ToListAsync();

			var monthly = patients
				.Where(p => p.CreatedAt >= twelveAgo)
				.GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month })
				.Select(g => new { year = g.Key.Year, month = g.Key.Month, count = g.Count() })
				.OrderBy(g => g.year).ThenBy(g => g.month)
				.ToList();

			var genderDist = patients
				.GroupBy(p => p.Gender ?? "غير محدد")
				.Select(g => new { gender = g.Key, count = g.Count() })
				.ToList();

			var ageDist = patients
				.Where(p => p.DateOfBirth.HasValue)
				.GroupBy(p => {
					var age = (now - p.DateOfBirth!.Value).Days / 365;
					return age < 18 ? "أقل من 18" : age < 30 ? "18-29" : age < 45 ? "30-44" : age < 60 ? "45-59" : "60+";
				})
				.Select(g => new { ageGroup = g.Key, count = g.Count() })
				.ToList();

			var topPatients = recentAppointments
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

			var startOfMonth = new DateTime(now.Year, now.Month, 1);
			var newThisMonth = patients.Count(p => p.CreatedAt >= startOfMonth);

			return Ok(new
			{
				total = patients.Count,
				newThisMonth,
				active = recentAppointments.Select(a => a.PatientId).Distinct().Count(),
				returning = recentAppointments.GroupBy(a => a.PatientId).Count(g => g.Count() > 1),
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
			var now = await GetClinicNow(clinicId);

			// ✅ محصور بآخر 12 شهر بدل كل تاريخ العيادة
			var twelveAgo = new DateTime(now.AddMonths(-11).Year, now.AddMonths(-11).Month, 1);
			var appointments = await _db.Appointments
				.Where(a => a.ClinicId == clinicId && !a.IsDeleted && a.AppointmentDate >= twelveAgo)
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
				.OrderByDescending(g => g.Count())
				.Select(g => new { day = DayNameAr(g.Key), count = g.Count() })
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
				.Where(a => a.ClinicId == clinicId && !a.IsDeleted);

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
				.Where(p => p.ClinicId == clinicId && !p.IsDeleted);

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

			var apptsQuery = _db.Appointments
				.Where(a => a.ClinicId == clinicId && !a.IsDeleted && patientIds.Contains(a.PatientId));

			if (doctorId.HasValue)
				apptsQuery = apptsQuery.Where(a => a.DoctorId == doctorId);
			if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fd))
				apptsQuery = apptsQuery.Where(a => a.AppointmentDate >= fd);
			if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var td))
				apptsQuery = apptsQuery.Where(a => a.AppointmentDate < td.Date.AddDays(1));

			var appts = await apptsQuery
				.Include(a => a.Doctor)
				.ToListAsync();

			var now = await GetClinicNow(clinicId);
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