using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Auth;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly JwtService _jwtService;

        public AuthController(ApplicationDbContext db, JwtService jwtService)
        {
            _db = db;
            _jwtService = jwtService;
        }

        // ─── Helper ───────────────────────────────────────────────────────────
        private static string Msg(string lang, string ar, string en)
            => lang == "ar" ? ar : en;

        // POST: api/auth/setup
        [HttpPost("setup")]
        public async Task<ActionResult> Setup()
        {
            var exists = await _db.Users.AnyAsync(u => u.Role == "SuperAdmin");
            if (exists) return BadRequest("Super Admin already exists.");

            var superAdmin = new User
            {
                Id = Guid.NewGuid(),
                FullName = "Super Admin",
                Email = "admin@clinicsaas.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                Role = "SuperAdmin",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };
            _db.Users.Add(superAdmin);
            await _db.SaveChangesAsync();
            return Ok("Super Admin created successfully.");
        }

        // POST: api/auth/login
        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login(
            [FromBody] LoginDto dto,
            [FromQuery] string lang = "ar",
            [FromQuery] string? subdomain = null)
        {
            if (string.IsNullOrWhiteSpace(dto.EmailOrUsername))
                return BadRequest(Msg(lang,
                    "البريد الإلكتروني أو اسم المستخدم مطلوب",
                    "Email or username is required"));

            var input = dto.EmailOrUsername.Trim().ToLower();

            var user = await _db.Users
                .Include(u => u.Clinic)
                .FirstOrDefaultAsync(u =>
                    u.IsActive &&
                    (u.Email.ToLower() == input ||
                     (u.Username != null && u.Username.ToLower() == input)));

            if (user == null)
                return Unauthorized(Msg(lang,
                    "البريد أو كلمة المرور غير صحيحة",
                    "Invalid email or password"));

            if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                return Unauthorized(Msg(lang,
                    "البريد أو كلمة المرور غير صحيحة",
                    "Invalid email or password"));

            // ✅ التحقق من انتماء المستخدم للعيادة المختارة
            if (!string.IsNullOrEmpty(subdomain) && subdomain.ToLower() != "admin")
            {
                // SuperAdmin يدخل بأي عيادة بدون قيود
                if (user.Role != "SuperAdmin")
                {
                    var clinic = await _db.Clinics
                        .FirstOrDefaultAsync(c => c.Subdomain == subdomain && c.IsActive);

                    if (clinic == null)
                        return BadRequest(Msg(lang,
                            "العيادة غير موجودة أو غير نشطة",
                            "Clinic not found or inactive"));

                    if (user.ClinicId != clinic.Id)
                        return Unauthorized(Msg(lang,
                            "ليس لديك صلاحية الدخول إلى هذه العيادة",
                            "You are not authorized to access this clinic"));
                }
            }

            // ✅ ربط RoleId تلقائياً إذا كان فارغاً
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

            var token = _jwtService.GenerateToken(user);

            var refreshTokenValue = _jwtService.GenerateRefreshToken();
            var refreshToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                Token = refreshTokenValue,
                UserId = user.Id,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30),
                IsRevoked = false,
            };
            _db.RefreshTokens.Add(refreshToken);
            await _db.SaveChangesAsync();

            return Ok(new AuthResponseDto
            {
                Token = token,
                RefreshToken = refreshTokenValue,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                ClinicId = user.ClinicId,
                ClinicName = user.Clinic?.Name,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                RefreshTokenExpiresAt = refreshToken.ExpiresAt,
            });
        }

        // POST: api/auth/refresh
        [HttpPost("refresh")]
        public async Task<ActionResult<AuthResponseDto>> Refresh([FromBody] RefreshTokenDto dto)
        {
            var refreshToken = await _db.RefreshTokens
                .Include(rt => rt.User).ThenInclude(u => u.Clinic)
                .FirstOrDefaultAsync(rt => rt.Token == dto.RefreshToken);

            if (refreshToken == null) return Unauthorized("Refresh Token غير صحيح");
            if (refreshToken.IsRevoked) return Unauthorized("Refresh Token تم إلغاؤه");
            if (refreshToken.ExpiresAt < DateTime.UtcNow) return Unauthorized("Refresh Token انتهت صلاحيته");
            if (!refreshToken.User.IsActive) return Unauthorized("الحساب غير نشط");

            refreshToken.IsRevoked = true;

            var newToken = _jwtService.GenerateToken(refreshToken.User);
            var newRefreshTokenValue = _jwtService.GenerateRefreshToken();
            var newRefreshToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                Token = newRefreshTokenValue,
                UserId = refreshToken.UserId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30),
                IsRevoked = false,
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
                RefreshTokenExpiresAt = newRefreshToken.ExpiresAt,
            });
        }

        // POST: api/auth/logout
        [HttpPost("logout")]
        public async Task<ActionResult> Logout([FromBody] RefreshTokenDto dto)
        {
            var refreshToken = await _db.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == dto.RefreshToken);

            if (refreshToken == null) return NotFound("Refresh Token غير موجود");

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
                new { Name="patients.view",         Module="patients",      DisplayName="عرض المرضى",       Group="المرضى"       },
                new { Name="patients.create",       Module="patients",      DisplayName="إضافة مريض",       Group="المرضى"       },
                new { Name="patients.edit",         Module="patients",      DisplayName="تعديل مريض",       Group="المرضى"       },
                new { Name="patients.delete",       Module="patients",      DisplayName="حذف مريض",         Group="المرضى"       },
                new { Name="doctors.view",          Module="doctors",       DisplayName="عرض الأطباء",       Group="الأطباء"      },
                new { Name="doctors.create",        Module="doctors",       DisplayName="إضافة طبيب",       Group="الأطباء"      },
                new { Name="doctors.edit",          Module="doctors",       DisplayName="تعديل طبيب",       Group="الأطباء"      },
                new { Name="doctors.delete",        Module="doctors",       DisplayName="حذف طبيب",         Group="الأطباء"      },
                new { Name="appointments.view",     Module="appointments",  DisplayName="عرض المواعيد",     Group="المواعيد"     },
                new { Name="appointments.create",   Module="appointments",  DisplayName="إضافة موعد",       Group="المواعيد"     },
                new { Name="appointments.edit",     Module="appointments",  DisplayName="تعديل موعد",       Group="المواعيد"     },
                new { Name="appointments.delete",   Module="appointments",  DisplayName="حذف موعد",         Group="المواعيد"     },
                new { Name="schedules.view",        Module="schedules",     DisplayName="عرض الجداول",      Group="الجداول"      },
                new { Name="schedules.manage",      Module="schedules",     DisplayName="إدارة الجداول",    Group="الجداول"      },
                new { Name="users.view",            Module="users",         DisplayName="عرض المستخدمين",   Group="المستخدمون"   },
                new { Name="users.create",          Module="users",         DisplayName="إضافة مستخدم",     Group="المستخدمون"   },
                new { Name="departments.manage",    Module="departments",   DisplayName="إدارة الأقسام",     Group="الأقسام"      },
                new { Name="settings.view",         Module="settings",      DisplayName="عرض الإعدادات",     Group="الإعدادات"    },
                new { Name="settings.edit",         Module="settings",      DisplayName="تعديل الإعدادات",   Group="الإعدادات"    },
                new { Name="reports.view",          Module="reports",       DisplayName="عرض التقارير",          Group="التقارير"     },
                new { Name="insurance.view",        Module="insurance",     DisplayName="عرض التأمين الصحي",     Group="'التأمين "     },
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