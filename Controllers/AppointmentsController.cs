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
        private readonly IClinicContext _clinicContext; // ✅ أضف

        public AppointmentsController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext; // ✅ أضف
        }

        // GET: api/appointments
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AppointmentResponseDto>>> GetAll()
        {
            var query = _db.Appointments.Where(a => !a.isdeleted);

            if (!_clinicContext.IsCompanyStaff)
            {
                if (_clinicContext.ClinicId == null)
                    return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

                query = query.Where(a => a.ClinicId == _clinicContext.ClinicId);

                // ✅ الطبيب يرى مواعيده فقط
                // ✅ استخدم UserId للبحث عن بطاقة الطبيب أولاً
                if (_clinicContext.Role == "Doctor")
                {
                    var doctorRecord = await _db.Doctors
                        .FirstOrDefaultAsync(d => d.UserId == _clinicContext.UserId
                            && d.ClinicId == _clinicContext.ClinicId
                            && !d.isdeleted);

                    if (doctorRecord != null)
                        query = query.Where(a => a.DoctorId == doctorRecord.Id);
                    else
                        return Ok(new List<AppointmentResponseDto>());
                }
            }

            var appointments = await query
                .OrderByDescending(a => a.AppointmentDate)
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .ToListAsync();

            return Ok(appointments.Select(a => ToResponse(a)).ToList());
        }
        // GET: api/appointments/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<AppointmentResponseDto>> GetById(Guid id)
        {
            var appointment = await _db.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id && !a.isdeleted);

            if (appointment == null)
                return NotFound();

            // ✅ تحقق من العيادة
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
                .Where(a => !a.isdeleted
                    && a.AppointmentDate >= today
                    && a.AppointmentDate < tomorrow
                    && a.DoctorId != null);

            if (!_clinicContext.IsCompanyStaff)
                query = query.Where(a => a.ClinicId == _clinicContext.ClinicId);

            var appointments = await query
                .Include(a => a.Doctor)
                .Include(a => a.Patient)
                .ToListAsync();

            // تجميع حسب الطبيب
            var result = appointments
                .GroupBy(a => a.DoctorId)
                .Select(g => new {
                    doctorId = g.Key,
                    doctorName = g.First().Doctor?.FullName ?? "—",
                    doctorSpecialty = g.First().Doctor?.Specialty ?? "",
                    appointmentCount = g.Count(),
                    appointments = g.Select(a => new {
                        id = a.Id,
                        patientName = a.Patient.FullName,
                        time = a.AppointmentDate
                    }).ToList()
                })
                .OrderByDescending(d => d.appointmentCount)
                .ToList();

            return Ok(result);
        }
        // GET: api/appointments/patient/{patientId}
        [HttpGet("patient/{patientId}")]
        public async Task<ActionResult<IEnumerable<AppointmentResponseDto>>> GetByPatient(Guid patientId)
        {
            // ✅ تحقق أن المريض ينتمي لنفس العيادة
            var patient = await _db.Patients
                .FirstOrDefaultAsync(p => p.Id == patientId && !p.isdeleted);

            if (patient == null)
                return NotFound("المريض غير موجود");

            if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            var query = _db.Appointments
                .Where(a => a.PatientId == patientId && !a.isdeleted);

            // ✅ فلتر إضافي للعيادة
            if (!_clinicContext.IsCompanyStaff)
                query = query.Where(a => a.ClinicId == _clinicContext.ClinicId);

            var appointments = await query
                .OrderByDescending(a => a.AppointmentDate)
                .Include(a => a.Patient)
                .ToListAsync();

            var result = appointments.Select(a => ToResponse(a)).ToList();
            return Ok(result);
        }

        // POST: api/appointments
        [HttpPost]
        public async Task<ActionResult<AppointmentResponseDto>> Create([FromBody] CreateAppointmentDto dto)
        {
            if (!_clinicContext.HasPermission("appointments.create"))
                return Forbid();

            if (_clinicContext.IsSuperAdmin)
                return BadRequest("SuperAdmin لا يستطيع إضافة مواعيد مباشرة");

            if (_clinicContext.ClinicId == null)
                return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

            var patient = await _db.Patients
                .FirstOrDefaultAsync(p => p.Id == dto.PatientId && !p.isdeleted);

            if (patient == null)
                return BadRequest("المريض غير موجود");

            if (patient.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            // ✅ التحقق من جدول الدوام
            if (dto.DoctorId.HasValue)
            {
                var dayOfWeek = dto.AppointmentDate.DayOfWeek;
                var timeOfDay = TimeOnly.FromDateTime(dto.AppointmentDate);
                var clinicId = _clinicContext.ClinicId.Value;

                // 1 — تحقق أن العيادة مفتوحة
                var clinicSchedule = await _db.ClinicSchedules
                    .FirstOrDefaultAsync(s => s.ClinicId == clinicId
                        && s.DayOfWeek == dayOfWeek
                        && s.IsActive);

                if (clinicSchedule == null)
                    return BadRequest("العيادة مغلقة في هذا اليوم");

                // 2 — تحقق أن الطبيب يعمل
                var doctorSchedule = await _db.DoctorSchedules
                    .FirstOrDefaultAsync(s => s.DoctorId == dto.DoctorId
                        && s.DayOfWeek == dayOfWeek
                        && s.IsActive);

                if (doctorSchedule == null)
                    return BadRequest("الطبيب لا يعمل في هذا اليوم");

                // 3 — تحقق أن الوقت ضمن دوام الطبيب
                if (timeOfDay < doctorSchedule.StartTime || timeOfDay >= doctorSchedule.EndTime)
                    return BadRequest($"الوقت خارج دوام الطبيب ({doctorSchedule.StartTime} - {doctorSchedule.EndTime})");

                // 4 — تحقق أن الموعد غير محجوز مسبقاً
                var slotEnd = dto.AppointmentDate.AddMinutes(doctorSchedule.SlotDuration);

                var isConflict = await _db.Appointments
                    .AnyAsync(a => a.DoctorId == dto.DoctorId
                        && !a.isdeleted
                        && a.Status != "cancelled"
                        && a.AppointmentDate < slotEnd
                        && a.AppointmentDate.AddMinutes(doctorSchedule.SlotDuration) > dto.AppointmentDate);

                if (isConflict)
                    return BadRequest("هذا الموعد محجوز مسبقاً — اختر وقتاً آخر");

                // 5 — تحديد السعر تلقائياً
                if (dto.Price == null)
                {
                    // هل زار هذا المريض الطبيب من قبل؟
                    var hasVisited = await _db.Appointments
                        .AnyAsync(a => a.PatientId == dto.PatientId
                            && a.DoctorId == dto.DoctorId
                            && !a.isdeleted
                            && a.Status == "completed");

                    dto.Price = hasVisited
                        ? doctorSchedule.FollowUpPrice
                        : doctorSchedule.FirstVisitPrice;
                }
            }

            if (dto.AppointmentDate <= DateTime.Now)
                return BadRequest("تاريخ الموعد يجب أن يكون في المستقبل");

            var appointment = new Appointment
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                isdeleted = false,
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

            await _db.Entry(appointment).Reference(a => a.Patient).LoadAsync();
            if (appointment.DoctorId.HasValue)
                await _db.Entry(appointment).Reference(a => a.Doctor).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = appointment.Id }, ToResponse(appointment));
        }

        // PUT: api/appointments/{id}
        [HttpPut("{id}")]
        public async Task<ActionResult<AppointmentResponseDto>> Update(Guid id, [FromBody] UpdateAppointmentDto dto)
        {
            if (!_clinicContext.HasPermission("appointments.edit"))
                return Forbid();

            // ✅ جلب الموعد أولاً
            var appointment = await _db.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id && !a.isdeleted);

            if (appointment == null)
                return NotFound();

            // ✅ تحقق من العيادة
            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            // ✅ Doctor يعدّل مواعيده فقط — بعد جلب الموعد
            // ✅ يجب المقارنة بـ Doctor.Id وليس UserId
            if (_clinicContext.Role == "Doctor")
            {
                var myDoctor = await _db.Doctors
                    .FirstOrDefaultAsync(d => d.UserId == _clinicContext.UserId
                        && d.ClinicId == _clinicContext.ClinicId
                        && !d.isdeleted);

                if (myDoctor == null || appointment.DoctorId != myDoctor.Id)
                    return Forbid();
            }

            if (dto.PatientId != appointment.PatientId)
            {
                var patient = await _db.Patients
                    .FirstOrDefaultAsync(p => p.Id == dto.PatientId && !p.isdeleted);

                if (patient == null)
                    return BadRequest("المريض غير موجود");

                if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId)
                    return Forbid();

                appointment.PatientId = dto.PatientId;
            }

            var validStatuses = new[] { "scheduled", "confirmed", "completed", "cancelled" };
            if (!string.IsNullOrEmpty(dto.Status) && !validStatuses.Contains(dto.Status))
                return BadRequest("Status يجب أن يكون: scheduled أو confirmed أو completed أو cancelled");

            if (dto.AppointmentDate.HasValue)
                appointment.AppointmentDate = dto.AppointmentDate.Value;

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
            if (!_clinicContext.HasPermission("appointments.delete"))
                return Forbid();

            var appointment = await _db.Appointments.FindAsync(id);

            if (appointment == null || appointment.isdeleted)
                return NotFound();

            // ✅ تحقق من العيادة
            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            appointment.isdeleted = true;
            await _db.SaveChangesAsync();
            return NoContent();
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
            Type = a.Type,
            Price = a.Price,
            Status = a.Status,
            Notes = a.Notes,
            Notes2 = a.Notes2,
            Notes3 = a.Notes3,
            CreatedAt = a.CreatedAt
        };
    }
}