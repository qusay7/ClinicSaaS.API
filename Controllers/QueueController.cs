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
	public class QueueController : ControllerBase
	{
		private readonly ApplicationDbContext _db;
		private readonly IClinicContext _clinicContext;

		public QueueController(ApplicationDbContext db, IClinicContext clinicContext)
		{
			_db = db;
			_clinicContext = clinicContext;
		}

		// ✅ يحسب "اليوم" بتوقيت العيادة المحلي بدل UTC مباشرة
		private async Task<DateTime> GetClinicToday()
		{
			var clinic = await _db.Clinics.FindAsync(_clinicContext.ClinicId);
			var tzId = clinic?.TimeZone ?? "Asia/Amman";

			try
			{
				var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
				var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
				return localNow.Date;
			}
			catch
			{
				return DateTime.UtcNow.Date;
			}
		}

		// GET: api/queue/today
		[HttpGet("today")]
		public async Task<ActionResult> GetToday([FromQuery] Guid? doctorId)
		{
			if (_clinicContext.ClinicId == null) return Unauthorized();

			var today = await GetClinicToday();

			var query = _db.QueueEntries
				.Where(q => q.ClinicId == _clinicContext.ClinicId
					&& q.Date == today
					&& !q.IsDeleted);

			if (doctorId.HasValue)
				query = query.Where(q => q.DoctorId == doctorId);

			var entries = await query
				.Include(q => q.Patient)
				.Include(q => q.Doctor)
				.OrderBy(q => q.QueueNumber)
				.ToListAsync();

			return Ok(entries.Select(q => new
			{
				q.Id,
				q.QueueNumber,
				q.Status,
				q.Notes,
				q.CreatedAt,
				patientId = q.PatientId,
				patientName = q.Patient.FullName,
				patientPhone = q.Patient.Phone,
				doctorId = q.DoctorId,
				doctorName = q.Doctor?.FullName,
			}));
		}

		// POST: api/queue
		[HttpPost]
		public async Task<ActionResult> AddToQueue([FromBody] AddToQueueDto dto)
		{
			if (_clinicContext.ClinicId == null) return Unauthorized();

			var today = await GetClinicToday();

			var alreadyInQueue = await _db.QueueEntries
				.AnyAsync(q => q.ClinicId == _clinicContext.ClinicId
					&& q.PatientId == dto.PatientId
					&& q.Date == today
					&& !q.IsDeleted
					&& q.Status != "cancelled"
					&& q.Status != "completed");

			if (alreadyInQueue)
				return BadRequest("المريض موجود بالفعل في قائمة الانتظار اليوم");

			const int maxRetries = 3;
			for (int attempt = 1; attempt <= maxRetries; attempt++)
			{
				var lastNumber = await _db.QueueEntries
					.Where(q => q.ClinicId == _clinicContext.ClinicId && q.Date == today && !q.IsDeleted)
					.MaxAsync(q => (int?)q.QueueNumber) ?? 0;

				var entry = new QueueEntry
				{
					Id = Guid.NewGuid(),
					ClinicId = _clinicContext.ClinicId.Value,
					PatientId = dto.PatientId,
					DoctorId = dto.DoctorId,
					QueueNumber = lastNumber + 1,
					Date = today,
					Status = "waiting",
					Notes = dto.Notes,
					CreatedAt = DateTime.UtcNow,
				};

				_db.QueueEntries.Add(entry);

				try
				{
					await _db.SaveChangesAsync();
					await _db.Entry(entry).Reference(q => q.Patient).LoadAsync();

					return Ok(new
					{
						entry.Id,
						entry.QueueNumber,
						entry.Status,
						patientName = entry.Patient.FullName,
						message = $"تم تسجيل المريض — رقم الدور: {entry.QueueNumber}"
					});
				}
				catch (DbUpdateException) when (attempt < maxRetries)
				{
					_db.Entry(entry).State = EntityState.Detached;
				}
			}

			return Conflict("تعذر إنشاء رقم دور فريد، يرجى المحاولة مرة أخرى");
		}

		// PUT: api/queue/{id}/call
		[HttpPut("{id}/call")]
		public async Task<ActionResult> CallPatient(Guid id)
		{
			if (_clinicContext.ClinicId == null) return Unauthorized();

			var entry = await _db.QueueEntries
				.Include(q => q.Patient)
				.FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted);

			if (entry == null) return NotFound();
			if (entry.ClinicId != _clinicContext.ClinicId) return Forbid();

			entry.Status = "called";
			await _db.SaveChangesAsync();

			return Ok(new { message = $"تم استدعاء {entry.Patient.FullName} — رقم {entry.QueueNumber}" });
		}

		// PUT: api/queue/{id}/complete
		[HttpPut("{id}/complete")]
		public async Task<ActionResult> CompletePatient(Guid id)
		{
			if (_clinicContext.ClinicId == null) return Unauthorized();

			var entry = await _db.QueueEntries
				.FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted);

			if (entry == null) return NotFound();
			if (entry.ClinicId != _clinicContext.ClinicId) return Forbid();

			entry.Status = "completed";
			await _db.SaveChangesAsync();

			return Ok(new { message = "تم إنهاء الدور بنجاح" });
		}

		// PUT: api/queue/{id}/cancel
		[HttpPut("{id}/cancel")]
		public async Task<ActionResult> CancelPatient(Guid id)
		{
			if (_clinicContext.ClinicId == null) return Unauthorized();

			var entry = await _db.QueueEntries
				.FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted);

			if (entry == null) return NotFound();
			if (entry.ClinicId != _clinicContext.ClinicId) return Forbid();

			entry.Status = "cancelled";
			await _db.SaveChangesAsync();

			return Ok(new { message = "تم إلغاء الدور" });
		}

		// GET: api/queue/stats
		[HttpGet("stats")]
		public async Task<ActionResult> GetStats()
		{
			if (_clinicContext.ClinicId == null) return Unauthorized();

			var today = await GetClinicToday();

			var entries = await _db.QueueEntries
				.Where(q => q.ClinicId == _clinicContext.ClinicId
					&& q.Date == today
					&& !q.IsDeleted)
				.ToListAsync();

			return Ok(new
			{
				total = entries.Count,
				waiting = entries.Count(q => q.Status == "waiting"),
				called = entries.Count(q => q.Status == "called"),
				completed = entries.Count(q => q.Status == "completed"),
				cancelled = entries.Count(q => q.Status == "cancelled"),
				nextNumber = (entries.Max(q => (int?)q.QueueNumber) ?? 0) + 1,
			});
		}
	}

	public class AddToQueueDto
	{
		public Guid PatientId { get; set; }
		public Guid? DoctorId { get; set; }
		public string? Notes { get; set; }
	}
}