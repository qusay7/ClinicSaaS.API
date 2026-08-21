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
        private readonly IRoleSeedingService _roleSeedingService;   // ✅ جديد

        public RolesController(ApplicationDbContext db, IClinicContext clinicContext, IRoleSeedingService roleSeedingService)
        {
            _db = db;
            _clinicContext = clinicContext;
            _roleSeedingService = roleSeedingService;
        }

        // GET: api/roles
        // جلب كل الأدوار مع صلاحياتها
        // ✅ ?clinicId= اختياري — يُستخدم بس لو SuperAdmin يبي أدوار عيادة معينة
        // (لو مو SuperAdmin، نتجاهل الباراميتر ونستخدم عيادته هو دايماً — حماية أمنية)
        [HttpGet]
        [Authorize]
        public async Task<ActionResult<IEnumerable<RoleResponseDto>>> GetAll([FromQuery] Guid? clinicId = null)
        {
            var query = _db.Roles
                .Include(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
                .Where(r => r.IsActive);

            if (_clinicContext.IsSuperAdmin)
            {
                // ✅ SuperAdmin يقدر يحدد عيادة معينة، وإلا يرى كل الأدوار
                if (clinicId.HasValue)
                    query = query.Where(r => r.ClinicId == clinicId);
            }
            else
            {
                if (_clinicContext.ClinicId == null) return Unauthorized();
                query = query.Where(r => r.ClinicId == _clinicContext.ClinicId);
            }

            var roles = await query.ToListAsync();

            var allPermissions = await _db.Permissions
                .OrderBy(p => p.Group)
                .ToListAsync();

            var result = roles.Select(r => new RoleResponseDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                IsActive = r.IsActive,
                ClinicId = r.ClinicId,   // ✅ جديد — يفيد الفرونت إند لو يعرض أدوار متعددة عيادات

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

            // جلب الأدوار القابلة للتخصيص حسب المستخدم
            var rolesQuery = _db.Roles.AsQueryable();

            if (_clinicContext.Role == "SuperAdmin")
            {
                // المستخدم ليس مرتبطاً بدور
                // اعرض أدوار Admin
                rolesQuery = rolesQuery
                    .Where(r => r.Scope == RoleScope.Admin);
            }
            else
            {
                // المستخدم لديه RoleId
                // اعرض أدوار العيادة فقط
                rolesQuery = rolesQuery
                    .Where(r =>
                        r.Scope == RoleScope.Clinic &&
                        r.ClinicId == clinicId);
            }

            var roles = await rolesQuery
                .Where(r => r.IsActive)
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

            var role = await _db.Roles.FindAsync(roleId);
            if (role == null) return NotFound();

            // ✅ تحقق أن الدور يتبع لنفس عيادة المستخدم
            if (role.ClinicId != clinicId)
                return Forbid();

            if (role.Name == "SuperAdmin") return BadRequest("لا يمكن تعديل صلاحيات SuperAdmin");

            // احذف الصلاحيات الخاصة بهذه العيادة لهذا الدور
            var oldPerms = await _db.RolePermissions
                .Where(rp => rp.RoleId == roleId && rp.ClinicId == clinicId)
                .ToListAsync();
            _db.RolePermissions.RemoveRange(oldPerms);

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
                        ClinicId = clinicId,
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

            var added = await _roleSeedingService.SeedDefaultRoles(clinicId);

            return Ok(new { message = $"تم إنشاء {added} أدوار وربط المستخدمين تلقائياً" });
        }
    }
}