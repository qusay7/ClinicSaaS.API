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
    public class StaffController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public StaffController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        private static string Msg(string lang, string ar, string en) => lang == "ar" ? ar : en;

        // GET: api/staff
        [HttpGet]
        public async Task<ActionResult> GetAll([FromQuery] string? role, [FromQuery] bool? isActive)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var query = _db.Staff
                .Include(s => s.Department)
                .Include(s => s.Role)
                .Include(s => s.Doctor)
                .Where(s => s.ClinicId == _clinicContext.ClinicId);
            if (!string.IsNullOrEmpty(role)) query = query.Where(s => s.StaffRole == role);
            if (isActive.HasValue) query = query.Where(s => s.IsActive == isActive);

            var staff = await query.OrderBy(s => s.FullName).ToListAsync();

            return Ok(staff.Select(s => ToResponse(s)));
        }

        // GET: api/staff/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult> GetById(Guid id)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var s = await _db.Staff
                .Include(s => s.Department)
                .Include(s => s.Role)
                .Include(s => s.Doctor)
                .FirstOrDefaultAsync(s => s.Id == id && s.ClinicId == _clinicContext.ClinicId);
            if (s == null) return NotFound();
            return Ok(ToResponse(s));
        }

        // ═══════════════════════════════════════
        // POST: api/staff
        // ✅ يدعم الآن: إنشاء بطاقة طبيب تلقائياً (IsDoctor) وحساب دخول تلقائياً
        // (CreateLoginAccount) — الثلاثة (Staff/Doctor/User) بمعاملة واحدة.
        // ═══════════════════════════════════════
        [HttpPost]
        public async Task<ActionResult> Create([FromBody] CreateStaffDto dto, [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var clinicId = _clinicContext.ClinicId.Value;

            if (string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest(Msg(lang, "الاسم مطلوب", "Name is required"));

            // ✅ منع تكرار اسم الموظف بنفس العيادة
            var nameExists = await _db.Staff.AnyAsync(s => s.ClinicId == clinicId && s.FullName == dto.FullName);
            if (nameExists)
                return BadRequest(Msg(lang, "يوجد موظف بنفس الاسم مسبقاً", "A staff member with this name already exists"));

            // ✅ تحقق مسبق لو طالبين حساب دخول — يمنع نصف عملية فاشلة بمفاجأة
            if (dto.CreateLoginAccount)
            {
                if (string.IsNullOrWhiteSpace(dto.LoginEmail))
                    return BadRequest(Msg(lang, "البريد الإلكتروني مطلوب لإنشاء حساب دخول", "Email is required to create a login account"));
                if (string.IsNullOrWhiteSpace(dto.LoginPassword) || dto.LoginPassword.Length < 6)
                    return BadRequest(Msg(lang, "كلمة المرور يجب أن تكون 6 أحرف على الأقل", "Password must be at least 6 characters"));

                var emailExists = await _db.Users.AnyAsync(u => u.Email == dto.LoginEmail);
                if (emailExists)
                    return BadRequest(Msg(lang, "البريد الإلكتروني مستخدم مسبقاً", "This email is already in use"));
            }

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                Guid? newUserId = null;
                Guid? newDoctorId = null;

                // ── 1) حساب الدخول (اختياري) ──
                if (dto.CreateLoginAccount)
                {
                    var loginRole = !string.IsNullOrWhiteSpace(dto.LoginRole)
                        ? dto.LoginRole
                        : (dto.IsDoctor ? "Doctor" : "ClinicStaff");

                    var user = new User
                    {
                        Id = Guid.NewGuid(),
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true,
                        FullName = dto.FullName,
                        Username = dto.LoginUsername?.Trim(),
                        Email = dto.LoginEmail!,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.LoginPassword),
                        Role = loginRole,
                        ClinicId = clinicId,
                        DepartmentId = dto.DepartmentId,
                    };

                    var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == loginRole && r.ClinicId == clinicId);
                    if (role != null) user.RoleId = role.Id;

                    _db.Users.Add(user);
                    await _db.SaveChangesAsync();
                    newUserId = user.Id;
                }

                // ── 2) بطاقة الطبيب (اختياري) ──
                // لو أُرسل DoctorId جاهز، نربط بطبيب موجود مسبقاً بدل ما ننشئ جديد.
                Guid? linkedDoctorId = dto.DoctorId;
                if (!linkedDoctorId.HasValue && dto.IsDoctor)
                {
                    var doctor = new Doctor
                    {
                        Id = Guid.NewGuid(),
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true,
                        IsDeleted = false,
                        ClinicId = clinicId,
                        FullName = dto.FullName,
                        Email = dto.LoginEmail ?? dto.Email,
                        Specialty = dto.Specialization,
                        DepartmentId = dto.DepartmentId,
                        WorkType = string.IsNullOrWhiteSpace(dto.DoctorWorkType) ? "appointments" : dto.DoctorWorkType,
                        UserId = newUserId,
                    };
                    _db.Doctors.Add(doctor);
                    await _db.SaveChangesAsync();
                    newDoctorId = doctor.Id;
                    linkedDoctorId = doctor.Id;
                }

                // ── 3) الموظف نفسه ──
                var staff = new Staff
                {
                    Id = Guid.NewGuid(),
                    ClinicId = clinicId,
                    FullName = dto.FullName,
                    FullNameEn = dto.FullNameEn,
                    Gender = dto.Gender,
                    DateOfBirth = dto.DateOfBirth,
                    NationalId = dto.NationalId,
                    Nationality = dto.Nationality,
                    MaritalStatus = dto.MaritalStatus,
                    BloodType = dto.BloodType,
                    Phone = dto.Phone,
                    Phone2 = dto.Phone2,
                    Email = dto.Email,
                    Address = dto.Address,
                    EmergencyContact = dto.EmergencyContact,
                    EmergencyPhone = dto.EmergencyPhone,
                    JobTitle = dto.JobTitle ?? "",
                    DepartmentId = dto.DepartmentId,
                    StaffRole = dto.StaffRole,
                    RoleId = dto.RoleId,
                    ContractType = dto.ContractType ?? "fulltime",
                    JoinDate = dto.JoinDate,
                    EndDate = dto.EndDate,
                    Salary = dto.Salary,
                    WorkingHours = dto.WorkingHours,
                    Qualifications = dto.Qualifications,
                    Specialization = dto.Specialization,
                    IsActive = true,
                    Notes = dto.Notes,
                    UserId = newUserId ?? dto.UserId,
                    DoctorId = linkedDoctorId,
                    CreatedAt = DateTime.UtcNow,
                };

                _db.Staff.Add(staff);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new
                {
                    staff.Id,
                    doctorId = newDoctorId,
                    userId = newUserId,
                    message = Msg(lang, "تم إضافة الموظف بنجاح", "Staff member added successfully"),
                });
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                return BadRequest(Msg(lang, "البريد الإلكتروني مستخدم بالفعل", "This email is already in use"));
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // PUT: api/staff/{id}
        // ملاحظة: التعديل هنا يسمح بربط/تغيير الطبيب والمستخدم المرتبطين يدوياً (عبر DoctorId/UserId)،
        // لكنه لا يُنشئ بطاقة طبيب أو حساب دخول جديدين تلقائياً — هذا يحدث فقط عند الإنشاء الأول.
        [HttpPut("{id}")]
        public async Task<ActionResult> Update(Guid id, [FromBody] CreateStaffDto dto, [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var s = await _db.Staff.FirstOrDefaultAsync(s => s.Id == id && s.ClinicId == _clinicContext.ClinicId);
            if (s == null) return NotFound();

            s.FullName = dto.FullName ?? s.FullName;
            s.FullNameEn = dto.FullNameEn;
            s.Gender = dto.Gender;
            s.DateOfBirth = dto.DateOfBirth;
            s.NationalId = dto.NationalId;
            s.Nationality = dto.Nationality;
            s.MaritalStatus = dto.MaritalStatus;
            s.BloodType = dto.BloodType;
            s.Phone = dto.Phone;
            s.Phone2 = dto.Phone2;
            s.Email = dto.Email;
            s.Address = dto.Address;
            s.EmergencyContact = dto.EmergencyContact;
            s.EmergencyPhone = dto.EmergencyPhone;
            s.JobTitle = dto.JobTitle ?? s.JobTitle;
            s.DepartmentId = dto.DepartmentId;
            s.StaffRole = dto.StaffRole;
            s.RoleId = dto.RoleId;
            s.ContractType = dto.ContractType ?? s.ContractType;
            s.JoinDate = dto.JoinDate;
            s.EndDate = dto.EndDate;
            s.Salary = dto.Salary;
            s.WorkingHours = dto.WorkingHours;
            s.Qualifications = dto.Qualifications;
            s.Specialization = dto.Specialization;
            s.Notes = dto.Notes;
            s.UserId = dto.UserId;
            s.DoctorId = dto.DoctorId;   // ✅ كانت مفقودة — الآن يمكن تحديث الربط يدوياً

            // ✅ لو الموظف مرتبط ببطاقة طبيب، خلي قسم الطبيب يتزامن تلقائياً مع قسم الموظف
            if (s.DoctorId.HasValue)
            {
                var linkedDoctor = await _db.Doctors.FindAsync(s.DoctorId.Value);
                if (linkedDoctor != null)
                    linkedDoctor.DepartmentId = dto.DepartmentId;
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = Msg(lang, "تم التحديث بنجاح", "Updated successfully") });
        }

        // PUT: api/staff/{id}/toggle-active
        [HttpPut("{id}/toggle-active")]
        public async Task<ActionResult> ToggleActive(Guid id, [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var s = await _db.Staff.FirstOrDefaultAsync(s => s.Id == id && s.ClinicId == _clinicContext.ClinicId);
            if (s == null) return NotFound();
            s.IsActive = !s.IsActive;
            await _db.SaveChangesAsync();
            return Ok(new { isActive = s.IsActive, message = Msg(lang, s.IsActive ? "تم تفعيل الموظف" : "تم إيقاف الموظف", s.IsActive ? "Staff activated" : "Staff deactivated") });
        }

        // DELETE: api/staff/{id}
        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(Guid id, [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var s = await _db.Staff.FirstOrDefaultAsync(s => s.Id == id && s.ClinicId == _clinicContext.ClinicId);
            if (s == null) return NotFound();

            s.IsDeleted = true;
            s.IsActive = false;
            await _db.SaveChangesAsync();
            return Ok(new { message = Msg(lang, "تم الحذف", "Deleted") });
        }

        // GET: api/staff/stats
        [HttpGet("stats")]
        public async Task<ActionResult> GetStats()
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var all = await _db.Staff.Where(s => s.ClinicId == _clinicContext.ClinicId).ToListAsync();
            return Ok(new
            {
                total = all.Count,
                active = all.Count(s => s.IsActive),
                inactive = all.Count(s => !s.IsActive),
                byRole = all.GroupBy(s => s.StaffRole ?? "other").Select(g => new { role = g.Key, count = g.Count() }),
                byContract = all.GroupBy(s => s.ContractType).Select(g => new { type = g.Key, count = g.Count() }),
                totalSalary = all.Where(s => s.IsActive).Sum(s => s.Salary ?? 0),
            });
        }

        private static object ToResponse(Staff s) => new
        {
            s.Id,
            s.ClinicId,
            s.FullName,
            s.FullNameEn,
            s.Gender,
            dateOfBirth = s.DateOfBirth?.ToString("yyyy-MM-dd"),
            s.NationalId,
            s.Nationality,
            s.MaritalStatus,
            s.BloodType,
            s.Phone,
            s.Phone2,
            s.Email,
            s.Address,
            s.EmergencyContact,
            s.EmergencyPhone,
            s.JobTitle,
            departmentId = s.DepartmentId,
            staffRole = s.StaffRole,
            s.ContractType,
            joinDate = s.JoinDate?.ToString("yyyy-MM-dd"),
            endDate = s.EndDate?.ToString("yyyy-MM-dd"),
            s.Salary,
            s.WorkingHours,
            s.Qualifications,
            s.Specialization,
            s.IsActive,
            s.Notes,
            s.UserId,
            departmentName = s.Department?.Name,
            roleId = s.RoleId,
            roleName = s.Role?.Name,
            roleNameAr = s.Role?.Description,   // ✅ التسمية العربية
            roleNameEn = s.Role?.NameEn,         // ✅ التسمية الإنجليزية
            doctorId = s.DoctorId,
            doctorName = s.Doctor?.FullName,
            createdAt = s.CreatedAt.ToString("yyyy-MM-dd"),
            age = s.DateOfBirth.HasValue
                ? (DateTime.UtcNow - s.DateOfBirth.Value).Days / 365
                : (int?)null,
            yearsInClinic = s.JoinDate.HasValue
                ? Math.Round((DateTime.UtcNow - s.JoinDate.Value).TotalDays / 365, 1)
                : (double?)null,
        };
    }

    public class CreateStaffDto
    {
        public string? FullName { get; set; }
        public string? FullNameEn { get; set; }
        public string? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? NationalId { get; set; }
        public string? Nationality { get; set; }
        public string? MaritalStatus { get; set; }
        public string? BloodType { get; set; }
        public string? Phone { get; set; }
        public string? Phone2 { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? EmergencyContact { get; set; }
        public string? EmergencyPhone { get; set; }
        public string? JobTitle { get; set; }
        public Guid? DepartmentId { get; set; }
        public string? StaffRole { get; set; }
        public Guid? RoleId { get; set; }
        public Guid? DoctorId { get; set; }
        public string? Role { get; set; }
        public string? ContractType { get; set; }
        public DateTime? JoinDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal? Salary { get; set; }
        public string? WorkingHours { get; set; }
        public string? Qualifications { get; set; }
        public string? Specialization { get; set; }
        public string? Notes { get; set; }
        public Guid? UserId { get; set; }

        // ✅ جديد — "هل هذا الشخص طبيب؟"
        public bool IsDoctor { get; set; } = false;
        public string? DoctorWorkType { get; set; }   // افتراضي: "appointments"

        // ✅ جديد — "هل يحتاج حساب دخول؟"
        public bool CreateLoginAccount { get; set; } = false;
        public string? LoginEmail { get; set; }
        public string? LoginUsername { get; set; }
        public string? LoginPassword { get; set; }
        public string? LoginRole { get; set; }   // افتراضي: Doctor لو IsDoctor، وإلا ClinicStaff
    }
}