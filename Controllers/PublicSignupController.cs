using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Public;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    // ✅ نقطة عامة واحدة (بدون أي مصادقة) لزر "ابدأ مجاناً" بالموقع التسويقي —
    // تنشئ عيادة + مدير عيادة + اشتراك تجريبي 30 يوم + الأدوار/الأقسام/قوالب
    // الزيارة الافتراضية، بمعاملة واحدة، ثم تُرجع توكنات دخول جاهزة (دخول تلقائي
    // بدون الحاجة لإعادة كتابة بيانات الدخول). عمداً endpoint واحد بدل كشف
    // /clinics /users /subscriptions /roles/seed-defaults... للعامة — كل واحدة
    // منها تفترض مستخدم SuperAdmin موثوق به ولا تتحقق من ملكية الـclinicId
    [ApiController]
    [Route("api/public")]
    [AllowAnonymous]
    public class PublicSignupController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly JwtService _jwtService;
        private readonly IRoleSeedingService _roleSeedingService;
        private readonly IDepartmentSeedingService _departmentSeedingService;
        private readonly ITreatmentTemplateSeedingService _treatmentTemplateSeedingService;

        // ✅ اسم ثابت يحدد "خطة التجربة المجانية" — تُزرع تلقائياً عند بدء
        // التطبيق (Program.cs) لو لم تكن موجودة، بدل الاعتماد على GUID ثابت
        // بالكود قد يختلف بين بيئة وأخرى
        public const string FreeTrialPlanName = "تجربة مجانية";
        private const int TrialDays = 30;

        // ✅ محجوزة مسبقاً — تمنع إنشاء عيادة بنطاق فرعي قد يتعارض مع مسارات
        // النظام نفسه (admin.cura.jo، api.cura.jo...)
        private static readonly HashSet<string> ReservedSubdomains = new()
        {
            "admin", "api", "www", "app", "static", "support", "mail", "dashboard", "cura",
        };

        public PublicSignupController(
            ApplicationDbContext db,
            JwtService jwtService,
            IRoleSeedingService roleSeedingService,
            IDepartmentSeedingService departmentSeedingService,
            ITreatmentTemplateSeedingService treatmentTemplateSeedingService)
        {
            _db = db;
            _jwtService = jwtService;
            _roleSeedingService = roleSeedingService;
            _departmentSeedingService = departmentSeedingService;
            _treatmentTemplateSeedingService = treatmentTemplateSeedingService;
        }

        private static string Msg(string lang, string ar, string en) => lang == "ar" ? ar : en;

        // POST: api/public/signup
        [HttpPost("signup")]
        [EnableRateLimiting("signup")]
        public async Task<ActionResult<PublicSignupResponseDto>> Signup([FromBody] PublicSignupDto dto, [FromQuery] string lang = "ar")
        {
            if (string.IsNullOrWhiteSpace(dto.ClinicName))
                return BadRequest(Msg(lang, "اسم العيادة مطلوب", "Clinic name is required"));

            if (string.IsNullOrWhiteSpace(dto.OwnerName))
                return BadRequest(Msg(lang, "اسم المالك مطلوب", "Owner name is required"));

            if (string.IsNullOrWhiteSpace(dto.OwnerEmail) && string.IsNullOrWhiteSpace(dto.OwnerPhone))
                return BadRequest(Msg(lang, "أدخل بريداً إلكترونياً أو رقم هاتف للتواصل", "Enter an email or phone number"));

            // ✅ رسالة واحدة موحّدة لكل أسباب رفض النطاق الفرعي (صيغة غير صالحة/محجوز/
            // مستخدم مسبقاً) — عمداً لا نميّز "مستخدم مسبقاً" عن باقي الأسباب، حتى لا
            // يتحوّل هذا الـ endpoint العام لأداة يتحقق بها أي شخص مجهول من وجود عيادة
            // معيّنة (استكشاف/enumeration) بمجرد تجربة أسماء نطاقات مختلفة
            var subdomainRejected = Msg(lang,
                "تعذّر استخدام هذا الرابط، جرّب رابطاً آخر",
                "This link isn't available, please try a different one");

            var subdomain = dto.Subdomain?.Trim().ToLower() ?? "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(subdomain, "^[a-z0-9._-]+$"))
                return BadRequest(subdomainRejected);

            if (ReservedSubdomains.Contains(subdomain))
                return BadRequest(subdomainRejected);

            var subdomainExists = await _db.Clinics.AnyAsync(c => c.Subdomain == subdomain);
            if (subdomainExists)
                return BadRequest(subdomainRejected);

            var freePlan = await _db.Plans.FirstOrDefaultAsync(p => p.Name == FreeTrialPlanName);
            if (freePlan == null)
                return StatusCode(500, Msg(lang,
                    "خطة التجربة المجانية غير متوفرة حالياً، حاول لاحقاً",
                    "The free trial plan is not available right now, please try again later"));

            var clinic = new Clinic
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                Name = dto.ClinicName.Trim(),
                Subdomain = subdomain,
                OwnerName = dto.OwnerName.Trim(),
                OwnerEmail = dto.OwnerEmail?.Trim(),
                OwnerPhone = dto.OwnerPhone?.Trim(),
                Email = dto.OwnerEmail?.Trim(),
                Phone = dto.OwnerPhone?.Trim(),
            };

            // ✅ اسم المستخدم/البريد مبنيان على النطاق الفرعي (علني، غير سرّي أصلاً)،
            // لكن كلمة المرور عشوائية حقيقية — غير قابلة للاستنتاج من رابط العيادة
            var username = subdomain;
            var generatedPassword = PasswordGenerator.Generate();

            var adminUser = new User
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                FullName = dto.OwnerName.Trim(),
                Username = username,
                Email = $"{subdomain}@CURA.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(generatedPassword),
                Role = "ClinicAdmin",
                ClinicId = clinic.Id,
            };

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                _db.Clinics.Add(clinic);
                await _db.SaveChangesAsync();

                _db.Users.Add(adminUser);
                await _db.SaveChangesAsync();

                // ✅ ينشئ الأدوار الافتراضية ويربط adminUser.RoleId تلقائياً
                // (RoleSeedingService.LinkUsersToRole) — لازم قبل احتساب الصلاحيات تحت
                await _roleSeedingService.SeedDefaultRoles(clinic.Id);
                await _departmentSeedingService.SeedDefaultDepartments(clinic.Id);
                await _treatmentTemplateSeedingService.SeedDefaultTemplates(clinic.Id);

                var now = DateTime.UtcNow;
                var trialEndsAt = now.AddDays(TrialDays);

                _db.Subscriptions.Add(new Subscription
                {
                    Id = Guid.NewGuid(),
                    ClinicId = clinic.Id,
                    PlanId = freePlan.Id,
                    BillingCycle = "trial",
                    PricePaid = 0,
                    StartDate = now,
                    EndDate = trialEndsAt,
                    ExtraUserAllowance = 0,
                });
                await _db.SaveChangesAsync();

                await transaction.CommitAsync();

                var token = _jwtService.GenerateToken(adminUser);
                var refreshTokenValue = _jwtService.GenerateRefreshToken();
                var refreshToken = new RefreshToken
                {
                    Id = Guid.NewGuid(),
                    Token = refreshTokenValue,
                    UserId = adminUser.Id,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(30),
                    IsRevoked = false,
                    UserAgent = Request.Headers.UserAgent.ToString(),
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                };
                _db.RefreshTokens.Add(refreshToken);
                await _db.SaveChangesAsync();

                var permissions = await _db.RolePermissions
                    .Where(rp => rp.RoleId == adminUser.RoleId)
                    .Include(rp => rp.Permission)
                    .Select(rp => rp.Permission.Name)
                    .ToListAsync();

                return Ok(new PublicSignupResponseDto
                {
                    Token = token,
                    RefreshToken = refreshTokenValue,
                    FullName = adminUser.FullName,
                    Email = adminUser.Email,
                    Role = adminUser.Role,
                    ClinicId = clinic.Id,
                    ClinicName = clinic.Name,
                    TimeFormat = clinic.TimeFormat,
                    Country = clinic.Country,
                    Currency = clinic.Currency,
                    Permissions = permissions,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    RefreshTokenExpiresAt = refreshToken.ExpiresAt,
                    IsEmergencyDoctor = false,
                    Subdomain = subdomain,
                    Username = username,
                    Password = generatedPassword,
                    TrialEndsAt = trialEndsAt,
                });
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                return BadRequest(subdomainRejected);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
