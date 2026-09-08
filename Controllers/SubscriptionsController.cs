using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Subscriptions;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SubscriptionsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public SubscriptionsController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        // GET: api/subscriptions
        [HttpGet]
        [Authorize(Roles = "SuperAdmin,ClinicStaff")]
        public async Task<ActionResult<IEnumerable<SubscriptionResponseDto>>> GetAll()
        {
            var subscriptions = await _db.Subscriptions
                .Include(s => s.Clinic)
                .Include(s => s.Plan)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var result = new List<SubscriptionResponseDto>();
            foreach (var s in subscriptions)
                result.Add(await ToResponse(s));

            return Ok(result);
        }

        // GET: api/subscriptions/clinic/{clinicId}
        [HttpGet("clinic/{clinicId}")]
        [Authorize(Roles = "SuperAdmin,ClinicStaff,ClinicAdmin")]
        public async Task<ActionResult<SubscriptionResponseDto>> GetByClinic(Guid clinicId)
        {
            if (_clinicContext.Role == "ClinicAdmin" && clinicId != _clinicContext.ClinicId)
                return Forbid();

            var subscription = await _db.Subscriptions
                .Include(s => s.Clinic)
                .Include(s => s.Plan)
                .Where(s => s.ClinicId == clinicId && s.IsActive)
                .OrderByDescending(s => s.EndDate)
                .FirstOrDefaultAsync();

            if (subscription == null)
                return NotFound("لا يوجد اشتراك لهذه العيادة");

            if (subscription.EndDate <= DateTime.UtcNow)
                return StatusCode(StatusCodes.Status402PaymentRequired, new
                {
                    code = "SUBSCRIPTION_EXPIRED",
                    message = "انتهى اشتراك العيادة. يرجى التجديد للمتابعة.",
                    expiredAt = subscription.EndDate
                });

            return Ok(await ToResponse(subscription));
        }

        // POST: api/subscriptions
        [HttpPost]
        [Authorize(Roles = "SuperAdmin,ClinicStaff")]
        public async Task<ActionResult<SubscriptionResponseDto>> Create([FromBody] CreateSubscriptionDto dto)
        {
            var clinic = await _db.Clinics.FindAsync(dto.ClinicId);
            if (clinic == null)
                return NotFound("العيادة غير موجودة");

            var plan = await _db.Plans.FindAsync(dto.PlanId);
            if (plan == null || !plan.IsActive)
                return NotFound("الخطة غير موجودة أو غير نشطة");

            var validCycles = new[] { "monthly", "yearly" };
            if (!validCycles.Contains(dto.BillingCycle))
                return BadRequest("BillingCycle يجب أن يكون monthly أو yearly");

            var endDate = dto.BillingCycle == "yearly"
                ? dto.StartDate.AddYears(1)
                : dto.StartDate.AddMonths(1);

            var pricePaid = dto.BillingCycle == "yearly"
                ? plan.YearlyPrice
                : plan.MonthlyPrice;

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var oldSubscription = await _db.Subscriptions
                    .FirstOrDefaultAsync(s => s.ClinicId == dto.ClinicId && s.IsActive);

                if (oldSubscription != null)
                {
                    oldSubscription.IsActive = false;
                    await _db.SaveChangesAsync();
                }

                var subscription = new Subscription
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    ClinicId = dto.ClinicId,
                    PlanId = dto.PlanId,
                    BillingCycle = dto.BillingCycle,
                    StartDate = dto.StartDate,
                    EndDate = endDate,
                    PricePaid = pricePaid
                };

                _db.Subscriptions.Add(subscription);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await _db.Entry(subscription).Reference(s => s.Clinic).LoadAsync();
                await _db.Entry(subscription).Reference(s => s.Plan).LoadAsync();

                return CreatedAtAction(
                    nameof(GetByClinic),
                    new { clinicId = subscription.ClinicId },
                    await ToResponse(subscription));
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                return BadRequest("يوجد اشتراك نشط قيد الإنشاء لهذه العيادة، حاول مرة أخرى");
            }
        }

        // PATCH: api/subscriptions/{id}/cancel
        [HttpPatch("{id}/cancel")]
        [Authorize(Roles = "SuperAdmin,ClinicStaff")]
        public async Task<ActionResult> Cancel(Guid id)
        {
            var subscription = await _db.Subscriptions.FindAsync(id);

            if (subscription == null)
                return NotFound();

            if (!subscription.IsActive)
                return BadRequest("الاشتراك غير نشط مسبقاً");

            subscription.IsActive = false;
            await _db.SaveChangesAsync();

            return Ok(new { message = "تم إلغاء الاشتراك بنجاح" });
        }

        // POST: api/subscriptions/{clinicId}/renew
        [HttpPost("{clinicId}/renew")]
        [Authorize(Roles = "SuperAdmin,ClinicStaff")]
        public async Task<ActionResult<SubscriptionResponseDto>> Renew(Guid clinicId, [FromBody] RenewSubscriptionDto dto)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null) return NotFound("العيادة غير موجودة");

            var plan = await _db.Plans.FindAsync(dto.PlanId);
            if (plan == null || !plan.IsActive) return NotFound("الخطة غير موجودة أو غير نشطة");

            var validCycles = new[] { "monthly", "yearly" };
            if (!validCycles.Contains(dto.BillingCycle))
                return BadRequest("BillingCycle يجب أن يكون monthly أو yearly");

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var current = await _db.Subscriptions
                    .Where(s => s.ClinicId == clinicId && s.IsActive)
                    .OrderByDescending(s => s.EndDate)
                    .FirstOrDefaultAsync();

                var startDate = (current != null && current.EndDate > DateTime.UtcNow)
                    ? current.EndDate
                    : DateTime.UtcNow;

                var endDate = dto.BillingCycle == "yearly"
                    ? startDate.AddYears(1)
                    : startDate.AddMonths(1);

                var pricePaid = dto.BillingCycle == "yearly"
                    ? plan.YearlyPrice
                    : plan.MonthlyPrice;

                if (current != null)
                {
                    current.IsActive = false;
                    await _db.SaveChangesAsync();
                }

                var subscription = new Subscription
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    ClinicId = clinicId,
                    PlanId = dto.PlanId,
                    BillingCycle = dto.BillingCycle,
                    StartDate = startDate,
                    EndDate = endDate,
                    PricePaid = pricePaid,
                };

                _db.Subscriptions.Add(subscription);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await _db.Entry(subscription).Reference(s => s.Clinic).LoadAsync();
                await _db.Entry(subscription).Reference(s => s.Plan).LoadAsync();

                return Ok(await ToResponse(subscription));
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // دالة مساعدة — تجلب إحصائيات الاستخدام الحالي
        private async Task<SubscriptionResponseDto> ToResponse(Subscription s)
        {
            var currentUsers = await _db.Users
                .CountAsync(u => u.ClinicId == s.ClinicId && u.IsActive);

            var currentDoctors = await _db.Doctors
                .CountAsync(d => d.ClinicId == s.ClinicId && !d.IsDeleted && d.IsActive);

            var currentPatients = await _db.Patients
                .CountAsync(p => p.ClinicId == s.ClinicId && !p.IsDeleted);

            return new SubscriptionResponseDto
            {
                Id = s.Id,
                ClinicId = s.ClinicId,
                ClinicName = s.Clinic.Name,
                PlanId = s.PlanId,
                PlanName = s.Plan.Name,
                MonthlyPrice = s.Plan.MonthlyPrice,
                YearlyPrice = s.Plan.YearlyPrice,
                MaxUsers = s.Plan.MaxUsers,
                MaxDoctors = s.Plan.MaxDoctors,
                MaxPatients = s.Plan.MaxPatients,
                BillingCycle = s.BillingCycle,
                PricePaid = s.PricePaid,
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt,
                CurrentUsers = currentUsers,
                CurrentDoctors = currentDoctors,
                CurrentPatients = currentPatients,
                HasElectronicInvoicing = s.Plan.HasElectronicInvoicing,
                HasMultipleDepartments = s.Plan.HasMultipleDepartments
            };
        }
    }

    public class RenewSubscriptionDto
    {
        public Guid PlanId { get; set; }
        public string BillingCycle { get; set; } = "monthly";
    }
}