using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Patients;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PatientsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly SubscriptionService _subscriptionService;


        public PatientsController(ApplicationDbContext db, IClinicContext clinicContext, SubscriptionService subscriptionService)
        {
            _db = db;
            _clinicContext = clinicContext;
            _subscriptionService = subscriptionService;
        }

        // GET: api/patients
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PatientResponseDto>>> GetAll()
        {
            var query = _db.Patients.Where(p => !p.isdeleted);

            if (!_clinicContext.IsCompanyStaff)
            {
                if (_clinicContext.ClinicId == null)
                    return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

                // ✅ يرى مرضى عيادته فقط
                query = query.Where(p => p.ClinicId == _clinicContext.ClinicId);
            }

            var patients = await query
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            var result = patients.Select(p => ToResponse(p)).ToList();
            return Ok(result);
        }

        // GET: api/patients/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<PatientResponseDto>> GetById(Guid id)
        {
            var patient = await _db.Patients.FindAsync(id);

            if (patient == null || patient.isdeleted)
                return NotFound();

            // ✅ تحقق أن المريض ينتمي لنفس العيادة
            if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            return Ok(ToResponse(patient));
        }

        // POST: api/patients
        [HttpPost]
        public async Task<ActionResult<PatientResponseDto>> Create([FromBody] CreatePatientDto model)
        {
            // ✅ أضف هذا أولاً
            if (_clinicContext.IsSuperAdmin)
                return BadRequest("SuperAdmin لا يستطيع إضافة مرضى مباشرة");

            if (_clinicContext.ClinicId == null)
                return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

            var (canAdd, error) = await _subscriptionService.CanAddPatient(_clinicContext.ClinicId.Value);
            if (!canAdd) return BadRequest(error);

 

            if (string.IsNullOrWhiteSpace(model.FullName))
                return BadRequest("FullName is required");

            // ✅ رقم المريض خاص بكل عيادة
            var patientNumber = await _db.Patients
                .AnyAsync(p => p.ClinicId == _clinicContext.ClinicId)
                ? await _db.Patients
                    .Where(p => p.ClinicId == _clinicContext.ClinicId)
                    .MaxAsync(p => p.PatientNumber) + 1
                : 1_000_000;

            var patient = new Patient
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                stopped = false,
                isdeleted = false,
                ClinicId = _clinicContext.ClinicId ?? Guid.Empty, // ✅ ربط بالعيادة
                PatientNumber = patientNumber,
                FullName = model.FullName,
                DateOfBirth = model.DateOfBirth,
                Phone = model.Phone,
                Phone2 = model.Phone2,
                Gender = model.Gender,
                NationalId = model.NationalId,
                Notes = model.Notes,
                Notes2 = model.Notes2,
                Notes3 = model.Notes3,
                BloodType = model.BloodType,
                Address = model.Address,
                Email = model.Email,
                EmergencyContact = model.EmergencyContact,
                EmergencyPhone = model.EmergencyPhone,
                Allergies = model.Allergies,
                ChronicDiseases = model.ChronicDiseases,
                Occupation = model.Occupation,
                MaritalStatus = model.MaritalStatus,
             };

            _db.Patients.Add(patient);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = patient.Id }, ToResponse(patient));
        }

        // PUT: api/patients/{id}
        [HttpPut("{id}")]
        public async Task<ActionResult<PatientResponseDto>> Update(Guid id, [FromBody] UpdatePatientDto dto)
        {
            var patient = await _db.Patients.FindAsync(id);

            if (patient == null || patient.isdeleted)
                return NotFound();

            // ✅ تحقق أن المريض ينتمي لنفس العيادة
            if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            if (string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest("FullName is required");

            patient.FullName = dto.FullName;
            patient.DateOfBirth = dto.DateOfBirth;
            patient.Phone = dto.Phone;
            patient.Phone2 = dto.Phone2;
            patient.Gender = dto.Gender;
            patient.NationalId = dto.NationalId;
            patient.stopped = dto.stopped;
            patient.Notes = dto.Notes;
            patient.Notes2 = dto.Notes2;
            patient.Notes3 = dto.Notes3;
            patient.BloodType = dto.BloodType;
            patient.Address = dto.Address;
            patient.Email = dto.Email;
            patient.EmergencyContact = dto.EmergencyContact;
            patient.EmergencyPhone = dto.EmergencyPhone;
            patient.Allergies = dto.Allergies;
            patient.ChronicDiseases = dto.ChronicDiseases;
            patient.Occupation = dto.Occupation;
            patient.MaritalStatus = dto.MaritalStatus;

            await _db.SaveChangesAsync();
            return Ok(ToResponse(patient));
        }

        // DELETE: api/patients/{id}
        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(Guid id)
        {
            var patient = await _db.Patients.FindAsync(id);

            if (patient == null || patient.isdeleted)
                return NotFound();

            // ✅ تحقق أن المريض ينتمي لنفس العيادة
            if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            patient.isdeleted = true;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private static PatientResponseDto ToResponse(Patient p) => new PatientResponseDto
        {
            Id = p.Id,
            PatientNumber = p.PatientNumber,
            FullName = p.FullName,
            DateOfBirth = p.DateOfBirth,
            Phone = p.Phone,
            Phone2 = p.Phone2,
            Gender = p.Gender,
            NationalId = p.NationalId,
            CreatedAt = p.CreatedAt,
            stopped = p.stopped,
            Notes = p.Notes,
            Notes2 = p.Notes2,
            Notes3 = p.Notes3,
            BloodType = p.BloodType,
            Address = p.Address,
            Email = p.Email,
            EmergencyContact = p.EmergencyContact,
            EmergencyPhone = p.EmergencyPhone,
            Allergies = p.Allergies,
            ChronicDiseases = p.ChronicDiseases,
            Occupation = p.Occupation,
            MaritalStatus = p.MaritalStatus
        };
    }
}