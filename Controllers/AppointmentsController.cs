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
            // ✅ فلتر العيادة
            var query = _db.Appointments.Where(a => !a.isdeleted);

            if (!_clinicContext.IsCompanyStaff)
            {
                if (_clinicContext.ClinicId == null)
                    return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

                query = query.Where(a => a.ClinicId == _clinicContext.ClinicId);
            }

            var appointments = await query
                .OrderByDescending(a => a.AppointmentDate)
                .Include(a => a.Patient)
                    .Include(a => a.Doctor)  // ✅ أضف هذا

                .ToListAsync();

            var result = appointments.Select(a => ToResponse(a)).ToList();
            return Ok(result);
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
            // ✅ SuperAdmin لا يستطيع إضافة مواعيد مباشرة
            if (_clinicContext.IsSuperAdmin)
                return BadRequest("SuperAdmin لا يستطيع إضافة مواعيد");

            if (_clinicContext.ClinicId == null)
                return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

            // ✅ تحقق أن المريض ينتمي لنفس العيادة
            var patient = await _db.Patients
                .FirstOrDefaultAsync(p => p.Id == dto.PatientId && !p.isdeleted);

            if (patient == null)
                return BadRequest("المريض غير موجود");

            if (patient.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            if (dto.AppointmentDate <= DateTime.UtcNow)
                return BadRequest("تاريخ الموعد يجب أن يكون في المستقبل");

            var appointment = new Appointment
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                isdeleted = false,
                ClinicId = _clinicContext.ClinicId.Value,
                PatientId = dto.PatientId,
                DoctorId = dto.DoctorId, // ✅ بدلاً من DoctorName
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
            return CreatedAtAction(nameof(GetById), new { id = appointment.Id }, ToResponse(appointment));
        }

        // PUT: api/appointments/{id}
        [HttpPut("{id}")]
        public async Task<ActionResult<AppointmentResponseDto>> Update(Guid id, [FromBody] UpdateAppointmentDto dto)
        {
            var appointment = await _db.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id && !a.isdeleted);

            if (appointment == null)
                return NotFound();

            // ✅ تحقق من العيادة
            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            if (dto.PatientId != appointment.PatientId)
            {
                var patient = await _db.Patients
                    .FirstOrDefaultAsync(p => p.Id == dto.PatientId && !p.isdeleted);

                if (patient == null)
                    return BadRequest("المريض غير موجود");

                // ✅ تحقق أن المريض الجديد ينتمي لنفس العيادة
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