using System.Security.Claims;
using ClinicSaaS.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Services
{
    public class ClinicContext : IClinicContext
    {
        public Guid? ClinicId { get; }
        public string? Role { get; }
        public Guid? UserId { get; }
        public List<string> Permissions { get; } = new();
        public bool IsSuperAdmin => Role == "SuperAdmin";
        public bool IsCompanyStaff => Role == "SuperAdmin" || Role == "ClinicStaff";
        public bool IsClinicUser => ClinicId != null;

        public ClinicContext(
            IHttpContextAccessor httpContextAccessor,
            ApplicationDbContext db)
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (user == null) return;

            // قراءة UserId
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
                UserId = userId;

            // قراءة Role
            Role = user.FindFirst(ClaimTypes.Role)?.Value;

            // قراءة ClinicId
            var clinicIdClaim = user.FindFirst("ClinicId")?.Value;
            if (Guid.TryParse(clinicIdClaim, out var clinicId))
                ClinicId = clinicId;

            // ✅ تحميل الصلاحيات من DB
            // ✅ تحميل الصلاحيات من DB بشكل صحيح
            if (UserId.HasValue && !IsSuperAdmin)
            {
                try
                {
                    var dbUser = db.Users
                        .AsNoTracking()  // ✅ أسرع — لا نحتاج tracking
                        .FirstOrDefault(u => u.Id == UserId.Value);

                    if (dbUser?.RoleId != null && ClinicId.HasValue)
                    {
                        // ✅ query واحد بدل اثنين
                        var perms = db.RolePermissions
                            .AsNoTracking()
                            .Include(rp => rp.Permission)
                            .Where(rp => rp.RoleId == dbUser.RoleId
                                && (rp.ClinicId == ClinicId || rp.ClinicId == null))
                            .ToList();

                        // ✅ أولاً خاصة بالعيادة، وإلا الافتراضية
                        var clinicPerms = perms
                            .Where(rp => rp.ClinicId == ClinicId)
                            .Select(rp => rp.Permission.Name)
                            .ToList();

                        Permissions = clinicPerms.Any()
                            ? clinicPerms
                            : perms.Where(rp => rp.ClinicId == null)
                                   .Select(rp => rp.Permission.Name)
                                   .ToList();
                    }
                }
                catch (Exception ex)
                {
                    // ✅ لا تفشل الـ request بسبب خطأ في الصلاحيات
                    Console.WriteLine($"ClinicContext error: {ex.Message}");
                }
            }
        }

        // ✅ التحقق من صلاحية معينة
        public bool HasPermission(string permission)
        {
            if (IsSuperAdmin) return true;
            return Permissions.Contains(permission);
        }
    }
}