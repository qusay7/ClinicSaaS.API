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
    [Authorize] // هذا الكونترولر يتطلب توثيق المستخدم للوصول إليه
    public class UsersController: ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext; // ← أضف
        private readonly SubscriptionService _subscriptionService;

        public UsersController(ApplicationDbContext db, IClinicContext clinicContext, SubscriptionService subscriptionService)
        {
            _db = db;
            _clinicContext = clinicContext;
            _subscriptionService = subscriptionService;
        }


        // GET: api/users
        // SuperAdmin → يرى كل المستخدمين
        [HttpGet]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<IEnumerable<UserResponseDto>>> GetAll()
        {

            var users = await _db.Users
                .Include(u=>u.Clinic) // جلب بيانات العيادة المرتبطة بكل مستخدم
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();

            var result = users.Select(u => ToResponse(u)).ToList();
            return Ok(result);
        }

        // GET: api/users/clinic/{clinicId}
        // جلب مستخدمي عيادة معينة
        [HttpGet("clinic/{clinicId}")]
        [Authorize(Roles = "SuperAdmin,ClinicStaff,ClinicAdmin")]
        public async Task<ActionResult<IEnumerable<UserResponseDto>>> GetByClinic(Guid clinicId)
        {
            // ✅ ClinicAdmin يرى عيادته فقط
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
        // SuperAdmin → ينشئ أي مستخدم
        [HttpPost]
        [Authorize(Roles = "SuperAdmin,ClinicStaff")] // ✅ أضف ClinicStaff
        public async Task<ActionResult<UserResponseDto>> Create([FromBody] CreateUserDto dto)
        {
            // في Create أضف قبل إنشاء المستخدم
            if (dto.ClinicId.HasValue)
            {
                var (canAdd, error) = await _subscriptionService.CanAddUser(dto.ClinicId.Value);
                if (!canAdd)
                    return BadRequest(error);
            }

            if (string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest("Full name required");
            if (string.IsNullOrWhiteSpace(dto.Email))
                return BadRequest("Email is required");
            if (string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest("Password required");

            var emailExists = await _db.Users.AnyAsync(u => u.Email == dto.Email);
            if (emailExists)
                return BadRequest("The email address is already in use.");

            var validRoles = new[] { "ClinicStaff", "ClinicAdmin", "Doctor", "Receptionist" };

            if (_clinicContext.Role == "ClinicStaff" && dto.Role == "ClinicStaff")
                return Forbid();

            if (!validRoles.Contains(dto.Role))
                return BadRequest("دور غير صحيح");

            // ✅ تحقق من العيادة
            if (!dto.ClinicId.HasValue)
                return BadRequest("العيادة مطلوبة");

            var clinic = await _db.Clinics
                .FirstOrDefaultAsync(c => c.Id == dto.ClinicId && c.IsActive);
            if (clinic == null)
                return BadRequest("العيادة غير موجودة أو غير مفعّلة");

            var user = new User
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                FullName = dto.FullName,
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Role = dto.Role,
                ClinicId = dto.ClinicId
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            await _db.Entry(user).Reference(u => u.Clinic).LoadAsync();

            return CreatedAtAction(nameof(GetAll), new { id = user.Id }, ToResponse(user));
        }

        // PATCH: api/users/{id}/toggle
        // تفعيل أو تعطيل مستخدم
        [HttpPatch("{id}/toggle")]
        [Authorize(Roles = "SuperAdmin,ClinicStaff,ClinicAdmin")]
        public async Task<ActionResult> Toggle(Guid id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound();

            // ✅ ClinicStaff و ClinicAdmin يعدّلان عيادتهم فقط
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

        // دالة مساعدة
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