using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Audit;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    // ✅ سجل التدقيق — مين أنشأ/عدّل أي سجل ومتى (CreatedBy/UpdatedBy/UpdatedAt عبر
    // IAuditable، تتعبّى تلقائياً بـ ApplicationDbContext.SaveChangesAsync). ClinicAdmin
    // يشوف عيادته فقط، SuperAdmin يشوف الكل (أو عيادة محددة عبر ?clinicId=)
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "ClinicAdmin,SuperAdmin")]
    public class AuditController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public AuditController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        private static readonly (string Key, string LabelAr, string LabelEn)[] EntityTypes = new[]
        {
            ("patients", "المرضى", "Patients"),
            ("appointments", "المواعيد", "Appointments"),
            ("staff", "فريق العمل", "Staff"),
            ("users", "المستخدمون", "Users"),
            ("doctors", "الأطباء", "Doctors"),
            ("invoices", "الفواتير", "Invoices"),
            ("departments", "الأقسام", "Departments"),
        };

        // GET: api/audit/entities
        [HttpGet("entities")]
        public ActionResult GetEntityTypes()
        {
            return Ok(EntityTypes.Select(e => new { key = e.Key, labelAr = e.LabelAr, labelEn = e.LabelEn }));
        }

        // GET: api/audit?entityType=patients&page=1&pageSize=20
        [HttpGet]
        public async Task<ActionResult> GetAudit(
            [FromQuery] string entityType,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] Guid? clinicId = null)
        {
            if (!EntityTypes.Any(e => e.Key == entityType))
                return BadRequest("نوع غير معروف");

            // ✅ ClinicAdmin مقيّد بعيادته دايماً، SuperAdmin يقدر يشوف الكل أو يحدد عيادة
            Guid? scopeClinicId = _clinicContext.IsSuperAdmin ? clinicId : _clinicContext.ClinicId;
            if (!_clinicContext.IsSuperAdmin && scopeClinicId == null) return Unauthorized();

            IQueryable<AuditRowDto> query;
            switch (entityType)
            {
                case "patients":
                    {
                        var q = _db.Patients.Where(p => !p.IsDeleted).AsQueryable();
                        if (scopeClinicId.HasValue) q = q.Where(p => p.ClinicId == scopeClinicId);
                        query = q.Select(p => new AuditRowDto
                        {
                            Id = p.Id,
                            Name = p.FullName,
                            CreatedAt = p.CreatedAt,
                            CreatedBy = p.CreatedBy,
                            UpdatedAt = p.UpdatedAt,
                            UpdatedBy = p.UpdatedBy,
                        });
                        break;
                    }
                case "appointments":
                    {
                        var q = _db.Appointments.AsQueryable();
                        if (scopeClinicId.HasValue) q = q.Where(a => a.ClinicId == scopeClinicId);
                        query = q.Select(a => new AuditRowDto
                        {
                            Id = a.Id,
                            Name = (a.Patient != null ? a.Patient.FullName : "—") + " — " + a.AppointmentDate.ToString("yyyy-MM-dd HH:mm"),
                            CreatedAt = a.CreatedAt,
                            CreatedBy = a.CreatedBy,
                            UpdatedAt = a.UpdatedAt,
                            UpdatedBy = a.UpdatedBy,
                        });
                        break;
                    }
                case "staff":
                    {
                        var q = _db.Staff.Where(s => !s.IsDeleted).AsQueryable();
                        if (scopeClinicId.HasValue) q = q.Where(s => s.ClinicId == scopeClinicId);
                        query = q.Select(s => new AuditRowDto
                        {
                            Id = s.Id,
                            Name = s.FullName,
                            CreatedAt = s.CreatedAt,
                            CreatedBy = s.CreatedBy,
                            UpdatedAt = s.UpdatedAt,
                            UpdatedBy = s.UpdatedBy,
                        });
                        break;
                    }
                case "users":
                    {
                        var q = _db.Users.Where(u => !u.IsDeleted).AsQueryable();
                        if (scopeClinicId.HasValue) q = q.Where(u => u.ClinicId == scopeClinicId);
                        query = q.Select(u => new AuditRowDto
                        {
                            Id = u.Id,
                            Name = u.FullName,
                            CreatedAt = u.CreatedAt,
                            CreatedBy = u.CreatedBy,
                            UpdatedAt = u.UpdatedAt,
                            UpdatedBy = u.UpdatedBy,
                        });
                        break;
                    }
                case "doctors":
                    {
                        var q = _db.Doctors.Where(d => !d.IsDeleted).AsQueryable();
                        if (scopeClinicId.HasValue) q = q.Where(d => d.ClinicId == scopeClinicId);
                        query = q.Select(d => new AuditRowDto
                        {
                            Id = d.Id,
                            Name = d.FullName,
                            CreatedAt = d.CreatedAt,
                            CreatedBy = d.CreatedBy,
                            UpdatedAt = d.UpdatedAt,
                            UpdatedBy = d.UpdatedBy,
                        });
                        break;
                    }
                case "invoices":
                    {
                        var q = _db.Invoices.Where(i => !i.IsDeleted).AsQueryable();
                        if (scopeClinicId.HasValue) q = q.Where(i => i.ClinicId == scopeClinicId);
                        query = q.Select(i => new AuditRowDto
                        {
                            Id = i.Id,
                            Name = i.InvoiceNumber + (i.Patient != null ? " — " + i.Patient.FullName : ""),
                            CreatedAt = i.CreatedAt,
                            CreatedBy = i.CreatedBy,
                            UpdatedAt = i.UpdatedAt,
                            UpdatedBy = i.UpdatedBy,
                        });
                        break;
                    }
                case "departments":
                    {
                        var q = _db.Departments.AsQueryable();
                        if (scopeClinicId.HasValue) q = q.Where(d => d.ClinicId == scopeClinicId);
                        query = q.Select(d => new AuditRowDto
                        {
                            Id = d.Id,
                            Name = d.Name,
                            CreatedAt = d.CreatedAt,
                            CreatedBy = d.CreatedBy,
                            UpdatedAt = d.UpdatedAt,
                            UpdatedBy = d.UpdatedBy,
                        });
                        break;
                    }
                default:
                    return BadRequest("نوع غير معروف");
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // ✅ CreatedBy/UpdatedBy ما فيها FK حقيقي لجدول Users (مجرد Guid? خام) —
            // نحلّها يدوياً لأسماء دفعة وحدة بدل استعلام منفصل لكل سجل
            var userIds = items
                .SelectMany(r => new[] { r.CreatedBy, r.UpdatedBy })
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();
            var names = await _db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName);

            foreach (var r in items)
            {
                r.CreatedByName = r.CreatedBy.HasValue && names.TryGetValue(r.CreatedBy.Value, out var cn) ? cn : null;
                r.UpdatedByName = r.UpdatedBy.HasValue && names.TryGetValue(r.UpdatedBy.Value, out var un) ? un : null;
            }

            return Ok(new
            {
                total,
                page,
                pageSize,
                pages = (int)Math.Ceiling((double)total / Math.Max(pageSize, 1)),
                items,
            });
        }
    }
}
