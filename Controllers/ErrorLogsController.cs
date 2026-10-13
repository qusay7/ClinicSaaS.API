using ClinicSaaS.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    // ✅ سجل أخطاء الباك إند — بديل خفيف عن Sentry، SuperAdmin فقط
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "SuperAdmin")]
    public class ErrorLogsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public ErrorLogsController(ApplicationDbContext db)
        {
            _db = db;
        }

        // GET: api/errorlogs?page=1&pageSize=20&clinicId=
        [HttpGet]
        public async Task<ActionResult> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] Guid? clinicId = null)
        {
            var query = _db.ErrorLogs.AsQueryable();
            if (clinicId.HasValue) query = query.Where(e => e.ClinicId == clinicId);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(e => e.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new
                {
                    e.Id,
                    e.Message,
                    e.RequestPath,
                    e.RequestMethod,
                    e.UserId,
                    e.ClinicId,
                    e.CreatedAt,
                })
                .ToListAsync();

            // ✅ نحلّ UserId/ClinicId لأسماء دفعة وحدة — زي AuditController بالضبط
            var userIds = items.Where(e => e.UserId.HasValue).Select(e => e.UserId!.Value).Distinct().ToList();
            var userNames = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);

            var clinicIds = items.Where(e => e.ClinicId.HasValue).Select(e => e.ClinicId!.Value).Distinct().ToList();
            var clinicNames = await _db.Clinics.Where(c => clinicIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);

            var result = items.Select(e => new
            {
                e.Id,
                e.Message,
                e.RequestPath,
                e.RequestMethod,
                e.CreatedAt,
                userName = e.UserId.HasValue && userNames.TryGetValue(e.UserId.Value, out var un) ? un : null,
                clinicName = e.ClinicId.HasValue && clinicNames.TryGetValue(e.ClinicId.Value, out var cn) ? cn : null,
            });

            return Ok(new
            {
                total,
                page,
                pageSize,
                pages = (int)Math.Ceiling((double)total / Math.Max(pageSize, 1)),
                items = result,
            });
        }

        // GET: api/errorlogs/{id} — تفاصيل كاملة (stack trace) لخطأ واحد
        [HttpGet("{id}")]
        public async Task<ActionResult> GetOne(Guid id)
        {
            var log = await _db.ErrorLogs.FirstOrDefaultAsync(e => e.Id == id);
            if (log == null) return NotFound();
            return Ok(log);
        }
    }
}
