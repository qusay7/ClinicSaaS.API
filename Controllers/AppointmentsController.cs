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

        public AppointmentsController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        // ✅ دالة مساعدة للرسائل ثنائية اللغة
        private static string Msg(string? lang, string ar, string en)
            => lang == "ar" ? ar : en;

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
            var patient = await _db.Patients
                .FirstOrDefaultAsync(p => p.Id == patientId && !p.isdeleted);

            if (patient == null)
                return NotFound("المريض غير موجود");

            if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            var query = _db.Appointments
                .Where(a => a.PatientId == patientId && !a.isdeleted);

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
            // ✅ اللغة من الـ DTO
            var lang = dto.Lang ?? "ar";

            if (!_clinicContext.HasPermission("appointments.create"))
                return Forbid();

            if (_clinicContext.IsSuperAdmin)
                return BadRequest(Msg(lang,
                    "SuperAdmin لا يستطيع إضافة مواعيد مباشرة",
                    "SuperAdmin cannot add appointments directly"));

            if (_clinicContext.ClinicId == null)
                return Unauthorized(Msg(lang,
                    "لا توجد عيادة مرتبطة بهذا المستخدم",
                    "No clinic associated with this user"));

            var patient = await _db.Patients
                .FirstOrDefaultAsync(p => p.Id == dto.PatientId && !p.isdeleted);

            if (patient == null)
                return BadRequest(Msg(lang, "المريض غير موجود", "Patient not found"));

            if (patient.ClinicId != _clinicContext.ClinicId)
                return Forbid();

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
                catch
                {
                    appointmentLocal = dto.AppointmentDate;
                }

                var dayOfWeek = appointmentLocal.DayOfWeek;
                var timeOfDay = TimeOnly.FromTimeSpan(appointmentLocal.TimeOfDay);
                var clinicId = _clinicContext.ClinicId.Value;

                // 1 — تحقق أن العيادة مفتوحة
                var clinicSchedule = await _db.ClinicSchedules
                    .FirstOrDefaultAsync(s => s.ClinicId == clinicId
                        && s.DayOfWeek == dayOfWeek
                        && s.IsActive);

                if (clinicSchedule == null)
                    return BadRequest(Msg(lang,
                        "العيادة مغلقة في هذا اليوم",
                        "Clinic is closed on this day"));

                // 2 — تحقق أن الطبيب يعمل
                var doctorSchedule = await _db.DoctorSchedules
                    .FirstOrDefaultAsync(s => s.DoctorId == dto.DoctorId
                        && s.DayOfWeek == dayOfWeek
                        && s.IsActive);

                if (doctorSchedule == null)
                    return BadRequest(Msg(lang,
                        "الطبيب لا يعمل في هذا اليوم",
                        "Doctor does not work on this day"));

                // 3 — تحقق أن الوقت ضمن دوام الطبيب
                if (timeOfDay < doctorSchedule.StartTime || timeOfDay >= doctorSchedule.EndTime)
                    return BadRequest(Msg(lang,
                        $"الوقت خارج دوام الطبيب ({doctorSchedule.StartTime} - {doctorSchedule.EndTime})",
                        $"Time is outside doctor's working hours ({doctorSchedule.StartTime} - {doctorSchedule.EndTime})"));

                // 4 — تحقق أن الموعد غير محجوز مسبقاً
                var slotEnd = dto.AppointmentDate.AddMinutes(doctorSchedule.SlotDuration);
                var isConflict = await _db.Appointments
                    .AnyAsync(a => a.DoctorId == dto.DoctorId
                        && !a.isdeleted
                        && a.Status != "cancelled"
                        && a.AppointmentDate < slotEnd
                        && a.AppointmentDate.AddMinutes(doctorSchedule.SlotDuration) > dto.AppointmentDate);

                if (isConflict)
                    return BadRequest(Msg(lang,
                        "هذا الموعد محجوز مسبقاً — اختر وقتاً آخر",
                        "This slot is already booked — please choose another time"));

                // 5 — تحديد السعر تلقائياً
                if (dto.Price == null)
                {
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

            if (dto.AppointmentDate <= DateTime.UtcNow)
                return BadRequest(Msg(lang,
                    "تاريخ الموعد يجب أن يكون في المستقبل",
                    "Appointment date must be in the future"));

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

            var appointment = await _db.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id && !a.isdeleted);

            if (appointment == null)
                return NotFound();

            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId)
                return Forbid();

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

            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            appointment.isdeleted = true;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST: api/appointments/seed-defaults/{clinicId}
        [HttpPost("seed-defaults/{clinicId}")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult> SeedDefaultRoles(Guid clinicId)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null) return NotFound("العيادة غير موجودة");

            var defaultRoles = new[]
            {
                new { Name = "ClinicAdmin",   Description = "مدير العيادة — صلاحيات كاملة" },
                new { Name = "Doctor",        Description = "طبيب — يرى مواعيده ومرضاه فقط" },
                new { Name = "Receptionist",  Description = "موظف استقبال — إدارة المرضى والمواعيد" },
                new { Name = "ClinicStaff",   Description = "موظف العيادة — صلاحيات محدودة" },
            };

            var allPermissions = await _db.Permissions.ToListAsync();

            foreach (var roleData in defaultRoles)
            {
                var exists = await _db.Roles
                    .AnyAsync(r => r.Name == roleData.Name && r.ClinicId == clinicId);
                if (exists) continue;

                var role = new Role
                {
                    Id = Guid.NewGuid(),
                    Name = roleData.Name,
                    Description = roleData.Description,
                    ClinicId = clinicId,
                    IsActive = true,
                    IsSystem = true,
                };
                _db.Roles.Add(role);
                await _db.SaveChangesAsync();

                var permissionsForRole = GetDefaultPermissionsForRole(roleData.Name, allPermissions);
                foreach (var perm in permissionsForRole)
                {
                    _db.RolePermissions.Add(new RolePermission
                    {
                        Id = Guid.NewGuid(),
                        RoleId = role.Id,
                        PermissionId = perm.Id,
                        ClinicId = null,
                    });
                }
                await _db.SaveChangesAsync();
            }

            return Ok(new { message = "تم إنشاء الأدوار الأساسية بنجاح" });
        }

        // GET: api/appointments/doctor-status/{doctorId}
        [HttpGet("doctor-status/{doctorId}")]
        public async Task<ActionResult> GetDoctorStatus(Guid doctorId)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var now = DateTime.UtcNow;
            var today = now.Date;

            var clinic = await _db.Clinics.FindAsync(_clinicContext.ClinicId);
            var tzId = clinic?.TimeZone ?? "Jordan Standard Time";
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
            catch { tz = TimeZoneInfo.Utc; }

            var currentAppointment = await _db.Appointments
                .Where(a => a.DoctorId == doctorId
                    && a.ClinicId == _clinicContext.ClinicId
                    && !a.isdeleted
                    && a.Status != "cancelled"
                    && a.AppointmentDate <= now.AddMinutes(30)
                    && a.AppointmentDate >= now.AddMinutes(-30))
                .Include(a => a.Patient)
                .FirstOrDefaultAsync();

            var queueCount = await _db.QueueEntries
                .CountAsync(q => q.DoctorId == doctorId
                    && q.ClinicId == _clinicContext.ClinicId
                    && q.Date == today
                    && !q.IsDeleted
                    && (q.Status == "waiting" || q.Status == "called"));

            var nextAppointment = await _db.Appointments
                .Where(a => a.DoctorId == doctorId
                    && a.ClinicId == _clinicContext.ClinicId
                    && !a.isdeleted
                    && a.Status == "scheduled"
                    && a.AppointmentDate > now)
                .OrderBy(a => a.AppointmentDate)
                .FirstOrDefaultAsync();

            var isBusy = currentAppointment != null;

            return Ok(new
            {
                isBusy,
                queueCount,
                currentPatient = currentAppointment?.Patient?.FullName,
                nextAppointmentTime = nextAppointment?.AppointmentDate.ToString("HH:mm"),
            });
        }

        // POST: api/appointments/{id}/checkin
        [HttpPost("{id}/checkin")]
        public async Task<ActionResult> CheckIn(Guid id)
        {
            var appointment = await _db.Appointments.FindAsync(id);
            if (appointment == null || appointment.isdeleted) return NotFound();
            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId) return Forbid();

            appointment.CheckInTime = DateTime.Now;
            appointment.Status = "confirmed";
            await _db.SaveChangesAsync();

            return Ok(new { checkInTime = appointment.CheckInTime, status = appointment.Status });
        }

        // POST: api/appointments/{id}/checkout
        [HttpPost("{id}/checkout")]
        public async Task<ActionResult> CheckOut(Guid id)
        {
            var appointment = await _db.Appointments.FindAsync(id);
            if (appointment == null || appointment.isdeleted) return NotFound();
            if (!_clinicContext.IsSuperAdmin && appointment.ClinicId != _clinicContext.ClinicId) return Forbid();

            if (appointment.CheckInTime == null)
                return BadRequest("لم يتم تسجيل الدخول بعد");

            appointment.CheckOutTime = DateTime.Now;
            appointment.Status = "completed";
            await _db.SaveChangesAsync();

            var duration = appointment.CheckOutTime - appointment.CheckInTime;
            return Ok(new
            {
                checkOutTime = appointment.CheckOutTime,
                status = appointment.Status,
                durationMinutes = (int)duration!.Value.TotalMinutes
            });
        }

        private static List<Permission> GetDefaultPermissionsForRole(string roleName, List<Permission> allPermissions)
        {
            var permMap = new Dictionary<string, string[]>
            {
                ["ClinicAdmin"] = new[]
                {
                    "patients.view", "patients.create", "patients.edit", "patients.delete",
                    "doctors.view", "doctors.create", "doctors.edit", "doctors.delete",
                    "appointments.view", "appointments.create", "appointments.edit", "appointments.delete",
                    "schedules.view", "schedules.manage",
                    "users.view", "users.create",
                    "departments.manage",
                    "settings.view", "settings.edit",
                    "reports.view",
                },
                ["Doctor"] = new[]
                {
                    "patients.view",
                    "appointments.view", "appointments.create", "appointments.edit",
                    "schedules.view",
                },
                ["Receptionist"] = new[]
                {
                    "patients.view", "patients.create", "patients.edit",
                    "appointments.view", "appointments.create", "appointments.edit",
                    "schedules.view",
                },
                ["ClinicStaff"] = new[]
                {
                    "patients.view",
                    "appointments.view",
                    "schedules.view",
                    "reports.view",
                },
            };

            if (!permMap.ContainsKey(roleName)) return new List<Permission>();

            return allPermissions
                .Where(p => permMap[roleName].Contains(p.Name))
                .ToList();
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
            CreatedAt = a.CreatedAt,
            CheckInTime = a.CheckInTime,
            CheckOutTime = a.CheckOutTime,
        };
    }
}