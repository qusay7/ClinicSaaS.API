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
    public class VisitNotesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public VisitNotesController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        // GET: api/visitnotes/patient/{patientId}
        // جلب كل زيارات مريض معين — السجل المرضي الكامل (يشمل نوع الزيارة والمرفقات)
        [HttpGet("patient/{patientId}")]
        public async Task<ActionResult> GetByPatient(Guid patientId)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var notes = await _db.VisitNotes
                .Where(v => v.PatientId == patientId
                    && v.ClinicId == _clinicContext.ClinicId
                    && !v.IsDeleted)
                .Include(v => v.Doctor)
                .Include(v => v.Appointment)
                .Include(v => v.QueueEntry)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();

            // ✅ نجيب كل مرفقات المريض بضربة وحدة، ونربطها بالزيارة المناسبة حسب AppointmentId
            var attachments = await _db.Attachments
                .Where(a => a.PatientId == patientId && a.ClinicId == _clinicContext.ClinicId)
                .Select(a => new { a.Id, a.FileName, a.Category, a.AppointmentId, isImage = a.FileType.StartsWith("image/") })
                .ToListAsync();

            return Ok(notes.Select(v => new
            {
                v.Id,
                appointmentId = v.AppointmentId,
                v.Diagnosis,
                v.Prescription,
                v.Tests,
                v.Notes,
                v.NextVisitDate,
                v.Cost,
                v.CreatedAt,
                doctorName = v.Doctor?.FullName,
                appointmentDate = v.Appointment?.AppointmentDate,
                visitType = v.Appointment?.Type,
                source = v.AppointmentId != null ? "appointment" : "queue",
                // ✅ الشرط جوا Where بدل ternary بنوعين مختلفين — يرجّع قائمة فاضية تلقائياً
                // لو الزيارة بدون AppointmentId، بدون تعارض أنواع بـ C#
                attachments = attachments
                    .Where(a => v.AppointmentId != null && a.AppointmentId == v.AppointmentId)
                    .Select(a => new { a.Id, a.FileName, a.Category, a.isImage })
                    .ToList(),
            }));
        }

        // GET: api/visitnotes/appointment/{appointmentId}
        // جلب ملاحظات موعد معين
        [HttpGet("appointment/{appointmentId}")]
        public async Task<ActionResult> GetByAppointment(Guid appointmentId)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var note = await _db.VisitNotes
                .Where(v => v.AppointmentId == appointmentId
                    && v.ClinicId == _clinicContext.ClinicId
                    && !v.IsDeleted)
                .Include(v => v.Doctor)
                .FirstOrDefaultAsync();

            if (note == null) return Ok(null);

            return Ok(new
            {
                note.Id,
                note.Diagnosis,
                note.Prescription,
                note.Tests,
                note.Notes,
                note.NextVisitDate,
                note.Cost,
                note.CreatedAt,
                doctorName = note.Doctor?.FullName,
            });
        }

        // POST: api/visitnotes
        // إضافة ملاحظة زيارة
        // POST: api/visitnotes
        // إضافة ملاحظة زيارة
        [HttpPost]
        public async Task<ActionResult> Create([FromBody] CreateVisitNoteDto dto)
        {
            if (!_clinicContext.HasPermission("visitnotes.create")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            // ✅ تحقق أن المريض ينتمي لنفس العيادة
            var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == dto.PatientId && !p.IsDeleted);
            if (patient == null) return BadRequest("المريض غير موجود");
            if (patient.ClinicId != _clinicContext.ClinicId) return Forbid();

            var note = new VisitNote
            {
                Id = Guid.NewGuid(),
                ClinicId = _clinicContext.ClinicId.Value,
                PatientId = dto.PatientId,
                AppointmentId = dto.AppointmentId,
                QueueEntryId = dto.QueueEntryId,
                DoctorId = dto.DoctorId,
                Diagnosis = dto.Diagnosis,
                Prescription = dto.Prescription,
                Tests = dto.Tests,
                Notes = dto.Notes,
                NextVisitDate = dto.NextVisitDate,
                Cost = dto.Cost,
                CreatedAt = DateTime.UtcNow,
            };

            _db.VisitNotes.Add(note);
            await _db.SaveChangesAsync();

            return Ok(new { note.Id, message = "تم حفظ ملاحظات الزيارة" });
        }

        // PUT: api/visitnotes/{id}
        // تعديل ملاحظة زيارة
        // PUT: api/visitnotes/{id}
        // تعديل ملاحظة زيارة
        [HttpPut("{id}")]
        public async Task<ActionResult> Update(Guid id, [FromBody] CreateVisitNoteDto dto)
        {
            if (!_clinicContext.HasPermission("visitnotes.edit")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var note = await _db.VisitNotes
                .FirstOrDefaultAsync(v => v.Id == id
                    && v.ClinicId == _clinicContext.ClinicId
                    && !v.IsDeleted);

            if (note == null) return NotFound();

            note.Diagnosis = dto.Diagnosis;
            note.Prescription = dto.Prescription;
            note.Tests = dto.Tests;
            note.Notes = dto.Notes;
            note.NextVisitDate = dto.NextVisitDate;
            note.Cost = dto.Cost;

            await _db.SaveChangesAsync();

            return Ok(new { message = "تم تحديث ملاحظات الزيارة" });
        }
    }

    public class CreateVisitNoteDto
    {
        public Guid PatientId { get; set; }
        public Guid? AppointmentId { get; set; }
        public Guid? QueueEntryId { get; set; }
        public Guid? DoctorId { get; set; }
        public string? Diagnosis { get; set; }
        public string? Prescription { get; set; }
        public string? Tests { get; set; }
        public string? Notes { get; set; }
        public DateTime? NextVisitDate { get; set; }
        public decimal? Cost { get; set; }
    }
}