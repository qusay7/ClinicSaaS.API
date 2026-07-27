using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Users;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly SubscriptionService _subscriptionService;

        public UsersController(ApplicationDbContext db, IClinicContext clinicContext, SubscriptionService subscriptionService)
        {
            _db = db;
            _clinicContext = clinicContext;
            _subscriptionService = subscriptionService;
        }

        // GET: api/users
        [HttpGet]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<IEnumerable<UserResponseDto>>> GetAll()
        {
            var users = await _db.Users
                .Include(u => u.Clinic)
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();

            return Ok(users.Select(u => ToResponse(u)).ToList());
        }

        // GET: api/users/clinic/{clinicId}
        [HttpGet("clinic/{clinicId}")]
        [Authorize(Roles = "SuperAdmin,ClinicStaff,ClinicAdmin")]
        public async Task<ActionResult<IEnumerable<UserResponseDto>>> GetByClinic(Guid clinicId)
        {
            if (_clinicContext.Role == "ClinicAdmin" && clinicId != _clinicContext.ClinicId)
                return Forbid();

            var clinicExists = await _db.Clinics.AnyAsync(c => c.Id == clinicId);
            if (!clinicExists)
                return NotFound("Clinic not found.");

            var users = await _db.Users
                .Include(u => u.Clinic)
                .Where(u => u.ClinicId == clinicId)
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();

            return Ok(users.Select(u => ToResponse(u)).ToList());
        }

        // POST: api/users
        [HttpPost]
        [Authorize(Roles = "SuperAdmin,ClinicStaff,ClinicAdmin")]
        public async Task<ActionResult<UserResponseDto>> Create([FromBody] CreateUserDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.FullName)) return BadRequest("Full name required");
            if (string.IsNullOrWhiteSpace(dto.Email)) return BadRequest("Email is required");
            if (string.IsNullOrWhiteSpace(dto.Password)) return BadRequest("Password required");

            var emailExists = await _db.Users.AnyAsync(u => u.Email == dto.Email);
            if (emailExists) return BadRequest("البريد الإلكتروني مستخدم مسبقاً");

            if (_clinicContext.Role == "SuperAdmin")
            {
                var superAdminAllowedRoles = new[] { "ClinicAdmin", "ClinicStaff", "Doctor", "Receptionist" };
                if (!superAdminAllowedRoles.Contains(dto.Role))
                    return BadRequest("دور غير صحيح");

                if (!dto.ClinicId.HasValue)
                    return BadRequest("العيادة مطلوبة");
            }
            else if (_clinicContext.Role == "ClinicAdmin")
            {
                var allowedRoles = new[] { "Doctor", "Receptionist", "ClinicStaff" };
                if (!allowedRoles.Contains(dto.Role))
                    return BadRequest("يمكنك فقط إنشاء Doctor أو Receptionist");

                dto.ClinicId = _clinicContext.ClinicId;
            }
            else if (_clinicContext.Role == "ClinicStaff")
            {
                if (dto.Role == "ClinicStaff") return Forbid();
                dto.ClinicId = _clinicContext.ClinicId;
            }

            if (dto.ClinicId.HasValue)
            {
                var (canAdd, error) = await _subscriptionService.CanAddUser(dto.ClinicId.Value);
                if (!canAdd) return BadRequest(error);
            }

            var clinic = await _db.Clinics
                .FirstOrDefaultAsync(c => c.Id == dto.ClinicId && c.IsActive);
            if (clinic == null)
                return BadRequest("العيادة غير موجودة أو غير مفعّلة");

            if (!string.IsNullOrWhiteSpace(dto.Username))
            {
                var usernameExists = await _db.Users.AnyAsync(u => u.Username == dto.Username.Trim());
                if (usernameExists) return BadRequest("اسم المستخدم مستخدم مسبقاً");
            }

            // ✅ منع تكرار اسم المستخدم بنفس العيادة
            var nameExists = await _db.Users.AnyAsync(u => u.ClinicId == dto.ClinicId && u.FullName == dto.FullName);
            if (nameExists)
                return BadRequest("يوجد مستخدم بنفس الاسم مسبقاً");

            var user = new User
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                FullName = dto.FullName,
                Username = dto.Username?.Trim(),
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Role = dto.Role,
                ClinicId = dto.ClinicId,
                DepartmentId = dto.DepartmentId,
            };

            // ✅ نلف كل العمليات المترابطة (User + ربط Staff + RoleId) بمعاملة واحدة
            // عشان لو صار خطأ بمنتصف الطريق، ما يبقى مستخدم "ناقص" بقاعدة البيانات
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                _db.Users.Add(user);
                await _db.SaveChangesAsync();

                // ✅ ربط المستخدم ببطاقة موظف موجودة (بدل إنشاء Doctor تلقائياً كما كان سابقاً)
                // الآن بطاقة Staff هي مصدر الحقيقة — أي حساب دخول لازم يرتبط بموظف موجود مسبقاً،
                // وإذا كان ذلك الموظف طبيباً، فهو أصلاً مرتبط ببطاقة Doctor من خلال Staff.DoctorId.
                if (dto.StaffId.HasValue)
                {
                    var staff = await _db.Staff
                        .FirstOrDefaultAsync(s => s.Id == dto.StaffId.Value && s.ClinicId == dto.ClinicId);

                    if (staff == null)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest("بطاقة الموظف المحددة غير موجودة أو لا تتبع لهذه العيادة");
                    }

                    if (staff.UserId.HasValue && staff.UserId != user.Id)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest("هذا الموظف مرتبط بالفعل بحساب دخول آخر — قم بفك الربط أولاً");
                    }

                    staff.UserId = user.Id;
                    await _db.SaveChangesAsync();
                }

                // ✅ ربط الدور لجميع الأدوار
                var role = await _db.Roles
                    .FirstOrDefaultAsync(r => r.Name == dto.Role
                        && r.ClinicId == dto.ClinicId);

                if (role != null)
                {
                    user.RoleId = role.Id;
                    await _db.SaveChangesAsync();
                }

                await transaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                return BadRequest("البريد الإلكتروني أو اسم المستخدم مستخدم بالفعل");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            await _db.Entry(user).Reference(u => u.Clinic).LoadAsync();
            return CreatedAtAction(nameof(GetAll), new { id = user.Id }, ToResponse(user));
        }

        // PATCH: api/users/{id}/toggle
        [HttpPatch("{id}/toggle")]
        [Authorize(Roles = "SuperAdmin,ClinicStaff,ClinicAdmin")]
        public async Task<ActionResult> Toggle(Guid id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound();

            if (!_clinicContext.IsCompanyStaff && user.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            user.IsActive = !user.IsActive;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = user.IsActive ? "تم تفعيل المستخدم" : "تم تعطيل المستخدم",
                isActive = user.IsActive
            });
        }

        // PATCH: api/users/profile
        [HttpPatch("profile")]
        public async Task<ActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            var userId = _clinicContext.UserId;
            var user = await _db.Users.FindAsync(userId);

            if (user == null)
                return NotFound();

            if (!string.IsNullOrWhiteSpace(dto.FullName))
                user.FullName = dto.FullName;

            if (!string.IsNullOrWhiteSpace(dto.NewPassword))
            {
                if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
                    return BadRequest("كلمة المرور الحالية مطلوبة");

                if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                    return BadRequest("كلمة المرور الحالية غير صحيحة");

                if (dto.NewPassword.Length < 6)
                    return BadRequest("كلمة المرور الجديدة يجب أن تكون 6 أحرف على الأقل");

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = "تم تحديث البيانات بنجاح" });
        }

        private static UserResponseDto ToResponse(User u) => new UserResponseDto
        {
            Id = u.Id,
            FullName = u.FullName,
            Email = u.Email,
            Role = u.Role,
            IsActive = u.IsActive,
            ClinicId = u.ClinicId,
            ClinicName = u.Clinic?.Name,
            CreatedAt = u.CreatedAt
        };
    }
}