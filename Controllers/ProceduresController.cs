using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClinicSaaS.API.Filters;

namespace ClinicSaaS.API.Controllers
{
    // ✅ كتالوج الإجراءات (حقنة مضاد حيوي، بنج، دواء...) — يحدده صاحب العيادة/
    // المسؤول بسعر افتراضي، ويضيفه الطبيب لأي زيارة (موعد أو طوارئ) لاحقاً
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [RequireActiveSubscription]
    public class ProceduresController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public ProceduresController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        private static string Msg(string lang, string ar, string en) => lang == "ar" ? ar : en;

        // GET: api/procedures
        [HttpGet]
        public async Task<ActionResult> GetAll()
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var items = await _db.Procedures
                .Where(p => p.ClinicId == _clinicContext.ClinicId && p.IsActive)
                .OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name, p.NameEn, p.DefaultPrice })
                .ToListAsync();

            return Ok(items);
        }

        // POST: api/procedures
        [HttpPost]
        public async Task<ActionResult> Create([FromBody] CreateProcedureDto dto, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("procedures.manage")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(Msg(lang, "اسم الإجراء مطلوب", "Procedure name is required"));

            var procedure = new Procedure
            {
                Id = Guid.NewGuid(),
                ClinicId = _clinicContext.ClinicId.Value,
                Name = dto.Name,
                NameEn = dto.NameEn,
                DefaultPrice = dto.DefaultPrice,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };

            _db.Procedures.Add(procedure);
            await _db.SaveChangesAsync();

            return Ok(new { procedure.Id, message = Msg(lang, "تم إنشاء الإجراء بنجاح", "Procedure created successfully") });
        }

        // PUT: api/procedures/{id}
        [HttpPut("{id}")]
        public async Task<ActionResult> Update(Guid id, [FromBody] CreateProcedureDto dto, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("procedures.manage")) return Forbid();

            var procedure = await _db.Procedures.FindAsync(id);
            if (procedure == null) return NotFound();
            if (procedure.ClinicId != _clinicContext.ClinicId) return Forbid();

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(Msg(lang, "اسم الإجراء مطلوب", "Procedure name is required"));

            procedure.Name = dto.Name;
            procedure.NameEn = dto.NameEn;
            procedure.DefaultPrice = dto.DefaultPrice;

            await _db.SaveChangesAsync();

            return Ok(new { procedure.Id, message = Msg(lang, "تم تحديث الإجراء بنجاح", "Procedure updated successfully") });
        }

        // DELETE: api/procedures/{id}
        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(Guid id, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("procedures.manage")) return Forbid();

            var procedure = await _db.Procedures.FindAsync(id);
            if (procedure == null) return NotFound();
            if (procedure.ClinicId != _clinicContext.ClinicId) return Forbid();

            procedure.IsDeleted = true;
            procedure.IsActive = false;
            await _db.SaveChangesAsync();

            return Ok(new { message = Msg(lang, "تم حذف الإجراء", "Procedure deleted") });
        }
    }

    public class CreateProcedureDto
    {
        public string Name { get; set; } = "";
        public string? NameEn { get; set; }
        public decimal? DefaultPrice { get; set; }
    }
}
