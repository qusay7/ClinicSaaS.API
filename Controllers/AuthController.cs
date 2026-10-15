using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Auth;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly JwtService _jwtService;
        private readonly IConfiguration _config;

        public AuthController(ApplicationDbContext db, JwtService jwtService, IConfiguration config)
        {
            _db = db;
            _jwtService = jwtService;
            _config = config;
        }

        // ✅ هل هذا المستخدم طبيب مرتبط بقسم من نوع "طوارئ"؟ — نفس منطق إيجاد
        // "الطبيب الحالي" من UserId المستخدم بأماكن أخرى (مثل AppointmentsController)
        private async Task<bool> IsEmergencyDoctorAsync(User user)
        {
            if (user.Role != "Doctor" || user.ClinicId == null) return false;
            var doctor = await _db.Doctors.FirstOrDefaultAsync(d =>
                d.UserId == user.Id && d.ClinicId == user.ClinicId && !d.IsDeleted);
            if (doctor?.DepartmentId == null) return false;
            var dept = await _db.Departments.FirstOrDefaultAsync(dp => dp.Id == doctor.DepartmentId);
            return dept?.Type == DepartmentType.Emergency;
        }

        // ─── Helper ───────────────────────────────────────────────────────────
        private static string Msg(string lang, string ar, string en)
            => lang == "ar" ? ar : en;

        // ✅ حماية brute-force: بعد 5 محاولات فاشلة متتالية يُقفل الحساب 15 دقيقة
        private const int MaxFailedLoginAttempts = 5;
        private const int LockoutMinutes = 15;

        // ✅ Helper لجلب الصلاحيات
        private async Task<List<string>> GetUserPermissionsAsync(User user)
        {
            if (user.Role == "SuperAdmin")
            {
                return await _db.Permissions
                    .Where(p => p.IsActive)
                    .Select(p => p.Name)
                    .ToListAsync();
            }

            // ✅ بدون التحقق من IsActive لأن RolePermission ما فيها هذا العمود
            return await _db.RolePermissions
                .Where(rp => rp.RoleId == user.RoleId)
                .Include(rp => rp.Permission)
                .Select(rp => rp.Permission.Name)
                .ToListAsync();
        }
        [HttpPost("setup")]
        public async Task<ActionResult> Setup([FromQuery] string setupKey, [FromBody] SetupSuperAdminDto dto)
        {
            var expectedKey = _config["Setup:SecretKey"];
            if (string.IsNullOrEmpty(expectedKey) || setupKey != expectedKey)
                return Unauthorized("مفتاح الإعداد غير صحيح");

            var exists = await _db.Users.AnyAsync(u => u.Role == "SuperAdmin");
            if (exists) return BadRequest("Super Admin already exists.");

            if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 8)
                return BadRequest("كلمة المرور يجب أن تكون 8 أحرف على الأقل");

            var superAdmin = new User
            {
                Id = Guid.NewGuid(),
                FullName = dto.FullName ?? "Super Admin",
                Email = dto.Email ?? "admin@clinicsaas.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
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

            if (user.LockoutUntil.HasValue && user.LockoutUntil.Value > DateTime.UtcNow)
            {
                var minutesLeft = Math.Ceiling((user.LockoutUntil.Value - DateTime.UtcNow).TotalMinutes);
                return Unauthorized(Msg(lang,
                    $"الحساب مقفل مؤقتاً بسبب محاولات دخول فاشلة متكررة. حاول بعد {minutesLeft} دقيقة",
                    $"Account temporarily locked due to repeated failed login attempts. Try again in {minutesLeft} minute(s)"));
            }

            if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
                {
                    user.LockoutUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
                    user.FailedLoginAttempts = 0;
                    await _db.SaveChangesAsync();
                    return Unauthorized(Msg(lang,
                        $"الحساب مقفل مؤقتاً بسبب محاولات دخول فاشلة متكررة. حاول بعد {LockoutMinutes} دقيقة",
                        $"Account temporarily locked due to repeated failed login attempts. Try again in {LockoutMinutes} minute(s)"));
                }
                await _db.SaveChangesAsync();
                return Unauthorized(Msg(lang,
                    "البريد أو كلمة المرور غير صحيحة",
                    "Invalid email or password"));
            }

            // ✅ دخول ناجح — نصفّر عداد المحاولات الفاشلة والقفل
            if (user.FailedLoginAttempts != 0 || user.LockoutUntil != null)
            {
                user.FailedLoginAttempts = 0;
                user.LockoutUntil = null;
            }

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
                UserAgent = Request.Headers.UserAgent.ToString(),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            };
            _db.RefreshTokens.Add(refreshToken);
            await _db.SaveChangesAsync();

            // ✅ جلب الصلاحيات
            var permissions = await GetUserPermissionsAsync(user);

            return Ok(new AuthResponseDto
            {
                Token = token,
                RefreshToken = refreshTokenValue,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                ClinicId = user.ClinicId,
                ClinicName = user.Clinic?.Name,
                TimeFormat = user.Clinic?.TimeFormat,
                Country = user.Clinic?.Country,
                Currency = user.Clinic?.Currency,
                Permissions = permissions, // ✅ جديد
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                RefreshTokenExpiresAt = refreshToken.ExpiresAt,
                IsEmergencyDoctor = await IsEmergencyDoctorAsync(user),
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
                UserAgent = Request.Headers.UserAgent.ToString(),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            };
            _db.RefreshTokens.Add(newRefreshToken);
            await _db.SaveChangesAsync();

            // ✅ جلب الصلاحيات
            var permissions = await GetUserPermissionsAsync(refreshToken.User);

            return Ok(new AuthResponseDto
            {
                Token = newToken,
                RefreshToken = newRefreshTokenValue,
                FullName = refreshToken.User.FullName,
                Email = refreshToken.User.Email,
                Role = refreshToken.User.Role,
                ClinicId = refreshToken.User.ClinicId,
                ClinicName = refreshToken.User.Clinic?.Name,
                TimeFormat = refreshToken.User.Clinic?.TimeFormat,
                Country = refreshToken.User.Clinic?.Country,
                Currency = refreshToken.User.Clinic?.Currency,
                Permissions = permissions, // ✅ جديد
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                RefreshTokenExpiresAt = newRefreshToken.ExpiresAt,
                IsEmergencyDoctor = await IsEmergencyDoctorAsync(refreshToken.User),
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

        // GET: api/auth/sessions — أجهزة/متصفحات المستخدم الحالي المسجّلة دخول فعلياً
        [HttpGet("sessions")]
        [Authorize]
        public async Task<ActionResult> GetSessions()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var uid)) return Unauthorized();

            var sessions = await _db.RefreshTokens
                .Where(rt => rt.UserId == uid && !rt.IsRevoked && rt.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(rt => rt.CreatedAt)
                .Select(rt => new
                {
                    id = rt.Id,
                    userAgent = rt.UserAgent,
                    ipAddress = rt.IpAddress,
                    createdAt = rt.CreatedAt,
                    expiresAt = rt.ExpiresAt,
                })
                .ToListAsync();

            return Ok(sessions);
        }

        // DELETE: api/auth/sessions/{id} — يسحب صلاحية جهاز معيّن بدون تغيير كلمة المرور
        [HttpDelete("sessions/{id}")]
        [Authorize]
        public async Task<ActionResult> RevokeSession(Guid id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var uid)) return Unauthorized();

            var session = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Id == id);
            if (session == null) return NotFound();
            // ✅ ما يقدر أحد يسحب جلسة مستخدم ثاني — بس جلساته هو
            if (session.UserId != uid) return Forbid();

            session.IsRevoked = true;
            await _db.SaveChangesAsync();
            return Ok(new { message = "تم إنهاء الجلسة" });
        }

        // GET: api/auth/permissions
        [HttpGet("permissions")]
        [Authorize]
        public async Task<ActionResult<List<string>>> GetUserPermissions()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _db.Users
                .FirstOrDefaultAsync(u => u.Id.ToString() == userId);

            if (user == null) return Unauthorized();

            var permissions = await GetUserPermissionsAsync(user);
            return Ok(permissions);
        }

        // GET: api/auth/me
        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<UserWithPermissionsDto>> GetCurrentUser()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _db.Users
                .Include(u => u.Clinic)
                .FirstOrDefaultAsync(u => u.Id.ToString() == userId);

            if (user == null) return Unauthorized();

            // جلب الصلاحيات
            var permissions = await GetUserPermissionsAsync(user);

            return Ok(new UserWithPermissionsDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                ClinicId = user.ClinicId,
                ClinicName = user.Clinic?.Name,
                Permissions = permissions,
            });
        }

        // POST: api/auth/seed-permissions
        [HttpPost("seed-permissions")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult> SeedPermissions()
        {
            var permissions = new[]
 {
    new { Name="patients.view",         Module="patients",      DisplayName="عرض المرضى",            Group="المرضى"       },
    new { Name="patients.create",       Module="patients",      DisplayName="إضافة مريض",            Group="المرضى"       },
    new { Name="patients.edit",         Module="patients",      DisplayName="تعديل مريض",            Group="المرضى"       },
    new { Name="patients.delete",       Module="patients",      DisplayName="حذف مريض",              Group="المرضى"       },

    new { Name="doctors.view",          Module="doctors",       DisplayName="عرض الأطباء",            Group="الأطباء"       },
    new { Name="doctors.create",        Module="doctors",       DisplayName="إضافة طبيب",            Group="الأطباء"       },
    new { Name="doctors.edit",          Module="doctors",       DisplayName="تعديل طبيب",            Group="الأطباء"       },
    new { Name="doctors.delete",        Module="doctors",       DisplayName="حذف طبيب",              Group="الأطباء"       },

    new { Name="appointments.view",     Module="appointments",  DisplayName="عرض المواعيد",          Group="المواعيد"     },
    new { Name="appointments.create",   Module="appointments",  DisplayName="إضافة موعد",            Group="المواعيد"     },
    new { Name="appointments.edit",     Module="appointments",  DisplayName="تعديل موعد",            Group="المواعيد"     },
    new { Name="appointments.delete",   Module="appointments",  DisplayName="حذف موعد",              Group="المواعيد"     },
    
    // ✅ صلاحيات الجداول الجديدة (مفصلة)
    new { Name="schedules.clinic.view",    Module="schedules",     DisplayName="عرض دوام العيادة",      Group="الجداول"      },
    new { Name="schedules.clinic.add",     Module="schedules",     DisplayName="إضافة دوام عيادة",      Group="الجداول"      },
    new { Name="schedules.clinic.edit",    Module="schedules",     DisplayName="تعديل دوام عيادة",      Group="الجداول"      },
    new { Name="schedules.clinic.delete",  Module="schedules",     DisplayName="حذف دوام عيادة",        Group="الجداول"      },

    new { Name="schedules.doctor.view",    Module="schedules",     DisplayName="عرض دوام الأطباء",      Group="الجداول"      },
    new { Name="schedules.doctor.add",     Module="schedules",     DisplayName="إضافة دوام طبيب",       Group="الجداول"      },
    new { Name="schedules.doctor.edit",    Module="schedules",     DisplayName="تعديل دوام طبيب",       Group="الجداول"      },
    new { Name="schedules.doctor.delete",  Module="schedules",     DisplayName="حذف دوام طبيب",         Group="الجداول"      },
    new { Name="schedules.doctor.editown", Module="schedules",     DisplayName="تعديل جدولي الخاص",    Group="الجداول"      },

    new { Name="schedules.absence.view",   Module="schedules",     DisplayName="عرض الإجازات",         Group="الجداول"      },
    new { Name="schedules.absence.add",    Module="schedules",     DisplayName="إضافة إجازة",          Group="الجداول"      },
    new { Name="schedules.absence.edit",   Module="schedules",     DisplayName="تعديل إجازة",          Group="الجداول"      },
    new { Name="schedules.absence.delete", Module="schedules",     DisplayName="حذف إجازة",            Group="الجداول"      },

    new { Name="users.view",            Module="users",         DisplayName="عرض المستخدمين",        Group="المستخدمون"   },
    new { Name="users.create",          Module="users",         DisplayName="إضافة مستخدم",          Group="المستخدمون"   },
    new { Name="users.toggle",          Module="users",         DisplayName="تفعيل/تعطيل مستخدم",     Group="المستخدمون"   },

    new { Name="departments.manage",    Module="departments",   DisplayName="إدارة الأقسام",          Group="الأقسام"       },

    new { Name="settings.view",         Module="settings",      DisplayName="عرض الإعدادات",          Group="الإعدادات"     },
    new { Name="settings.edit",         Module="settings",      DisplayName="تعديل الإعدادات",        Group="الإعدادات"     },

    new { Name="subscription.view",     Module="subscription",  DisplayName="عرض الباقة الحالية",     Group="الباقة"       },

    new { Name="reports.view",          Module="reports",       DisplayName="عرض التقارير",          Group="التقارير"     },
    new { Name="reports.export",        Module="reports",       DisplayName="تصدير البيانات (طباعة/PDF/Excel)", Group="التقارير" },

    new { Name="insurance.view",        Module="insurance",     DisplayName="عرض التأمين الصحي",     Group="التأمين"      },
    new { Name="insurance.manage",      Module="insurance",     DisplayName="إدارة التأمين الصحي",   Group="التأمين"      },

    new { Name="payments.view",         Module="payments",      DisplayName="المدفوعات",             Group="المدفوعات"   },
    new { Name="payments.manage",       Module="payments",      DisplayName="إدارة المدفوعات",       Group="المدفوعات"   },

    new { Name="invoices.manage",       Module="invoices",      DisplayName="إدارة الفواتير",        Group="الفواتير"     },

    new { Name="staff.view",            Module="staff",         DisplayName="فريق العمل",            Group="فريق العمل"   },
    new { Name="staff.manage",          Module="staff",         DisplayName="إدارة فريق العمل",      Group="فريق العمل"   },
    new { Name="staff.viewsalary",      Module="staff",         DisplayName="عرض الراتب",            Group="فريق العمل"   },

    new { Name="queue.manage",          Module="queue",         DisplayName="إدارة الطابور",         Group="الطابور"      },

    // ✅ صلاحيات مخصّصة للطوارئ — منفصلة عن queue.manage عشان تقدر تعطي موظف
    // الاستقبال صلاحية تسجيل الدخول فقط، بدون صلاحية إدارة الحالة كاملة (قراءات/إجراءات/خروج)
    new { Name="emergency.checkin",     Module="emergency",     DisplayName="تسجيل دخول الطوارئ",    Group="الطوارئ"      },
    new { Name="emergency.manage",      Module="emergency",     DisplayName="إدارة لوحة ومتابعة الطوارئ", Group="الطوارئ" },
    // ✅ بدون هذي، الحالة تُقفل كلياً بمجرد تسجيل الخروج (زي ما كانت من قبل) —
    // هذي الصلاحية تفتح تعديل محدود (قراءات/تشخيص/إجراءات) حتى بعد الخروج، لتصحيح
    // خطأ أو توثيق شي نُسي، بدون ما تكون متاحة افتراضياً لكل من يملك emergency.manage
    new { Name="emergency.editdischarged", Module="emergency",   DisplayName="تعديل حالة طوارئ بعد الخروج", Group="الطوارئ" },

    // ✅ طلب تحليل مخبري متاح لأي حدا يقدر يوثّق زيارة (visitnotes.create) —
    // هذي بس لقسم المختبر نفسه: شوف قائمة الطلبات ونفّذها وارفع النتيجة
    new { Name="lab.manage",            Module="lab",            DisplayName="إدارة طلبات المختبر",    Group="المختبر"      },
    // ✅ الوصفة نص حر موجود أصلاً بملاحظة الزيارة — هذي بس تتيح لقسم الصيدلية
    // يشوف قائمة الوصفات غير المصروفة ويعلّمها "تم الصرف"، بدون نظام مخزون
    new { Name="pharmacy.manage",       Module="pharmacy",       DisplayName="إدارة صرف الوصفات",      Group="الصيدلية"     },

    new { Name="visitnotes.view",       Module="visitnotes",    DisplayName="عرض ملاحظات الزيارة",    Group="ملاحظات الزيارة" },
    new { Name="visitnotes.create",     Module="visitnotes",    DisplayName="إضافة ملاحظة زيارة",     Group="ملاحظات الزيارة" },
    new { Name="visitnotes.edit",       Module="visitnotes",    DisplayName="تعديل ملاحظة زيارة",     Group="ملاحظات الزيارة" },

    new { Name="treatmenttemplates.manage", Module="treatmenttemplates", DisplayName="إدارة قوالب الزيارة", Group="قوالب الزيارة" },
    new { Name="diagnosistemplates.manage", Module="diagnosistemplates", DisplayName="إدارة اقتراحات التشخيص والأدوية", Group="قوالب الزيارة" },
    new { Name="procedures.manage", Module="procedures", DisplayName="إدارة كتالوج الإجراءات", Group="قوالب الزيارة" },

    new { Name="settlements.manage", Module="settlements", DisplayName="إدارة التسويات المالية", Group="التسويات المالية" },

    new { Name="daily.view",         Module="daily",       DisplayName="جدول اليوم",              Group="جدول اليوم" },
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