using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Doctors;
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
    public class DoctorsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly SubscriptionService _subscriptionService;
        private readonly IPdfExportService _pdfExport;
        private readonly IExcelExportService _excelExport;
        private readonly IWebHostEnvironment _env;

        public DoctorsController(ApplicationDbContext db, IClinicContext clinicContext, SubscriptionService subscriptionService,
            IPdfExportService pdfExport, IExcelExportService excelExport, IWebHostEnvironment env)
        {
            _db = db;
            _clinicContext = clinicContext;
            _subscriptionService = subscriptionService;
            _pdfExport = pdfExport;
            _excelExport = excelExport;
            _env = env;
        }

        private string? ResolveLogoPath(string? logoUrl)
        {
            if (string.IsNullOrEmpty(logoUrl)) return null;
            var cleanPath = logoUrl.Split('?')[0].TrimStart('/');
            var fullPath = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), cleanPath.Replace("logos/", "logos" + Path.DirectorySeparatorChar));
            return System.IO.File.Exists(fullPath) ? fullPath : null;
        }

        // GET: api/doctors
        [HttpGet]
        public async Task<ActionResult<IEnumerable<DoctorResponseDto>>> GetAll()
        {
            if (!_clinicContext.HasPermission("doctors.view") && !_clinicContext.IsCompanyStaff)
                return Forbid();

            var query = _db.Doctors
                .Include(d => d.Department) // ✅ تحميل القسم
                .Where(d => !d.IsDeleted);

            if (!_clinicContext.IsCompanyStaff)
            {
                if (_clinicContext.ClinicId == null)
                    return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

                query = query.Where(d => d.ClinicId == _clinicContext.ClinicId);
            }

            var doctors = await query.OrderBy(d => d.FullName).ToListAsync();
            return Ok(doctors.Select(d => ToResponse(d)).ToList());
        }

        // ✅ GET: api/doctors/export?format=pdf|excel
        [HttpGet("export")]
        public async Task<ActionResult> Export([FromQuery] string format = "pdf", [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("doctors.view") && !_clinicContext.IsCompanyStaff)
                return Forbid();

            var isRtl = lang == "ar";
            var query = _db.Doctors.Include(d => d.Department).Where(d => !d.IsDeleted);

            if (!_clinicContext.IsCompanyStaff)
            {
                if (_clinicContext.ClinicId == null) return Unauthorized();
                query = query.Where(d => d.ClinicId == _clinicContext.ClinicId);
            }

            var doctors = await query.OrderBy(d => d.FullName).ToListAsync();

            var rows = doctors.Select(d => new List<string> {
                d.FullName, d.Specialty ?? "—", d.Phone ?? "—", d.Department?.Name ?? "—",
                d.IsActive ? (isRtl ? "نشط" : "Active") : (isRtl ? "غير نشط" : "Inactive"),
            }).ToList();

            var columns = isRtl
                ? new List<string> { "الاسم", "التخصص", "الهاتف", "القسم", "الحالة" }
                : new List<string> { "Name", "Specialty", "Phone", "Department", "Status" };

            var clinic = _clinicContext.ClinicId.HasValue ? await _db.Clinics.FindAsync(_clinicContext.ClinicId.Value) : null;
            var summary = new List<(string, string)> {
                (isRtl ? "إجمالي الأطباء" : "Total Doctors", doctors.Count.ToString()),
                (isRtl ? "النشطون" : "Active", doctors.Count(d => d.IsActive).ToString()),
            };

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "الأطباء" : "Doctors",
                    Title = isRtl ? "قائمة الأطباء" : "Doctors List",
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "doctors.xlsx");
            }
            else
            {
                var bytes = _pdfExport.GenerateTableReport(new PdfReportRequest
                {
                    Title = isRtl ? "قائمة الأطباء" : "Doctors List",
                    ClinicName = clinic?.Name ?? "",
                    LogoPath = ResolveLogoPath(clinic?.Logo),
                    IsRtl = isRtl,
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                });
                return File(bytes, "application/pdf", "doctors.pdf");
            }
        }

        // ═══════════════════════════════════════
        // GET: api/doctors/available-for-staff
        // ✅ يرجع كل أطباء العيادة، مع اسم الموظف المرتبط بكل طبيب (لو موجود) —
        // تُستخدم بقائمة "اختيار طبيب موجود" عند إضافة موظف، عشان:
        //   1) نمنع اختيار طبيب مرتبط بموظف آخر أصلاً
        //   2) نعرض اسم الموظف المرتبط بوضوح بدل ما يختفي الخيار بصمت
        // ?excludeStaffId={id} — يُستخدم وقت التعديل، عشان الطبيب المرتبط
        // بنفس الموظف اللي نعدّله يبقى "متاح" (مو محجوب لأنه مرتبط بنفسه)
        // ═══════════════════════════════════════
        [HttpGet("available-for-staff")]
        public async Task<ActionResult> GetAvailableForStaff([FromQuery] Guid? excludeStaffId)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var clinicId = _clinicContext.ClinicId.Value;

            var doctors = await _db.Doctors
                .Where(d => d.ClinicId == clinicId && !d.IsDeleted && d.IsActive)
                .OrderBy(d => d.FullName)
                .ToListAsync();

            var doctorIds = doctors.Select(d => d.Id).ToList();

            // نجيب كل روابط Staff→Doctor الحالية بضربة وحدة، بدل استعلام لكل طبيب لحاله
            var links = await _db.Staff
                .Where(s => s.ClinicId == clinicId && s.DoctorId != null && doctorIds.Contains(s.DoctorId!.Value))
                .Select(s => new { s.Id, s.FullName, s.DoctorId })
                .ToListAsync();

            var result = doctors.Select(d =>
            {
                var link = links.FirstOrDefault(l => l.DoctorId == d.Id);
                var isLinkedToAnother = link != null && link.Id != excludeStaffId;
                return new
                {
                    id = d.Id,
                    fullName = d.FullName,
                    specialty = d.Specialty,
                    linkedStaffId = link?.Id,
                    linkedStaffName = link?.FullName,
                    isAvailable = !isLinkedToAnother,
                };
            });

            return Ok(result);
        }

        // GET: api/doctors/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<DoctorResponseDto>> GetById(Guid id)
        {
            var doctor = await _db.Doctors
                .Include(d => d.Clinic)
                .Include(d => d.Department) // ✅
                .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);

            if (doctor == null)
                return NotFound();

            if (!_clinicContext.IsSuperAdmin && doctor.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            return Ok(ToResponse(doctor));
        }

        // POST: api/doctors
        [HttpPost]
        public async Task<ActionResult<DoctorResponseDto>> Create([FromBody] CreateDoctorDto dto)
        {
            if (!_clinicContext.HasPermission("doctors.create"))
                return Forbid();

            if (_clinicContext.IsSuperAdmin)
                return BadRequest("SuperAdmin لا يستطيع إضافة أطباء مباشرة");

            if (_clinicContext.ClinicId == null)
                return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

            var (canAdd, error) = await _subscriptionService.CanAddDoctor(_clinicContext.ClinicId.Value);
            if (!canAdd) return BadRequest(error);

            if (string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest("اسم الطبيب مطلوب");

            // ✅ منع تكرار اسم الطبيب بنفس العيادة
            var nameExists = await _db.Doctors.AnyAsync(d => d.ClinicId == _clinicContext.ClinicId && !d.IsDeleted && d.FullName == dto.FullName);
            if (nameExists)
                return BadRequest("يوجد طبيب بنفس الاسم مسبقاً");

            // ✅ التحقق أن القسم ينتمي لنفس العيادة
            if (dto.DepartmentId.HasValue)
            {
                var dept = await _db.Departments
                    .FirstOrDefaultAsync(d => d.Id == dto.DepartmentId.Value
                        && d.ClinicId == _clinicContext.ClinicId.Value
                        && d.IsActive);
                if (dept == null)
                    return BadRequest("القسم غير موجود أو لا ينتمي لهذه العيادة");
            }

            var doctor = new Doctor
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                IsDeleted = false,
                ClinicId = _clinicContext.ClinicId.Value,
                FullName = dto.FullName,
                Specialty = dto.Specialty,
                Phone = dto.Phone,
                Email = dto.Email,
                Notes = dto.Notes,
                DepartmentId = dto.DepartmentId,       // ✅
                WorkType = dto.WorkType ?? "both",     // ✅
            };

            _db.Doctors.Add(doctor);
            await _db.SaveChangesAsync();

            await _db.Entry(doctor).Reference(d => d.Clinic).LoadAsync();
            if (doctor.DepartmentId.HasValue)
                await _db.Entry(doctor).Reference(d => d.Department).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = doctor.Id }, ToResponse(doctor));
        }

        // PUT: api/doctors/{id}
        [HttpPut("{id}")]
        public async Task<ActionResult<DoctorResponseDto>> Update(Guid id, [FromBody] UpdateDoctorDto dto)
        {
            if (!_clinicContext.HasPermission("doctors.edit"))
                return Forbid();

            var doctor = await _db.Doctors
                .Include(d => d.Clinic)
                .Include(d => d.Department) // ✅
                .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);

            if (doctor == null)
                return NotFound();

            if (!_clinicContext.IsSuperAdmin && doctor.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            if (string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest("اسم الطبيب مطلوب");

            // ✅ منع تكرار اسم الطبيب بنفس العيادة (باستثناء الطبيب نفسه)
            var nameExists = await _db.Doctors.AnyAsync(d => d.Id != id && d.ClinicId == doctor.ClinicId && !d.IsDeleted && d.FullName == dto.FullName);
            if (nameExists)
                return BadRequest("يوجد طبيب بنفس الاسم مسبقاً");

            // ✅ التحقق من القسم
            if (dto.DepartmentId.HasValue)
            {
                var dept = await _db.Departments
                    .FirstOrDefaultAsync(d => d.Id == dto.DepartmentId.Value
                        && d.ClinicId == doctor.ClinicId
                        && d.IsActive);
                if (dept == null)
                    return BadRequest("القسم غير موجود أو لا ينتمي لهذه العيادة");
            }

            doctor.FullName = dto.FullName;
            doctor.Specialty = dto.Specialty;
            doctor.Phone = dto.Phone;
            doctor.Email = dto.Email;
            doctor.Notes = dto.Notes;
            doctor.IsActive = dto.IsActive;
            doctor.DepartmentId = dto.DepartmentId;    // ✅
            doctor.WorkType = dto.WorkType ?? "both";  // ✅

            await _db.SaveChangesAsync();

            if (doctor.DepartmentId.HasValue)
                await _db.Entry(doctor).Reference(d => d.Department).LoadAsync();

            return Ok(ToResponse(doctor));
        }

        // PATCH: api/doctors/{id}/toggle
        [HttpPatch("{id}/toggle")]
        public async Task<ActionResult> Toggle(Guid id)
        {
            if (!_clinicContext.IsSuperAdmin && !_clinicContext.HasPermission("doctors.edit")) return Forbid();
            var doctor = await _db.Doctors.FindAsync(id);

            if (doctor == null || doctor.IsDeleted)
                return NotFound();

            if (!_clinicContext.IsSuperAdmin && doctor.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            doctor.IsActive = !doctor.IsActive;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = doctor.IsActive ? "تم تفعيل الطبيب" : "تم تعطيل الطبيب",
                isActive = doctor.IsActive
            });
        }

        // DELETE: api/doctors/{id}
        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(Guid id)
        {
            if (!_clinicContext.HasPermission("doctors.delete"))
                return Forbid();

            var doctor = await _db.Doctors.FindAsync(id);

            if (doctor == null || doctor.IsDeleted)
                return NotFound();

            if (!_clinicContext.IsSuperAdmin && doctor.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            // ✅ امنع الحذف لو عنده مواعيد مستقبلية غير ملغاة
            var hasUpcomingAppointments = await _db.Appointments.AnyAsync(a =>
                a.DoctorId == id &&
                !a.IsDeleted &&
                a.Status != "cancelled" &&
                a.AppointmentDate > DateTime.UtcNow);

            if (hasUpcomingAppointments)
                return BadRequest("لا يمكن حذف الطبيب لوجود مواعيد مستقبلية مرتبطة به — ألغِ أو أعد جدولة هذه المواعيد أولاً");

            doctor.IsDeleted = true;
            doctor.IsActive = false;   // ✅ نوقفه كمان عشان ما يظهر بأي قائمة اختيار
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ✅ ToResponse محدث
        private static DoctorResponseDto ToResponse(Doctor d) => new DoctorResponseDto
        {
            Id = d.Id,
            FullName = d.FullName,
            Specialty = d.Specialty,
            Phone = d.Phone,
            Email = d.Email,
            Notes = d.Notes,
            IsActive = d.IsActive,
            ClinicId = d.ClinicId,
            ClinicName = d.Clinic?.Name,
            CreatedAt = d.CreatedAt,
            DepartmentId = d.DepartmentId,           // ✅
            DepartmentName = d.Department?.Name,     // ✅
            WorkType = d.WorkType,                   // ✅
        };
    }
}