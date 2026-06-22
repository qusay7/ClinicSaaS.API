using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Roles;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RolesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public RolesController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        // GET: api/roles
        // جلب كل الأدوار مع صلاحياتها
        [HttpGet]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<IEnumerable<RoleResponseDto>>> GetAll()
        {
            var roles = await _db.Roles
                .Include(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
                .Where(r => r.IsActive)
                .ToListAsync();

            var allPermissions = await _db.Permissions
                .OrderBy(p => p.Group)
                .ToListAsync();

            var result = roles.Select(r => new RoleResponseDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                IsActive = r.IsActive,
                Permissions = allPermissions.Select(p => new PermissionDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    DisplayName = p.DisplayName,
                    Group = p.Group,
                    IsGranted = r.RolePermissions.Any(rp => rp.PermissionId == p.Id)
                }).ToList()
            }).ToList();

            return Ok(result);
        }

        // GET: api/roles/{id}
        [HttpGet("{id}")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<RoleResponseDto>> GetById(Guid id)
        {
            var role = await _db.Roles
                .Include(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (role == null)
                return NotFound();

            var allPermissions = await _db.Permissions
                .OrderBy(p => p.Group)
                .ToListAsync();

            return Ok(new RoleResponseDto
            {
                Id = role.Id,
                Name = role.Name,
                Description = role.Description,
                IsActive = role.IsActive,
                Permissions = allPermissions.Select(p => new PermissionDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    DisplayName = p.DisplayName,
                    Group = p.Group,
                    IsGranted = role.RolePermissions.Any(rp => rp.PermissionId == p.Id)
                }).ToList()
            });
        }

        // PUT: api/roles/{id}/permissions
        // تحديث صلاحيات دور معين
        [HttpPut("{id}/permissions")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult> UpdatePermissions(
            Guid id,
            [FromBody] UpdateRolePermissionsDto dto)
        {
            var role = await _db.Roles
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (role == null)
                return NotFound();

            // لا يمكن تعديل صلاحيات SuperAdmin
            if (role.Name == "SuperAdmin")
                return BadRequest("لا يمكن تعديل صلاحيات SuperAdmin");

            // حذف الصلاحيات القديمة
            _db.RolePermissions.RemoveRange(role.RolePermissions);

            // إضافة الصلاحيات الجديدة
            foreach (var permissionId in dto.PermissionIds)
            {
                var permissionExists = await _db.Permissions
                    .AnyAsync(p => p.Id == permissionId);

                if (permissionExists)
                {
                    _db.RolePermissions.Add(new RolePermission
                    {
                        Id = Guid.NewGuid(),
                        RoleId = id,
                        PermissionId = permissionId
                    });
                }
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = "تم تحديث الصلاحيات بنجاح" });
        }

        // GET: api/roles/my-permissions
        // جلب صلاحيات المستخدم الحالي
        [HttpGet("my-permissions")]
        public async Task<ActionResult> GetMyPermissions()
        {
            if (_clinicContext.IsSuperAdmin)
            {
                var allPermissions = await _db.Permissions.ToListAsync();
                return Ok(new
                {
                    role = "SuperAdmin",
                    permissions = allPermissions.Select(p => p.Name).ToList()
                });
            }

            return Ok(new
            {
                role = _clinicContext.Role,
                permissions = _clinicContext.Permissions
            });
        }



        // GET: api/roles/clinic-permissions
        [HttpGet("clinic-permissions")]
        [Authorize(Roles = "ClinicAdmin,SuperAdmin")]
        public async Task<ActionResult> GetClinicPermissions()
        {
            var clinicId = _clinicContext.ClinicId;

            // جلب الأدوار القابلة للتخصيص
            var roles = await _db.Roles
                .Where(r => r.Name == "Doctor" || r.Name == "Receptionist" || r.Name == "ClinicAdmin")
                .ToListAsync();

            var result = new List<object>();

            foreach (var role in roles)
            {
                // تحقق إذا كانت للعيادة صلاحيات خاصة
                List<string> perms;

                if (clinicId.HasValue)
                {
                    var clinicPerms = await _db.RolePermissions
                        .Include(rp => rp.Permission)
                        .Where(rp => rp.RoleId == role.Id && rp.ClinicId == clinicId)
                        .Select(rp => rp.Permission.Name)
                        .ToListAsync();

                    if (clinicPerms.Any())
                    {
                        perms = clinicPerms;
                    }
                    else
                    {
                        // استخدم الافتراضية
                        perms = await _db.RolePermissions
                            .Include(rp => rp.Permission)
                            .Where(rp => rp.RoleId == role.Id && rp.ClinicId == null)
                            .Select(rp => rp.Permission.Name)
                            .ToListAsync();
                    }
                }
                else
                {
                    perms = await _db.RolePermissions
                        .Include(rp => rp.Permission)
                        .Where(rp => rp.RoleId == role.Id && rp.ClinicId == null)
                        .Select(rp => rp.Permission.Name)
                        .ToListAsync();
                }

                result.Add(new
                {
                    roleId = role.Id,
                    roleName = role.Name,
                    permissions = perms,
                });
            }

            return Ok(result);
        }

        // GET: api/roles/all-permissions
        [HttpGet("all-permissions")]
        [Authorize(Roles = "ClinicAdmin,SuperAdmin")]
        public async Task<ActionResult> GetAllPermissions()
        {
            var perms = await _db.Permissions
                .Where(p => p.IsActive)
                .OrderBy(p => p.Module)
                .ThenBy(p => p.Name)
                .ToListAsync();

            return Ok(perms.Select(p => new {
                p.Id,
                p.Name,
                p.Module,
                p.DisplayName
            }));
        }

        // PUT: api/roles/clinic-permissions/{roleId}
        [HttpPut("clinic-permissions/{roleId}")]
        [Authorize(Roles = "ClinicAdmin")]
        public async Task<ActionResult> UpdateClinicPermissions(
            Guid roleId, [FromBody] List<string> permissionNames)
        {
            var clinicId = _clinicContext.ClinicId;
            if (!clinicId.HasValue) return Unauthorized();

            // تحقق أن الدور موجود
            var role = await _db.Roles.FindAsync(roleId);
            if (role == null) return NotFound();

            // لا يمكن تعديل SuperAdmin
            if (role.Name == "SuperAdmin") return BadRequest("لا يمكن تعديل صلاحيات SuperAdmin");

            // احذف الصلاحيات الخاصة بهذه العيادة لهذا الدور
            var oldPerms = await _db.RolePermissions
                .Where(rp => rp.RoleId == roleId && rp.ClinicId == clinicId)
                .ToListAsync();
            _db.RolePermissions.RemoveRange(oldPerms);

            // أضف الجديدة
            foreach (var name in permissionNames)
            {
                var perm = await _db.Permissions
                    .FirstOrDefaultAsync(p => p.Name == name && p.IsActive);
                if (perm != null)
                {
                    _db.RolePermissions.Add(new RolePermission
                    {
                        Id = Guid.NewGuid(),
                        RoleId = roleId,
                        PermissionId = perm.Id,
                        ClinicId = clinicId,  // ✅ مخصص لهذه العيادة
                    });
                }
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = "تم تحديث الصلاحيات بنجاح" });
        }

        // GET: api/roles/default-permissions/{roleId}
        [HttpGet("default-permissions/{roleId}")]
        [Authorize(Roles = "ClinicAdmin,SuperAdmin")]
        public async Task<ActionResult> GetDefaultPermissions(Guid roleId)
        {
            var perms = await _db.RolePermissions
                .Include(rp => rp.Permission)
                .Where(rp => rp.RoleId == roleId && rp.ClinicId == null)
                .Select(rp => rp.Permission.Name)
                .ToListAsync();

            return Ok(perms);
        }
        // POST: api/roles/seed-defaults/{clinicId}
        [HttpPost("seed-defaults/{clinicId}")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult> SeedDefaultRoles(Guid clinicId)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null) return NotFound("العيادة غير موجودة");

            var allPermissions = await _db.Permissions.ToListAsync();

            var defaultRoles = new[]
   {
    new { Name = "ClinicAdmin",  NameEn = "Clinic Admin",  Description = "مدير العيادة" },
    new { Name = "Doctor",       NameEn = "Doctor",        Description = "طبيب" },
    new { Name = "Receptionist", NameEn = "Receptionist",  Description = "موظف استقبال" },
    new { Name = "ClinicStaff",  NameEn = "Clinic Staff",  Description = "موظف العيادة" },
};

            var permMap = new Dictionary<string, string[]>
            {
                ["ClinicAdmin"] = new[] { "patients.view", "patients.create", "patients.edit", "patients.delete", "doctors.view", "doctors.create", "doctors.edit", "doctors.delete", "appointments.view", "appointments.create", "appointments.edit", "appointments.delete", "schedules.view", "schedules.manage", "users.view", "users.create", "departments.manage", "settings.view", "settings.edit", "reports.view" },
                ["Doctor"] = new[] { "patients.view", "appointments.view", "appointments.create", "appointments.edit", "schedules.view" },
                ["Receptionist"] = new[] { "patients.view", "patients.create", "patients.edit", "appointments.view", "appointments.create", "appointments.edit", "schedules.view" },
                ["ClinicStaff"] = new[] { "patients.view", "appointments.view", "schedules.view", "reports.view" },
            };

            int added = 0;
            foreach (var roleData in defaultRoles)
            {
                var exists = await _db.Roles.AnyAsync(r => r.Name == roleData.Name && r.ClinicId == clinicId);
                if (exists) continue;

                var role = new Role
                {
                    Id = Guid.NewGuid(),
                    Name = roleData.Name,
                    NameEn = roleData.NameEn, // ✅
                    Description = roleData.Description,
                    ClinicId = clinicId,
                    IsActive = true,
                    IsSystem = true,
                };
                _db.Roles.Add(role);
                await _db.SaveChangesAsync();

                if (permMap.ContainsKey(roleData.Name))
                {
                    foreach (var permName in permMap[roleData.Name])
                    {
                        var perm = allPermissions.FirstOrDefault(p => p.Name == permName);
                        if (perm != null)
                        {
                            _db.RolePermissions.Add(new RolePermission
                            {
                                Id = Guid.NewGuid(),
                                RoleId = role.Id,
                                PermissionId = perm.Id,
                                ClinicId = null,
                            });
                        }
                    }
                    await _db.SaveChangesAsync();
                    added++;
                }

                // ✅ ربط المستخدمين الموجودين بهذا الدور تلقائياً
                var usersWithRole = await _db.Users
                    .Where(u => u.ClinicId == clinicId
                        && u.Role == roleData.Name
                        && u.RoleId == null)
                    .ToListAsync();

                foreach (var user in usersWithRole)
                {
                    user.RoleId = role.Id;
                }
                await _db.SaveChangesAsync();
            }

            return Ok(new { message = $"تم إنشاء {added} أدوار وربط المستخدمين تلقائياً" });
        }
    }
}