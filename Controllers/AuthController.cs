
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





    }
  
}
