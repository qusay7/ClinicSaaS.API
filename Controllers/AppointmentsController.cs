using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Appointments;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AppointmentsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly IRoleSeedingService _roleSeedingService;   // ✅ جديد

        public AppointmentsController(ApplicationDbContext db, IClinicContext clinicContext, IRoleSeedingService roleSeedingService)
        {
            _db = db;
            _clinicContext = clinicContext;
            _roleSeedingService = roleSeedingService;
        }

        private static string Msg(string? lang, string ar, string en)
            => lang == "ar" ? ar : en;

        // GET: api/appointments?date=&doctorId=
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AppointmentResponseDto>>> GetAll(
            [FromQuery] DateTime? date, [FromQuery] Guid? doctorId,
            [FromQuery] Guid? patientId, [FromQuery] DateTime? dateFrom)
        {
            var query = _db.Appointments.Where(a => !a.IsDeleted );

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
                            && !d.IsDeleted );

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

        // GET: api/appointments/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<AppointmentResponseDto>> GetById(Guid id)
        {
            var appointment = await _db.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted );

            if (appointment == null) return NotFound();

            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            return Ok(ToResponse(appointment));
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
            var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == patientId && !p.IsDeleted );
            if (patient == null) return NotFound("المريض غير موجود");
            if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId) return Forbid();

            var query = _db.Appointments.Where(a => a.PatientId == patientId && !a.IsDeleted );
            if (!_clinicContext.IsCompanyStaff)
                query = query.Where(a => a.ClinicId == _clinicContext.ClinicId);

            var appointments = await query
                .OrderByDescending(a => a.AppointmentDate)
                .Include(a => a.Patient)
                .ToListAsync();

            return Ok(appointments.Select(a => ToResponse(a)).ToList());
        }

		// POST: api/appointments
		[HttpPost]
		public async Task<ActionResult<AppointmentResponseDto>> Create([FromBody] CreateAppointmentDto dto)
		{
			var lang = dto.Lang ?? "ar";

			if (!_clinicContext.HasPermission("appointments.create")) return Forbid();

			if (_clinicContext.IsSuperAdmin)
				return BadRequest(Msg(lang, "SuperAdmin لا يستطيع إضافة مواعيد مباشرة", "SuperAdmin cannot add appointments directly"));

			if (_clinicContext.ClinicId == null)
				return Unauthorized(Msg(lang, "لا توجد عيادة مرتبطة بهذا المستخدم", "No clinic associated with this user"));

			var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == dto.PatientId && !p.IsDeleted);
			if (patient == null)
				return BadRequest(Msg(lang, "المريض غير موجود", "Patient not found"));
			if (patient.ClinicId != _clinicContext.ClinicId) return Forbid();

			int? slotDurationForFinalCheck = null;

			if (dto.DoctorId.HasValue)
			{
				var clinic = await _db.Clinics.FindAsync(_clinicContext.ClinicId);
				var tzId = clinic?.TimeZone ?? "Asia/Amman";

				DateTime appointmentLocal;
				try
				{
					var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
					var utcTime = dto.AppointmentDate.Kind == DateTimeKind.Utc
						? dto.AppointmentDate
						: dto.AppointmentDate.ToUniversalTime();
					appointmentLocal = TimeZoneInfo.ConvertTimeFromUtc(utcTime, tz);
				}
				catch { appointmentLocal = dto.AppointmentDate; }

				var dayOfWeek = appointmentLocal.DayOfWeek;
				var timeOfDay = TimeOnly.FromTimeSpan(appointmentLocal.TimeOfDay);
				var dateOnly = DateOnly.FromDateTime(appointmentLocal);
				var clinicId = _clinicContext.ClinicId.Value;

				// 1 — تحقق أن العيادة مفتوحة (جدول الدوام)
				var clinicSchedule = await _db.ClinicSchedules
					.FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.DayOfWeek == dayOfWeek && s.IsActive);

				if (clinicSchedule == null)
					return BadRequest(Msg(lang, "العيادة مغلقة في هذا اليوم", "Clinic is closed on this day"));

				// 2 — تحقق من إجازة العيادة
				var clinicAbsent = await _db.Absences.AnyAsync(a =>
					a.ClinicId == clinicId &&
					a.DoctorId == null &&
					a.StartDate.Date <= appointmentLocal.Date &&
					a.EndDate.Date >= appointmentLocal.Date &&
					a.StartTime == null);

				if (clinicAbsent)
					return BadRequest(Msg(lang,
						"العيادة في إجازة في هذا اليوم",
						"Clinic is on holiday on this day"));

				// 3 — تحقق أن الطبيب يعمل (جدول الدوام)
				var doctorSchedule = await _db.DoctorSchedules
					.FirstOrDefaultAsync(s => s.DoctorId == dto.DoctorId && s.DayOfWeek == dayOfWeek && s.IsActive);

				if (doctorSchedule == null)
					return BadRequest(Msg(lang, "الطبيب لا يعمل في هذا اليوم", "Doctor does not work on this day"));

				// 4 — تحقق من إجازة الطبيب (يوم كامل)
				var doctorAbsent = await _db.Absences.AnyAsync(a =>
					a.ClinicId == clinicId &&
					a.DoctorId == dto.DoctorId &&
					a.StartDate.Date <= appointmentLocal.Date &&
					a.EndDate.Date >= appointmentLocal.Date &&
					a.StartTime == null);

				if (doctorAbsent)
					return BadRequest(Msg(lang,
						"الطبيب في إجازة في هذا اليوم",
						"Doctor is on leave on this day"));

				// 5 — تحقق من إجازة الطبيب (فترة محددة)
				var doctorPartialAbsent = await _db.Absences.AnyAsync(a =>
					a.ClinicId == clinicId &&
					a.DoctorId == dto.DoctorId &&
					a.StartDate.Date <= appointmentLocal.Date &&
					a.EndDate.Date >= appointmentLocal.Date &&
					a.StartTime != null &&
					a.StartTime <= timeOfDay &&
					a.EndTime >= timeOfDay);

				if (doctorPartialAbsent)
					return BadRequest(Msg(lang,
						"الطبيب غير متاح في هذا الوقت (اجتماع أو استراحة)",
						"Doctor is unavailable at this time (meeting or break)"));

				// 6 — تحقق أن الوقت ضمن دوام الطبيب
				if (timeOfDay < doctorSchedule.StartTime || timeOfDay >= doctorSchedule.EndTime)
					return BadRequest(Msg(lang,
						$"الوقت خارج دوام الطبيب ({doctorSchedule.StartTime} - {doctorSchedule.EndTime})",
						$"Time is outside doctor's working hours ({doctorSchedule.StartTime} - {doctorSchedule.EndTime})"));

				// 7 — تحقق أن الموعد غير محجوز مسبقاً
				var slotEnd = dto.AppointmentDate.AddMinutes(doctorSchedule.SlotDuration);
				var isConflict = await _db.Appointments.AnyAsync(a =>
					a.DoctorId == dto.DoctorId
					&& !a.IsDeleted
					&& a.Status != "cancelled"
					&& a.AppointmentDate < slotEnd
					&& a.AppointmentDate.AddMinutes(doctorSchedule.SlotDuration) > dto.AppointmentDate);

				if (isConflict)
					return BadRequest(Msg(lang,
						"هذا الموعد محجوز مسبقاً — اختر وقتاً آخر",
						"This slot is already booked — please choose another time"));

				// 8 — تحديد السعر تلقائياً (يراعي: سعر خاص للطبيب بهذا القالب ← سعر القالب العام ← دوام الطبيب كـ fallback)
				var hasVisited = await _db.Appointments.AnyAsync(a =>
					a.PatientId == dto.PatientId
					&& a.DoctorId == dto.DoctorId
					&& !a.IsDeleted
					&& a.Status == "completed");

				if (dto.Price == null)
				{
					dto.Price = await ResolveVisitPrice(dto.DoctorId.Value, dto.TemplateId, !hasVisited, doctorSchedule);
				}

				// نحتفظ بمدة الفترة لاستخدامها بالفحص الأخير قبل الحفظ
				slotDurationForFinalCheck = doctorSchedule.SlotDuration;
			}

			if (dto.AppointmentDate <= DateTime.UtcNow)
				return BadRequest(Msg(lang,
					"تاريخ الموعد يجب أن يكون في المستقبل",
					"Appointment date must be in the future"));

			// ✅ فحص أخير للتعارض مباشرة قبل الحفظ — يقلل احتمال الحجز المزدوج
			if (dto.DoctorId.HasValue && slotDurationForFinalCheck.HasValue)
			{
				var finalSlotEnd = dto.AppointmentDate.AddMinutes(slotDurationForFinalCheck.Value);
				var stillConflict = await _db.Appointments.AnyAsync(a =>
					a.DoctorId == dto.DoctorId
					&& !a.IsDeleted
					&& a.Status != "cancelled"
					&& a.AppointmentDate < finalSlotEnd
					&& a.AppointmentDate.AddMinutes(slotDurationForFinalCheck.Value) > dto.AppointmentDate);

				if (stillConflict)
					return BadRequest(Msg(lang,
						"هذا الموعد حُجز للتو من مستخدم آخر — اختر وقتاً آخر",
						"This slot was just booked by someone else — please choose another time"));
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
				TemplateId = dto.TemplateId,   // ✅ قالب الزيارة (كشف/مراجعة/استشارة/متابعة أو مخصص)
				Status = "scheduled",
				Notes = dto.Notes,
				Notes2 = dto.Notes2,
				Notes3 = dto.Notes3,
			};

			_db.Appointments.Add(appointment);
			await _db.SaveChangesAsync();

			await _db.Entry(appointment).Reference(a => a.Patient).LoadAsync();
			if (appointment.DoctorId.HasValue)
				await _db.Entry(appointment).Reference(a => a.Doctor).LoadAsync();

			return CreatedAtAction(nameof(GetById), new { id = appointment.Id }, ToResponse(appointment));
		}
		// PUT: api/appointments/{id}
		// PUT: api/appointments/{id}
		[HttpPut("{id}")]
		public async Task<ActionResult<AppointmentResponseDto>> Update(Guid id, [FromBody] UpdateAppointmentDto dto)
		{
			var lang = "ar"; // عدّلها إذا الـ DTO عندك فيه Lang زي Create

			if (!_clinicContext.HasPermission("appointments.edit")) return Forbid();

			var appointment = await _db.Appointments
				.Include(a => a.Patient)
				.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);

			if (appointment == null) return NotFound();
			if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId) return Forbid();

			if (_clinicContext.Role == "Doctor")
			{
				var myDoctor = await _db.Doctors.FirstOrDefaultAsync(d =>
					d.UserId == _clinicContext.UserId && d.ClinicId == _clinicContext.ClinicId && !d.IsDeleted);
				if (myDoctor == null || appointment.DoctorId != myDoctor.Id) return Forbid();
			}

			if (dto.PatientId != appointment.PatientId)
			{
				var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == dto.PatientId && !p.IsDeleted);
				if (patient == null) return BadRequest("المريض غير موجود");
				if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId) return Forbid();
				appointment.PatientId = dto.PatientId;
			}

			var validStatuses = new[] { "scheduled", "confirmed", "completed", "cancelled" };
			if (!string.IsNullOrEmpty(dto.Status) && !validStatuses.Contains(dto.Status))
				return BadRequest("Status يجب أن يكون: scheduled أو confirmed أو completed أو cancelled");

			// ✅ هل تغيّر الوقت أو الطبيب؟ لو نعم، لازم نعيد فحص كل قواعد الحجز
			var newDate = dto.AppointmentDate ?? appointment.AppointmentDate;
			var newDoctorId = dto.DoctorId;
			var scheduleChanged = newDate != appointment.AppointmentDate || newDoctorId != appointment.DoctorId;

			if (scheduleChanged && newDoctorId.HasValue)
			{
				var clinic = await _db.Clinics.FindAsync(appointment.ClinicId);
				var tzId = clinic?.TimeZone ?? "Asia/Amman";

				DateTime appointmentLocal;
				try
				{
					var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
					var utcTime = newDate.Kind == DateTimeKind.Utc ? newDate : newDate.ToUniversalTime();
					appointmentLocal = TimeZoneInfo.ConvertTimeFromUtc(utcTime, tz);
				}
				catch { appointmentLocal = newDate; }

				var dayOfWeek = appointmentLocal.DayOfWeek;
				var timeOfDay = TimeOnly.FromTimeSpan(appointmentLocal.TimeOfDay);
				var clinicId = appointment.ClinicId;

				// 1 — العيادة مفتوحة؟
				var clinicSchedule = await _db.ClinicSchedules
					.FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.DayOfWeek == dayOfWeek && s.IsActive);
				if (clinicSchedule == null)
					return BadRequest(Msg(lang, "العيادة مغلقة في هذا اليوم", "Clinic is closed on this day"));

				// 2 — إجازة العيادة؟
				var clinicAbsent = await _db.Absences.AnyAsync(a =>
					a.ClinicId == clinicId && a.DoctorId == null &&
					a.StartDate.Date <= appointmentLocal.Date && a.EndDate.Date >= appointmentLocal.Date &&
					a.StartTime == null);
				if (clinicAbsent)
					return BadRequest(Msg(lang, "العيادة في إجازة في هذا اليوم", "Clinic is on holiday on this day"));

				// 3 — دوام الطبيب
				var doctorSchedule = await _db.DoctorSchedules
					.FirstOrDefaultAsync(s => s.DoctorId == newDoctorId && s.DayOfWeek == dayOfWeek && s.IsActive);
				if (doctorSchedule == null)
					return BadRequest(Msg(lang, "الطبيب لا يعمل في هذا اليوم", "Doctor does not work on this day"));

				// 4 — إجازة الطبيب (يوم كامل)
				var doctorAbsent = await _db.Absences.AnyAsync(a =>
					a.ClinicId == clinicId && a.DoctorId == newDoctorId &&
					a.StartDate.Date <= appointmentLocal.Date && a.EndDate.Date >= appointmentLocal.Date &&
					a.StartTime == null);
				if (doctorAbsent)
					return BadRequest(Msg(lang, "الطبيب في إجازة في هذا اليوم", "Doctor is on leave on this day"));

				// 5 — إجازة الطبيب (فترة محددة)
				var doctorPartialAbsent = await _db.Absences.AnyAsync(a =>
					a.ClinicId == clinicId && a.DoctorId == newDoctorId &&
					a.StartDate.Date <= appointmentLocal.Date && a.EndDate.Date >= appointmentLocal.Date &&
					a.StartTime != null && a.StartTime <= timeOfDay && a.EndTime >= timeOfDay);
				if (doctorPartialAbsent)
					return BadRequest(Msg(lang,
						"الطبيب غير متاح في هذا الوقت (اجتماع أو استراحة)",
						"Doctor is unavailable at this time (meeting or break)"));

				// 6 — الوقت ضمن دوام الطبيب؟
				if (timeOfDay < doctorSchedule.StartTime || timeOfDay >= doctorSchedule.EndTime)
					return BadRequest(Msg(lang,
						$"الوقت خارج دوام الطبيب ({doctorSchedule.StartTime} - {doctorSchedule.EndTime})",
						$"Time is outside doctor's working hours ({doctorSchedule.StartTime} - {doctorSchedule.EndTime})"));

				// 7 — تعارض مع مواعيد ثانية (نستثني هذا الموعد نفسه من الفحص)
				var slotEnd = newDate.AddMinutes(doctorSchedule.SlotDuration);
				var isConflict = await _db.Appointments.AnyAsync(a =>
					a.Id != appointment.Id &&
					a.DoctorId == newDoctorId &&
					!a.IsDeleted &&
					a.Status != "cancelled" &&
					a.AppointmentDate < slotEnd &&
					a.AppointmentDate.AddMinutes(doctorSchedule.SlotDuration) > newDate);

				if (isConflict)
					return BadRequest(Msg(lang,
						"هذا الموعد محجوز مسبقاً — اختر وقتاً آخر",
						"This slot is already booked — please choose another time"));
			}

			if (dto.AppointmentDate.HasValue) appointment.AppointmentDate = dto.AppointmentDate.Value;
			appointment.DoctorId = dto.DoctorId;
			appointment.Type = dto.Type;
			appointment.Price = dto.Price;
			appointment.Status = dto.Status ?? appointment.Status;
			appointment.Notes = dto.Notes;
			appointment.Notes2 = dto.Notes2;
			appointment.Notes3 = dto.Notes3;

			await _db.SaveChangesAsync();
			await _db.Entry(appointment).Reference(a => a.Patient).LoadAsync();
			return Ok(ToResponse(appointment));
		}

		// DELETE: api/appointments/{id}
		[HttpDelete("{id}")]
        public async Task<ActionResult> Delete(Guid id)
        {
            if (!_clinicContext.HasPermission("appointments.delete")) return Forbid();
            var appointment = await _db.Appointments.FindAsync(id);
            if (appointment == null || appointment.IsDeleted ) return NotFound();
            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId) return Forbid();
            appointment.IsDeleted  = true;
            await _db.SaveChangesAsync();
            return NoContent();
        }

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
            if (appointment == null || appointment.IsDeleted ) return NotFound();
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
                .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted );
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
                    && a.Status == "completed");

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
                    && !a.IsDeleted  && a.Status != "cancelled"
                    && a.AppointmentDate <= now.AddMinutes(30) && a.AppointmentDate >= now.AddMinutes(-30))
                .Include(a => a.Patient)
                .FirstOrDefaultAsync();

            var queueCount = await _db.QueueEntries
                .CountAsync(q => q.DoctorId == doctorId && q.ClinicId == _clinicContext.ClinicId
                    && q.Date == today && !q.IsDeleted  && (q.Status == "waiting" || q.Status == "called"));

            var nextAppointment = await _db.Appointments
                .Where(a => a.DoctorId == doctorId && a.ClinicId == _clinicContext.ClinicId
                    && !a.IsDeleted  && a.Status == "scheduled" && a.AppointmentDate > now)
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
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult> SeedDefaultRoles(Guid clinicId)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null) return NotFound("العيادة غير موجودة");

            var added = await _roleSeedingService.SeedDefaultRoles(clinicId);

            return Ok(new { message = "تم إنشاء الأدوار الأساسية بنجاح" });
        }
 

        // ═══════════════════════════════════════
        // ✅ دوال حل السعر والحصة — الأولوية:
        // 1) استثناء خاص بالطبيب لهذا القالب تحديداً
        // 2) سعر/حصة القالب العام (للسعر فقط) أو الإعداد العام للطبيب (للحصة فقط)
        // 3) fallback على دوام الطبيب الافتراضي (للسعر فقط — الحصة بدون fallback، يعني بدون نظام عمولة)
        // ═══════════════════════════════════════

        private async Task<decimal?> ResolveVisitPrice(Guid doctorId, Guid? templateId, bool isFirstVisit, DoctorSchedule doctorSchedule)
        {
            if (templateId.HasValue)
            {
                // 1 — سعر خاص بالطبيب لهذا القالب بالذات (الأكثر تحديداً)
                var doctorSetting = await _db.DoctorTemplateSettings
                    .FirstOrDefaultAsync(s => s.DoctorId == doctorId && s.TemplateId == templateId && s.IsActive);

                if (doctorSetting != null)
                {
                    var customPrice = isFirstVisit ? doctorSetting.FirstVisitPrice : doctorSetting.FollowUpPrice;
                    if (customPrice.HasValue) return customPrice;
                }

                // 2 — الإعداد العام للطبيب (TemplateId = null) — سعره الشخصي الافتراضي،
                // يطبّق على أي قالب ما له استثناء خاص بالخطوة السابقة
                var doctorGeneralSetting = await _db.DoctorTemplateSettings
                    .FirstOrDefaultAsync(s => s.DoctorId == doctorId && s.TemplateId == null && s.IsActive);

                if (doctorGeneralSetting != null)
                {
                    var generalPrice = isFirstVisit ? doctorGeneralSetting.FirstVisitPrice : doctorGeneralSetting.FollowUpPrice;
                    if (generalPrice.HasValue) return generalPrice;
                }

                // 3 — سعر القالب العام (لو الطبيب ما له أي سعر شخصي إطلاقاً)
                var template = await _db.TreatmentPlanTemplates.FindAsync(templateId.Value);
                if (template != null)
                {
                    var templatePrice = isFirstVisit ? template.FirstVisitPrice : template.FollowUpPrice;
                    if (templatePrice.HasValue) return templatePrice;
                }
            }

            // 4 — fallback: سعر دوام الطبيب الافتراضي (النظام القديم، يبقى شغّال للمواعيد بدون قالب)
            return isFirstVisit ? doctorSchedule.FirstVisitPrice : doctorSchedule.FollowUpPrice;
        }

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
}