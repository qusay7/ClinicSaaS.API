using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Plans;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PlansController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public PlansController(ApplicationDbContext db)
        {
            _db = db;
        }

        // GET: api/plans
        // الكل يرى الخطط المتاحة
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PlanResponseDto>>> GetAll()
        {
            var plans = await _db.Plans
                .Where(p => p.IsActive)
                .OrderBy(p => p.MonthlyPrice)
                .ToListAsync();

            return Ok(plans.Select(p => ToResponse(p)).ToList());
        }

        // GET: api/plans/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<PlanResponseDto>> GetById(Guid id)
        {
            var plan = await _db.Plans.FindAsync(id);

            if (plan == null)
                return NotFound();

            return Ok(ToResponse(plan));
        }

        // POST: api/plans
        // SuperAdmin فقط — إنشاء خطة جديدة
        [HttpPost]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<PlanResponseDto>> Create([FromBody] CreatePlanDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("اسم الخطة مطلوب");

            // تحقق أن الاسم غير مكرر
            var nameExists = await _db.Plans
                .AnyAsync(p => p.Name == dto.Name);

            if (nameExists)
                return BadRequest("اسم الخطة مستخدم مسبقاً");

            var plan = new Plan
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                Name = dto.Name,
                Description = dto.Description,
                MonthlyPrice = dto.MonthlyPrice,
                YearlyPrice = dto.YearlyPrice,
                MaxUsers = dto.MaxUsers,
                MaxDoctors = dto.MaxDoctors,
                MaxPatients = dto.MaxPatients
            };

            _db.Plans.Add(plan);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = plan.Id }, ToResponse(plan));
        }

        // PUT: api/plans/{id}
        // SuperAdmin فقط — تعديل خطة
        [HttpPut("{id}")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<PlanResponseDto>> Update(Guid id, [FromBody] CreatePlanDto dto)
        {
            var plan = await _db.Plans.FindAsync(id);

            if (plan == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("اسم الخطة مطلوب");

            // تحقق أن الاسم غير مكرر (ما عدا نفس الخطة)
            var nameExists = await _db.Plans
                .AnyAsync(p => p.Name == dto.Name && p.Id != id);

            if (nameExists)
                return BadRequest("اسم الخطة مستخدم مسبقاً");

            plan.Name = dto.Name;
            plan.Description = dto.Description;
            plan.MonthlyPrice = dto.MonthlyPrice;
            plan.YearlyPrice = dto.YearlyPrice;
            plan.MaxUsers = dto.MaxUsers;
            plan.MaxDoctors = dto.MaxDoctors;
            plan.MaxPatients = dto.MaxPatients;

            await _db.SaveChangesAsync();
            return Ok(ToResponse(plan));
        }

        // PATCH: api/plans/{id}/toggle
        // SuperAdmin فقط — تفعيل/تعطيل خطة
        [HttpPatch("{id}/toggle")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult> Toggle(Guid id)
        {
            var plan = await _db.Plans.FindAsync(id);

            if (plan == null)
                return NotFound();

            plan.IsActive = !plan.IsActive;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = plan.IsActive ? "تم تفعيل الخطة" : "تم تعطيل الخطة",
                isActive = plan.IsActive
            });
        }

        private static PlanResponseDto ToResponse(Plan p) => new PlanResponseDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            MonthlyPrice = p.MonthlyPrice,
            YearlyPrice = p.YearlyPrice,
            MaxUsers = p.MaxUsers,
            MaxDoctors = p.MaxDoctors,
            MaxPatients = p.MaxPatients,
            IsActive = p.IsActive,
            CreatedAt = p.CreatedAt
        };
    }
}