using System.Security.Claims;
using ClinicSaaS.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Services
{
    public class ClinicContext : IClinicContext
    {
        private readonly ApplicationDbContext _db;

        public Guid? ClinicId { get; private set; }
        public string? Role { get; private set; }
        public Guid? UserId { get; private set; }
        public List<string> Permissions { get; private set; } = new();
        public bool IsSuperAdmin => Role == "SuperAdmin";
        public bool IsCompanyStaff => Role == "SuperAdmin" || Role == "ClinicStaff";
        public bool IsClinicUser => ClinicId != null;
        public Guid? RoleId { get; private set; }
        public bool HasElectronicInvoicing { get; private set; }
        public bool HasMultipleDepartments { get; private set; }
        public bool NotifyOnCreate { get; private set; } = true;
        public bool NotifyOnEdit { get; private set; } = true;
        public bool NotifyOnCancel { get; private set; } = true;

        // ✅ الـ constructor بقى خفيف تماماً — فقط قراءة claims من التوكن (CPU فقط،
        // بدون أي استعلام DB). استعلامات قاعدة البيانات انتقلت لـ InitializeAsync()
        // اللي تستدعيها middleware واحدة بعد UseAuthentication (Program.cs) بشكل
        // async حقيقي، بدل ما كانت سابقاً sync (.FirstOrDefault/.ToList) داخل
        // الـ constructor نفسه — هذا كان يحجز thread كامل من ASP.NET Core
        // ThreadPool بانتظار كل استعلام (حتى 4 استعلامات متتالية) لكل طلب مصادَق
        // عليه تقريباً بالنظام، وتحت تزامن عالي (اختبار حمل k6 كشفها: p95 قفز
        // من <100ms إلى 50 ثانية فوق ~100 مستخدم متزامن) هذا يُجمّد الـ ThreadPool
        // كامل لأنه لا يتوسّع بالسرعة الكافية لتعويض الـ threads المحجوزة
        public ClinicContext(IHttpContextAccessor httpContextAccessor, ApplicationDbContext db)
        {
            _db = db;

            var user = httpContextAccessor.HttpContext?.User;
            if (user == null) return;

            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
                UserId = userId;

            Role = user.FindFirst(ClaimTypes.Role)?.Value;

            var clinicIdClaim = user.FindFirst("ClinicId")?.Value;
            if (Guid.TryParse(clinicIdClaim, out var clinicId))
                ClinicId = clinicId;

            if (IsCompanyStaff)
            {
                HasElectronicInvoicing = true;
                HasMultipleDepartments = true;
            }
        }

        // ✅ تُستدعى مرة واحدة فقط بكل طلب (عبر middleware) — تجمع الاستعلامات
        // الأربعة القديمة بـ2 استعلام async فقط (الصلاحيات دمجناها مع جلب
        // RoleId بنفس الاستعلام، وخطة+إشعارات العيادة بنفس الاستعلام)
        public async Task InitializeAsync()
        {
            if (!UserId.HasValue) return;

            if (!IsSuperAdmin && ClinicId.HasValue)
            {
                try
                {
                    var userWithPerms = await _db.Users
                        .AsNoTracking()
                        .Where(u => u.Id == UserId.Value)
                        .Select(u => new
                        {
                            u.RoleId,
                            Permissions = _db.RolePermissions
                                .Where(rp => rp.RoleId == u.RoleId && (rp.ClinicId == ClinicId || rp.ClinicId == null))
                                .Select(rp => new { rp.ClinicId, Name = rp.Permission.Name })
                                .ToList(),
                        })
                        .FirstOrDefaultAsync();

                    if (userWithPerms != null)
                    {
                        RoleId = userWithPerms.RoleId;

                        var clinicPerms = userWithPerms.Permissions
                            .Where(rp => rp.ClinicId == ClinicId)
                            .Select(rp => rp.Name)
                            .ToList();

                        Permissions = clinicPerms.Any()
                            ? clinicPerms
                            : userWithPerms.Permissions
                                .Where(rp => rp.ClinicId == null)
                                .Select(rp => rp.Name)
                                .ToList();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ClinicContext error: {ex.Message}");
                }
            }

            if (!IsCompanyStaff && ClinicId.HasValue)
            {
                try
                {
                    var clinicData = await _db.Clinics
                        .AsNoTracking()
                        .Where(c => c.Id == ClinicId)
                        .Select(c => new
                        {
                            c.NotifyOnCreate,
                            c.NotifyOnEdit,
                            c.NotifyOnCancel,
                            Plan = _db.Subscriptions
                                .Where(s => s.ClinicId == c.Id && s.IsActive)
                                .OrderByDescending(s => s.EndDate)
                                .Select(s => new { s.Plan.HasElectronicInvoicing, s.Plan.HasMultipleDepartments })
                                .FirstOrDefault(),
                        })
                        .FirstOrDefaultAsync();

                    if (clinicData != null)
                    {
                        NotifyOnCreate = clinicData.NotifyOnCreate;
                        NotifyOnEdit = clinicData.NotifyOnEdit;
                        NotifyOnCancel = clinicData.NotifyOnCancel;
                        HasElectronicInvoicing = clinicData.Plan?.HasElectronicInvoicing ?? false;
                        HasMultipleDepartments = clinicData.Plan?.HasMultipleDepartments ?? false;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ClinicContext plan/notify error: {ex.Message}");
                }
            }
        }

        public bool HasPermission(string permission)
        {
            if (IsSuperAdmin) return true;
            return Permissions.Contains(permission);
        }
    }
}
