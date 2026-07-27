using ClinicSaaS.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Services
{
    public interface IRoleSeedingService
    {
        Task<int> SeedDefaultRoles(Guid clinicId);
    }

    public class RoleSeedingService : IRoleSeedingService
    {
        private readonly ApplicationDbContext _db;

        public RoleSeedingService(ApplicationDbContext db)
        {
            _db = db;
        }

        private static readonly (string Name, string NameEn, string Description)[] DefaultRoles = new[]
        {
            ("ClinicAdmin",  "Clinic Admin",  "مدير العيادة"),
            ("Doctor",       "Doctor",        "طبيب"),
            ("Receptionist", "Receptionist",  "موظف استقبال"),
            ("ClinicStaff",  "Clinic Staff",  "موظف العيادة"),
            ("Nurse",        "Nurse",         "ممرض"),
        };

        // ✅ Doctor/Receptionist/ClinicStaff/Nurse يحصلون على قائمة محددة يدوياً —
        // بينما ClinicAdmin (أدناه بمنطق منفصل) ياخذ كل صلاحية موجودة تلقائياً، بدون قائمة ثابتة
        private static readonly Dictionary<string, string[]> PermMap = new()
        {
            ["Doctor"] = new[] { "patients.view", "appointments.view", "appointments.create", "appointments.edit", "schedules.view", "visitnotes.view", "visitnotes.create", "visitnotes.edit" },
            ["Receptionist"] = new[] { "patients.view", "patients.create", "patients.edit", "appointments.view", "appointments.create", "appointments.edit", "schedules.view" },
            ["Nurse"] = new[] { "patients.view", "patients.create", "patients.edit", "appointments.view", "appointments.create", "appointments.edit", "schedules.view", "visitnotes.view" },
            ["ClinicStaff"] = new[] { "patients.view", "appointments.view", "schedules.view", "reports.view" },
        };

        public async Task<int> SeedDefaultRoles(Guid clinicId)
        {
            var allPermissions = await _db.Permissions.Where(p => p.IsActive).ToListAsync();
            int added = 0;

            foreach (var roleData in DefaultRoles)
            {
                // ✅ نجيب الدور لو موجود، وننشئه بس لو مو موجود — بدل ما نتجاهله بالكامل
                var role = await _db.Roles
                    .FirstOrDefaultAsync(r => r.Name == roleData.Name && r.ClinicId == clinicId);

                var isNewRole = role == null;
                if (isNewRole)
                {
                    role = new Role
                    {
                        Id = Guid.NewGuid(),
                        Name = roleData.Name,
                        NameEn = roleData.NameEn,
                        Description = roleData.Description,
                        ClinicId = clinicId,
                        IsActive = true,
                        IsSystem = true,
                    };
                    _db.Roles.Add(role);
                    await _db.SaveChangesAsync();
                    added++;
                }

                // ✅ ClinicAdmin ياخذ كل صلاحية موجودة حالياً تلقائياً — أي صلاحية جديدة تُضاف
                // مستقبلاً بالنظام تنعكس عليه تلقائياً بمجرد إعادة استدعاء هذي الدالة، بدون
                // ما نحتاج نعدّل قائمة يدوية بالكود في كل مرة
                var permNames = roleData.Name == "ClinicAdmin"
                    ? allPermissions.Select(p => p.Name)
                    : (PermMap.TryGetValue(roleData.Name, out var mapped) ? mapped : Array.Empty<string>());

                // ✅ نضيف بس الصلاحيات الناقصة — يخلي "إعادة التزامن" تشتغل صح حتى
                // لدور موجود من قبل (بدل ما تُتجاهل بالكامل زي السلوك القديم)
                var existingPermIds = await _db.RolePermissions
                    .Where(rp => rp.RoleId == role!.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync();

                foreach (var permName in permNames)
                {
                    var perm = allPermissions.FirstOrDefault(p => p.Name == permName);
                    if (perm != null && !existingPermIds.Contains(perm.Id))
                    {
                        _db.RolePermissions.Add(new RolePermission
                        {
                            Id = Guid.NewGuid(),
                            RoleId = role!.Id,
                            PermissionId = perm.Id,
                            ClinicId = null,
                        });
                    }
                }
                await _db.SaveChangesAsync();

                // ربط المستخدمين الموجودين اللي بدون RoleId
                var usersWithRole = await _db.Users
                    .Where(u => u.ClinicId == clinicId
                        && u.Role == roleData.Name
                        && u.RoleId == null)
                    .ToListAsync();

                foreach (var user in usersWithRole)
                    user.RoleId = role!.Id;

                await _db.SaveChangesAsync();
            }

            return added;
        }
    }
}