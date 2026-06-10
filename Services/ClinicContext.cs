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
            if (UserId.HasValue && !IsSuperAdmin)
            {
                var dbUser = db.Users
                    .Include(u => u.UserRole)
                    .FirstOrDefault(u => u.Id == UserId.Value);

                if (dbUser?.RoleId != null && ClinicId.HasValue)
                {
                    // ✅ أولاً — صلاحيات خاصة بالعيادة
                    var clinicPerms = db.RolePermissions
                        .Include(rp => rp.Permission)
                        .Where(rp => rp.RoleId == dbUser.RoleId
                            && rp.ClinicId == ClinicId)
                        .Select(rp => rp.Permission.Name)
                        .ToList();

                    if (clinicPerms.Any())
                    {
                        Permissions = clinicPerms;
                    }
                    else
                    {
                        // ✅ ثانياً — الصلاحيات الافتراضية
                        var defaultPerms = db.RolePermissions
                            .Include(rp => rp.Permission)
                            .Where(rp => rp.RoleId == dbUser.RoleId
                                && rp.ClinicId == null)
                            .Select(rp => rp.Permission.Name)
                            .ToList();

                        Permissions = defaultPerms;
                    }
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