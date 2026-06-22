
using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Auth;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace ClinicSaaS.API.Controllers
{
    
        [ApiController]
        [Route("api/[controller]")]
    // هذا الكونترولر مسؤول عن عمليات التوثيق (Authentication) مثل تسجيل الدخول
    // سنستخدم ApplicationDbContext للوصول إلى بيانات المستخدمين في قاعدة البيانات

       
    public class AuthController : ControllerBase
        {
               


        // حقن ApplicationDbContext للوصول لقاعدة البيانات  
        private readonly ApplicationDbContext _db;//   → للوصول لقاعدة البيانات(البحث عن المستخدم)
        // حقن JwtService لتوليد رموز JWT عند تسجيل الدخول
        private readonly JwtService _jwtService; //لتوليد التوكن بعد التحقق من المستخدم

        // في الكونستركتور، نستقبل الـ ApplicationDbContext والـ JwtService من خلال Dependency Injection
        public AuthController(ApplicationDbContext db, JwtService jwtService)
            {
                _db = db;
                _jwtService = jwtService;
            }
        // POST: api/auth/setup
        // ⚠️ مؤقت فقط — سنحذفه بعد إنشاء Super Admin
        [HttpPost("setup")]
        public async Task<ActionResult> Setup()
        {
            // تحقق إذا كان Super Admin موجوداً مسبقاً
            // حتى لا يتم إنشاؤه مرتين
            var exists = await _db.Users
                .AnyAsync(u => u.Role == "SuperAdmin");

            if (exists)
                return BadRequest("Super Admin already exists.");

                // إذا لم يكن هناك مستخدم، ننشئ Super Admin جديد

                var superAdmin = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Super Admin",
                    Email = "admin@clinicsaas.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"), // كلمة مرور قوية مشفّرة
                    Role = "SuperAdmin",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                   // ClinicId = Guid.Empty // Super Admin لا ينتمي لأي عيادة

                };

                _db.Users.Add(superAdmin);
                await _db.SaveChangesAsync();
                return Ok("Super Admin created successfully.");
           

        }

        // POST: api/auth/login
        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.EmailOrUsername))
                return BadRequest("البريد الإلكتروني أو اسم المستخدم مطلوب");
            // ✅ ابحث بـ Email أو Username
            var input = dto.EmailOrUsername.Trim().ToLower();

            var user = await _db.Users
       .Include(u => u.Clinic)
       .FirstOrDefaultAsync(u =>
           u.IsActive &&
           (u.Email.ToLower() == input ||
            (u.Username != null && u.Username.ToLower() == input))
       );



            //2- إذا لم يتم العثور على المستخدم، نرجع رسالة خطأ
            if (user == null)
                return Unauthorized("Invalid email or password.");

            //3- التحقق من كلمة المرور باستخدام BCrypt
            if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                return Unauthorized("Invalid email or password.");


            // ✅ ربط RoleId تلقائياً إذا كان فارغاً
            // بعد التحقق من كلمة المرور
            // ✅ ربط RoleId تلقائياً
            if (user.RoleId == null)
            {
                var role = await _db.Roles
                    .FirstOrDefaultAsync(r => r.Name == user.Role);
                if (role != null)
                {
                    user.RoleId = role.Id;
                    await _db.SaveChangesAsync();
                }
            }


            //4- إذا كانت بيانات الاعتماد صحيحة، نولد رمز JWT يحتوي على معلومات المستخدم
            var token = _jwtService.GenerateToken(user);

            // ✅ توليد Refresh Token وحفظه في DB
            var refreshTokenValue = _jwtService.GenerateRefreshToken();
            var refreshToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                Token = refreshTokenValue,
                UserId = user.Id,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30), // ← صالح 30 يوم
                IsRevoked = false
            };
            _db.RefreshTokens.Add(refreshToken);
            await _db.SaveChangesAsync();

            return Ok(new AuthResponseDto
            {
                Token = token,
                RefreshToken = refreshTokenValue,   // ✅
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                ClinicId = user.ClinicId,
                ClinicName = user.Clinic?.Name,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                RefreshTokenExpiresAt = refreshToken.ExpiresAt // ✅
            });
        }

        // POST: api/auth/refresh
        [HttpPost("refresh")]
        public async Task<ActionResult<AuthResponseDto>> Refresh([FromBody] RefreshTokenDto dto)
        {
            // 1 — البحث عن الـ Refresh Token في DB
            var refreshToken = await _db.RefreshTokens
                .Include(rt => rt.User)
                .ThenInclude(u => u.Clinic)
                .FirstOrDefaultAsync(rt => rt.Token == dto.RefreshToken);

            // 2 — التحقق من صحته
            if (refreshToken == null)
                return Unauthorized("Refresh Token غير صحيح");

            if (refreshToken.IsRevoked)
                return Unauthorized("Refresh Token تم إلغاؤه");

            if (refreshToken.ExpiresAt < DateTime.UtcNow)
                return Unauthorized("Refresh Token انتهت صلاحيته");

            if (!refreshToken.User.IsActive)
                return Unauthorized("الحساب غير نشط");

            // 3 — إلغاء الـ Refresh Token القديم
            refreshToken.IsRevoked = true;

            // 4 — توليد Access Token جديد
            var newToken = _jwtService.GenerateToken(refreshToken.User);

            // 5 — توليد Refresh Token جديد
            var newRefreshTokenValue = _jwtService.GenerateRefreshToken();
            var newRefreshToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                Token = newRefreshTokenValue,
                UserId = refreshToken.UserId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30),
                IsRevoked = false
            };

            _db.RefreshTokens.Add(newRefreshToken);
            await _db.SaveChangesAsync();

            return Ok(new AuthResponseDto
            {
                Token = newToken,
                RefreshToken = newRefreshTokenValue,
                FullName = refreshToken.User.FullName,
                Email = refreshToken.User.Email,
                Role = refreshToken.User.Role,
                ClinicId = refreshToken.User.ClinicId,
                ClinicName = refreshToken.User.Clinic?.Name,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                RefreshTokenExpiresAt = newRefreshToken.ExpiresAt
            });
        }

        // POST: api/auth/logout
        [HttpPost("logout")]
        public async Task<ActionResult> Logout([FromBody] RefreshTokenDto dto)
        {
            var refreshToken = await _db.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == dto.RefreshToken);

            if (refreshToken == null)
                return NotFound("Refresh Token غير موجود");

            // إلغاء الـ Refresh Token عند تسجيل الخروج
            refreshToken.IsRevoked = true;
            await _db.SaveChangesAsync();

            return Ok(new { message = "تم تسجيل الخروج بنجاح" });
        }

        // POST: api/auth/seed-permissions
        [HttpPost("seed-permissions")]
        public async Task<ActionResult> SeedPermissions()
        {
            var permissions = new[]
            {
        // Patients
        new { Name = "patients.view",   Module = "patients", DisplayName = "عرض المرضى",       Group = "المرضى" },
        new { Name = "patients.create", Module = "patients", DisplayName = "إضافة مريض",       Group = "المرضى" },
        new { Name = "patients.edit",   Module = "patients", DisplayName = "تعديل مريض",       Group = "المرضى" },
        new { Name = "patients.delete", Module = "patients", DisplayName = "حذف مريض",         Group = "المرضى" },
        // Doctors
        new { Name = "doctors.view",    Module = "doctors",  DisplayName = "عرض الأطباء",      Group = "الأطباء" },
        new { Name = "doctors.create",  Module = "doctors",  DisplayName = "إضافة طبيب",       Group = "الأطباء" },
        new { Name = "doctors.edit",    Module = "doctors",  DisplayName = "تعديل طبيب",       Group = "الأطباء" },
        new { Name = "doctors.delete",  Module = "doctors",  DisplayName = "حذف طبيب",         Group = "الأطباء" },
        // Appointments
        new { Name = "appointments.view",   Module = "appointments", DisplayName = "عرض المواعيد",  Group = "المواعيد" },
        new { Name = "appointments.create", Module = "appointments", DisplayName = "إضافة موعد",    Group = "المواعيد" },
        new { Name = "appointments.edit",   Module = "appointments", DisplayName = "تعديل موعد",    Group = "المواعيد" },
        new { Name = "appointments.delete", Module = "appointments", DisplayName = "حذف موعد",      Group = "المواعيد" },
        // Schedules
        new { Name = "schedules.view",   Module = "schedules", DisplayName = "عرض الجداول",    Group = "الجداول" },
        new { Name = "schedules.manage", Module = "schedules", DisplayName = "إدارة الجداول",  Group = "الجداول" },
        // Users
        new { Name = "users.view",   Module = "users", DisplayName = "عرض المستخدمين",        Group = "المستخدمون" },
        new { Name = "users.create", Module = "users", DisplayName = "إضافة مستخدم",          Group = "المستخدمون" },
        // Departments
        new { Name = "departments.manage", Module = "departments", DisplayName = "إدارة الأقسام", Group = "الأقسام" },
        // Settings
        new { Name = "settings.view", Module = "settings", DisplayName = "عرض الإعدادات",     Group = "الإعدادات" },
        new { Name = "settings.edit", Module = "settings", DisplayName = "تعديل الإعدادات",   Group = "الإعدادات" },
        // Reports
        new { Name = "reports.view", Module = "reports", DisplayName = "عرض التقارير",        Group = "التقارير" },
    };

            int added = 0;
            foreach (var p in permissions)
            {
                var exists = await _db.Permissions.AnyAsync(x => x.Name == p.Name);
                if (!exists)
                {
                    _db.Permissions.Add(new Permission
                    {
                        Id = Guid.NewGuid(),
                        Name = p.Name,
                        Module = p.Module,
                        DisplayName = p.DisplayName,
                        Group = p.Group,
                        IsActive = true,
                    });
                    added++;
                }
            }
            await _db.SaveChangesAsync();
            return Ok($"تم إنشاء {added} صلاحية بنجاح");
        }



    }
  
}
