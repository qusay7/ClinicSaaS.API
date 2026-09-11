using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Patients;
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
    public class PatientsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly SubscriptionService _subscriptionService;
        private readonly IPdfExportService _pdfExport;
        private readonly IExcelExportService _excelExport;
        private readonly IWebHostEnvironment _env;
        private readonly INotificationService _notificationService;


        public PatientsController(ApplicationDbContext db, IClinicContext clinicContext, SubscriptionService subscriptionService,
            IPdfExportService pdfExport, IExcelExportService excelExport, IWebHostEnvironment env, INotificationService notificationService)
        {
            _db = db;
            _clinicContext = clinicContext;
            _subscriptionService = subscriptionService;
            _pdfExport = pdfExport;
            _excelExport = excelExport;
            _env = env;
            _notificationService = notificationService;
        }

        // ✅ يحوّل رابط الشعار النسبي المخزّن لمسار فعلي على القرص
        private string? ResolveLogoPath(string? logoUrl)
        {
            if (string.IsNullOrEmpty(logoUrl)) return null;
            var cleanPath = logoUrl.Split('?')[0].TrimStart('/');
            var fullPath = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), cleanPath.Replace("logos/", "logos" + Path.DirectorySeparatorChar));
            return System.IO.File.Exists(fullPath) ? fullPath : null;
        }

        // GET: api/patients
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PatientResponseDto>>> GetAll()
        {
            var query = _db.Patients.Where(p => !p.IsDeleted);

            if (!_clinicContext.IsCompanyStaff)
            {
                if (_clinicContext.ClinicId == null)
                    return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

                query = query.Where(p => p.ClinicId == _clinicContext.ClinicId);

                // ✅ الطبيب يرى فقط مرضى مواعيده
                // ✅ الصحيح — يبحث بـ Doctor.UserId
                if (_clinicContext.Role == "Doctor")
                {
                    var doctorRecord = await _db.Doctors
                        .FirstOrDefaultAsync(d => d.UserId == _clinicContext.UserId
                            && d.ClinicId == _clinicContext.ClinicId
                            && !d.IsDeleted);

                    if (doctorRecord != null)
                    {
                        var patientIds = await _db.Appointments
                            .Where(a => a.DoctorId == doctorRecord.Id  // ✅ doctorRecord.Id وليس UserId
                                && a.ClinicId == _clinicContext.ClinicId
                                && !a.IsDeleted)
                            .Select(a => a.PatientId)
                            .Distinct()
                            .ToListAsync();

                        query = query.Where(p => patientIds.Contains(p.Id));
                    }
                    else
                    {
                        return Ok(new List<PatientResponseDto>());
                    }
                }
            }

            var patients = await query
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            return Ok(patients.Select(p => ToResponse(p)).ToList());
        }

        // ✅ GET: api/patients/export?format=pdf|excel
        [HttpGet("export")]
        public async Task<ActionResult> Export([FromQuery] string format = "pdf", [FromQuery] string lang = "ar")
        {
            var isRtl = lang == "ar";
            var query = _db.Patients.Where(p => !p.IsDeleted);

            if (!_clinicContext.IsCompanyStaff)
            {
                if (_clinicContext.ClinicId == null) return Unauthorized();
                query = query.Where(p => p.ClinicId == _clinicContext.ClinicId);

                if (_clinicContext.Role == "Doctor")
                {
                    var doctorRecord = await _db.Doctors
                        .FirstOrDefaultAsync(d => d.UserId == _clinicContext.UserId
                            && d.ClinicId == _clinicContext.ClinicId && !d.IsDeleted);

                    if (doctorRecord != null)
                    {
                        var patientIds = await _db.Appointments
                            .Where(a => a.DoctorId == doctorRecord.Id && a.ClinicId == _clinicContext.ClinicId && !a.IsDeleted)
                            .Select(a => a.PatientId).Distinct().ToListAsync();
                        query = query.Where(p => patientIds.Contains(p.Id));
                    }
                    else
                    {
                        query = query.Where(p => false);
                    }
                }
            }

            var patients = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();

            var rows = patients.Select(p => new List<string> {
                $"#{p.PatientNumber}", p.FullName, p.Phone ?? "—",
                p.Gender == "male" || p.Gender == "ذكر" ? (isRtl ? "ذكر" : "Male")
                    : p.Gender == "female" || p.Gender == "أنثى" ? (isRtl ? "أنثى" : "Female") : "—",
                p.CreatedAt.ToString("yyyy-MM-dd"),
            }).ToList();

            var columns = isRtl
                ? new List<string> { "رقم المريض", "الاسم", "الهاتف", "الجنس", "تاريخ الإنشاء" }
                : new List<string> { "Patient #", "Name", "Phone", "Gender", "Created" };

            var clinic = _clinicContext.ClinicId.HasValue ? await _db.Clinics.FindAsync(_clinicContext.ClinicId.Value) : null;
            var summary = new List<(string, string)> {
                (isRtl ? "إجمالي المرضى" : "Total Patients", patients.Count.ToString()),
            };

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "المرضى" : "Patients",
                    Title = isRtl ? "قائمة المرضى" : "Patients List",
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "patients.xlsx");
            }
            else
            {
                var bytes = _pdfExport.GenerateTableReport(new PdfReportRequest
                {
                    Title = isRtl ? "قائمة المرضى" : "Patients List",
                    ClinicName = clinic?.Name ?? "",
                    LogoPath = ResolveLogoPath(clinic?.Logo),
                    IsRtl = isRtl,
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                });
                return File(bytes, "application/pdf", "patients.pdf");
            }
        }

        // GET: api/patients/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<PatientResponseDto>> GetById(Guid id)
        {
            var patient = await _db.Patients.FindAsync(id);

            if (patient == null || patient.IsDeleted)
                return NotFound();

            // ✅ تحقق أن المريض ينتمي لنفس العيادة
            if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            return Ok(ToResponse(patient));
        }

        // ✅ GET: api/patients/{id}/export?format=pdf|excel&fields=name,phone,...
        [HttpGet("{id}/export")]
        public async Task<ActionResult> ExportOne(Guid id, [FromQuery] string? fields, [FromQuery] string format = "pdf", [FromQuery] string lang = "ar")
        {
            var patient = await _db.Patients.FindAsync(id);
            if (patient == null || patient.IsDeleted) return NotFound();
            if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId) return Forbid();

            var isRtl = lang == "ar";
            var requestedFields = (fields ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            var allFields = new List<(string Key, string LabelAr, string LabelEn, string? Value)>
            {
                ("number", "رقم المريض", "Patient #", $"#{patient.PatientNumber}"),
                ("name", "الاسم الكامل", "Full Name", patient.FullName),
                ("phone", "الهاتف", "Phone", patient.Phone),
                ("phone2", "هاتف إضافي", "Phone 2", patient.Phone2),
                ("dob", "تاريخ الميلاد", "Date of Birth", patient.DateOfBirth?.ToString("yyyy-MM-dd")),
                ("gender", "الجنس", "Gender", patient.Gender),
                ("nationalId", "الرقم الوطني", "National ID", patient.NationalId),
                ("bloodType", "فصيلة الدم", "Blood Type", patient.BloodType),
                ("address", "العنوان", "Address", patient.Address),
                ("email", "البريد الإلكتروني", "Email", patient.Email),
                ("emergencyContact", "جهة اتصال الطوارئ", "Emergency Contact", patient.EmergencyContact),
                ("emergencyPhone", "هاتف الطوارئ", "Emergency Phone", patient.EmergencyPhone),
                ("allergies", "الحساسية", "Allergies", patient.Allergies),
                ("chronicDiseases", "الأمراض المزمنة", "Chronic Diseases", patient.ChronicDiseases),
                ("occupation", "المهنة", "Occupation", patient.Occupation),
                ("maritalStatus", "الحالة الاجتماعية", "Marital Status", patient.MaritalStatus),
                ("createdAt", "تاريخ التسجيل", "Registered On", patient.CreatedAt.ToString("yyyy-MM-dd")),
            };

            var selected = requestedFields.Count > 0 ? allFields.Where(f => requestedFields.Contains(f.Key)) : allFields;
            var rows = selected.Select(f => new List<string> { isRtl ? f.LabelAr : f.LabelEn, f.Value ?? "—" }).ToList();

            var columns = isRtl ? new List<string> { "الحقل", "القيمة" } : new List<string> { "Field", "Value" };
            var clinic = _clinicContext.ClinicId.HasValue ? await _db.Clinics.FindAsync(_clinicContext.ClinicId.Value) : null;

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "بطاقة المريض" : "Patient Card",
                    Title = patient.FullName,
                    Columns = columns,
                    Rows = rows,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "patient.xlsx");
            }
            else
            {
                var bytes = _pdfExport.GenerateTableReport(new PdfReportRequest
                {
                    Title = patient.FullName,
                    Subtitle = isRtl ? "بطاقة المريض" : "Patient Card",
                    ClinicName = clinic?.Name ?? "",
                    LogoPath = ResolveLogoPath(clinic?.Logo),
                    IsRtl = isRtl,
                    Columns = columns,
                    Rows = rows,
                });
                return File(bytes, "application/pdf", "patient.pdf");
            }
        }

        // POST: api/patients
        // POST
        [HttpPost]
        public async Task<ActionResult<PatientResponseDto>> Create([FromBody] CreatePatientDto model)
        {
            if (!_clinicContext.HasPermission("patients.create"))
                return Forbid();

            if (_clinicContext.IsSuperAdmin)
                return BadRequest("SuperAdmin لا يستطيع إضافة مرضى مباشرة");

            if (_clinicContext.ClinicId == null)
                return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

            var (canAdd, error) = await _subscriptionService.CanAddPatient(_clinicContext.ClinicId.Value);
            if (!canAdd) return BadRequest(error);

            if (string.IsNullOrWhiteSpace(model.FullName))
                return BadRequest("FullName is required");

            if (!string.IsNullOrWhiteSpace(model.NationalId))
            {
                var nationalIdTaken = await _db.Patients.AnyAsync(p =>
                    p.ClinicId == _clinicContext.ClinicId && !p.IsDeleted && p.NationalId == model.NationalId);
                if (nationalIdTaken)
                    return BadRequest("رقم الهوية الوطني مستخدم مسبقاً لمريض آخر بهذي العيادة");
            }

            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
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
                    IsDeleted = false,
                    ClinicId = _clinicContext.ClinicId.Value,
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

                try
                {
                    await _db.SaveChangesAsync();
                    await CheckPatientQuotaAlert(patient.ClinicId);
                    return CreatedAtAction(nameof(GetById), new { id = patient.Id }, ToResponse(patient));
                }
                catch (DbUpdateException) when (attempt < maxRetries)
                {
                    // تعارض على PatientNumber — أزل التتبع وحاول مرة أخرى برقم جديد
                    _db.Entry(patient).State = EntityState.Detached;
                }
            }

            return Conflict("تعذر إنشاء رقم مريض فريد، يرجى المحاولة مرة أخرى");
        }

        // ✅ ينبّه بجرس الواجهة عند اقتراب عدد المرضى من حد الخطة (85%+) — مرة واحدة
        // فقط لحد ما يُقرأ التنبيه، عشان ما يتكرر مع كل مريض جديد
        private async Task CheckPatientQuotaAlert(Guid clinicId)
        {
            try
            {
                var sub = await _db.Subscriptions
                    .Include(s => s.Plan)
                    .Where(s => s.ClinicId == clinicId && s.IsActive)
                    .OrderByDescending(s => s.EndDate)
                    .FirstOrDefaultAsync();

                if (sub == null || sub.Plan.MaxPatients == -1) return;

                var count = await _db.Patients.CountAsync(p => p.ClinicId == clinicId && !p.IsDeleted);
                var pct = (double)count / sub.Plan.MaxPatients * 100;
                if (pct < 85) return;

                const string title = "تنبيه الحصة";
                var alreadyAlerted = await _db.AppNotifications.AnyAsync(n =>
                    n.ClinicId == clinicId && n.Type == "alert" && n.Title == title && !n.IsRead);
                if (alreadyAlerted) return;

                await _notificationService.CreateAppNotification(
                    clinicId, "alert", title,
                    $"اقتربت من الحد الأقصى لعدد المرضى ({Math.Round(pct)}%)");
            }
            catch
            {
                // ✅ تنبيه ثانوي — لا يفشل إنشاء المريض بسببه
            }
        }

        // PUT: api/patients/{id}
        [HttpPut("{id}")]
        public async Task<ActionResult<PatientResponseDto>> Update(Guid id, [FromBody] UpdatePatientDto dto)
        {
            if (!_clinicContext.HasPermission("patients.edit"))
                return Forbid();
            var patient = await _db.Patients.FindAsync(id);

            if (patient == null || patient.IsDeleted)
                return NotFound();

            // ✅ تحقق أن المريض ينتمي لنفس العيادة
            if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            if (string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest("FullName is required");

            if (!string.IsNullOrWhiteSpace(dto.NationalId))
            {
                var nationalIdTaken = await _db.Patients.AnyAsync(p =>
                    p.Id != id && p.ClinicId == patient.ClinicId && !p.IsDeleted && p.NationalId == dto.NationalId);
                if (nationalIdTaken)
                    return BadRequest("رقم الهوية الوطني مستخدم مسبقاً لمريض آخر بهذي العيادة");
            }

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
            if (!_clinicContext.HasPermission("patients.delete"))
                return Forbid();

            var patient = await _db.Patients.FindAsync(id);

            if (patient == null || patient.IsDeleted)
                return NotFound();

            // ✅ تحقق أن المريض ينتمي لنفس العيادة
            if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            patient.IsDeleted = true;
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