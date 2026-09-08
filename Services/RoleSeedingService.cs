using ClinicSaaS.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicSaaS.API.Services
{
    public interface IRoleSeedingService
    {
        Task<int> SeedDefaultRoles(Guid clinicId);//انشاء أدوار العيادة الافتراضية
        Task<int> SeedAdminRoles();//انشاء أدوار الادارة الافتراضية
        Task<int> SeedSchedulePermissions();//انشاء صلاحيات الجدول الافتراضية
    }

    public class RoleSeedingService : IRoleSeedingService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<RoleSeedingService> _logger;

        public RoleSeedingService(ApplicationDbContext db, ILogger<RoleSeedingService> logger)
        {
            _db = db;
            _logger = logger;
        }

        private static readonly (string Name, string NameEn, string Description)[] ClinicRoles = new[]
        {
            ("ClinicAdmin",  "Clinic Admin",  "مدير العيادة"),
            ("Doctor",       "Doctor",        "طبيب"),
            ("Receptionist", "Receptionist",  "موظف استقبال"),
            ("ClinicStaff",  "Clinic Staff",  "موظف العيادة"),
            ("Nurse",        "Nurse",         "ممرض"),
            ("Administration employee", "Administration employee", "موظف إداري"),
        };

        private static readonly (string Name, string NameEn, string Description)[] AdminRoles = new[]
        {
            ("SuperAdmin", "Super Admin", "مدير النظام"),
            ("AdminManager", "Admin Manager", "مدير الإدارة"),
        };

        private static readonly Dictionary<string, string[]> PermMap = new()
        {
            ["Doctor"] = new[] {
        "patients.view", "appointments.view", "appointments.create", "appointments.edit",
        "schedules.doctor.view",
        "schedules.doctor.editown",
        "schedules.absence.view",
        "schedules.absence.add",
        "visitnotes.view", "visitnotes.create", "visitnotes.edit",
        "daily.view"
    },

            ["Receptionist"] = new[] {
        "patients.view", "patients.create", "patients.edit", "appointments.view",
        "appointments.create", "appointments.edit",
        "schedules.clinic.view",
        "schedules.doctor.view",
        "schedules.absence.view",
        "doctors.view",
        "queue.manage",
        // ✅ يحتاجها فعلياً لإنهاء الزيارة/تحصيل الدفعة عند الـ checkout —
        // كانت هذي العملية بدون أي قيد صلاحية أصلاً (ثغرة)، فتقييدها الآن
        // على ClinicAdmin فقط بيكسر تدفق الاستقبال الحالي
        "payments.manage"
    },

            ["Nurse"] = new[] {
        "patients.view", "patients.create", "patients.edit", "appointments.view",
        "appointments.create", "appointments.edit",
        "schedules.clinic.view",
        "schedules.doctor.view",
        "schedules.absence.view",
        "visitnotes.view"
    },

            ["ClinicStaff"] = new[] {
        "patients.view", "appointments.view",
        "schedules.clinic.view",
        "schedules.doctor.view",
        "schedules.absence.view",
        "reports.view"
    },

            ["ClinicAdmin"] = new[] {
        "schedules.clinic.view",
        "schedules.clinic.add",
        "schedules.clinic.edit",
        "schedules.clinic.delete",

        "schedules.doctor.view",
        "schedules.doctor.add",
        "schedules.doctor.edit",
        "schedules.doctor.delete",
        "schedules.doctor.editown",

        "schedules.absence.view",
        "schedules.absence.add",
        "schedules.absence.edit",
        "schedules.absence.delete"
    },

            ["AdminManager"] = new[] {
        "clinics.view", "clinics.edit", "users.view", "users.create", "users.edit",
        "users.delete", "permissions.view", "reports.view", "payments.view", "settlements.manage",

        "schedules.clinic.view",
        "schedules.clinic.add",
        "schedules.clinic.edit",
        "schedules.clinic.delete",

        "schedules.doctor.view",
        "schedules.doctor.add",
        "schedules.doctor.edit",
        "schedules.doctor.delete",
        "schedules.doctor.editown",

        "schedules.absence.view",
        "schedules.absence.add",
        "schedules.absence.edit",
        "schedules.absence.delete"
    }
            // ✅ حذفنا SuperAdmin من هنا - لا يحتاجه
        };


        public async Task<int> SeedDefaultRoles(Guid clinicId)
        {
            try
            {
                var allPermissions = await _db.Permissions
                    .Where(p => p.IsActive)
                    .ToListAsync();

                if (allPermissions.Count == 0)
                {
                    _logger.LogWarning("⚠️ No permissions found. Please seed permissions first.");
                    return 0;
                }

                int added = 0;

                foreach (var roleData in ClinicRoles)
                {
                    var role = await _db.Roles
                        .FirstOrDefaultAsync(r => r.Name == roleData.Name && r.ClinicId == clinicId);

                    if (role == null)
                    {
                        role = new Role
                        {
                            Id = Guid.NewGuid(),
                            Name = roleData.Name,
                            NameEn = roleData.NameEn,
                            Description = roleData.Description,
                            ClinicId = clinicId,
                            Scope = RoleScope.Clinic,
                            IsActive = true,
                            IsSystem = true,
                        };
                        _db.Roles.Add(role);
                        await _db.SaveChangesAsync();
                        _logger.LogInformation($"✅ Created role: {roleData.Name}");
                        added++;
                    }

                    await AddPermissionsToRole(role, roleData.Name, allPermissions);
                    await LinkUsersToRole(role, roleData.Name, clinicId);
                }

                return added;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error seeding clinic roles: {ex.Message}");
                throw;
            }
        }

        public async Task<int> SeedAdminRoles()
        {
            try
            {
                var allPermissions = await _db.Permissions
                    .Where(p => p.IsActive)
                    .ToListAsync();

                if (allPermissions.Count == 0)
                {
                    _logger.LogWarning("⚠️ No permissions found. Please seed permissions first.");
                    return 0;
                }

                int added = 0;

                foreach (var roleData in AdminRoles)
                {
                    var role = await _db.Roles
                        .FirstOrDefaultAsync(r => r.Name == roleData.Name && r.Scope == RoleScope.Admin);

                    if (role == null)
                    {
                        role = new Role
                        {
                            Id = Guid.NewGuid(),
                            Name = roleData.Name,
                            NameEn = roleData.NameEn,
                            Description = roleData.Description,
                            ClinicId = null,
                            Scope = RoleScope.Admin,
                            IsActive = true,
                            IsSystem = true,
                        };
                        _db.Roles.Add(role);
                        await _db.SaveChangesAsync();
                        _logger.LogInformation($"✅ Created admin role: {roleData.Name}");
                        added++;
                    }

                    await AddPermissionsToRole(role, roleData.Name, allPermissions);
                }

                return added;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error seeding admin roles: {ex.Message}");
                throw;
            }
        }
        // ✅ دالة جديدة - أضفها بعد SeedAdminRoles
        public async Task<int> SeedSchedulePermissions()
        {
            try
            {
                _logger.LogInformation("🔄 Checking schedule permissions...");

                var schedulePermissions = new[]
                {
            ("schedules.clinic.view", "عرض دوام العيادة"),
            ("schedules.clinic.add", "إضافة دوام عيادة"),
            ("schedules.clinic.edit", "تعديل دوام عيادة"),
            ("schedules.clinic.delete", "حذف دوام عيادة"),

            ("schedules.doctor.view", "عرض دوام الأطباء"),
            ("schedules.doctor.add", "إضافة دوام طبيب"),
            ("schedules.doctor.edit", "تعديل دوام طبيب"),
            ("schedules.doctor.delete", "حذف دوام طبيب"),
            ("schedules.doctor.editown", "تعديل جدولي الخاص"),

            ("schedules.absence.view", "عرض الإجازات"),
            ("schedules.absence.add", "إضافة إجازة"),
            ("schedules.absence.edit", "تعديل إجازة"),
            ("schedules.absence.delete", "حذف إجازة"),
        };

                int added = 0;

                foreach (var (permName, description) in schedulePermissions)
                {
                    var permission = await _db.Permissions
                        .FirstOrDefaultAsync(p => p.Name == permName);

                    if (permission == null)
                    {
                        _db.Permissions.Add(new Permission
                        {
                            Id = Guid.NewGuid(),
                            Name = permName,
                            DisplayName = description,
                            Module = "schedules",
                            IsActive = true
                        });
                        added++;
                        _logger.LogInformation($"✅ Added permission: {permName}");
                    }
                }

                if (added > 0)
                {
                    await _db.SaveChangesAsync();
                    _logger.LogInformation($"✅ Total schedule permissions added: {added}");
                }

                return added;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error seeding schedule permissions: {ex.Message}");
                throw;
            }
        }
        private async Task AddPermissionsToRole(Role role, string roleName, List<Permission> allPermissions)
        {
            var permNames = roleName == "ClinicAdmin" || roleName == "SuperAdmin"
                ? allPermissions.Select(p => p.Name)
                : (PermMap.TryGetValue(roleName, out var mapped) ? mapped : Array.Empty<string>());

            var existingPermIds = await _db.RolePermissions
                .Where(rp => rp.RoleId == role.Id)
                .Select(rp => rp.PermissionId)
                .ToListAsync();

            var permissionsToAdd = new List<RolePermission>();
            foreach (var permName in permNames)
            {
                var perm = allPermissions.FirstOrDefault(p => p.Name == permName);
                if (perm != null && !existingPermIds.Contains(perm.Id))
                {
                    permissionsToAdd.Add(new RolePermission
                    {
                        Id = Guid.NewGuid(),
                        RoleId = role.Id,
                        PermissionId = perm.Id,
                        ClinicId = null,
                        IsActive = true,
                    });
                }
            }

            if (permissionsToAdd.Count > 0)
            {
                _db.RolePermissions.AddRange(permissionsToAdd);
                await _db.SaveChangesAsync();
                _logger.LogInformation($"✅ Added {permissionsToAdd.Count} permissions to role: {role.Name}");
            }
        }

        private async Task LinkUsersToRole(Role role, string roleName, Guid clinicId)
        {
            var usersWithoutRole = await _db.Users
                .Where(u => u.ClinicId == clinicId
                    && u.Role == roleName
                    && u.RoleId == null)
                .ToListAsync();

            if (usersWithoutRole.Count > 0)
            {
                foreach (var user in usersWithoutRole)
                    user.RoleId = role.Id;

                await _db.SaveChangesAsync();
                _logger.LogInformation($"✅ Linked {usersWithoutRole.Count} users to role: {role.Name}");
            }
        }
    }
}