using ClinicSaaS.API.Data;
using ClinicSaaS.API.DTOs.Plans;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    /// <summary>
    /// إدارة خطط الاشتراك (Plans). القراءة عامة (تُستخدم بصفحة الأسعار العامة قبل تسجيل الدخول)،
    /// بينما الإنشاء والتعديل والتفعيل/التعطيل محصورة على SuperAdmin فقط.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PlansController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public PlansController(ApplicationDbContext db)
        {
            _db = db;
        }

        private static string Msg(string lang, string ar, string en) => lang == "ar" ? ar : en;

        // ═══════════════════════════════════════
        // GET: api/plans
        // ✅ عام بالكامل — صفحة الهبوط تحتاجها قبل تسجيل الدخول
        // ═══════════════════════════════════════
        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<IEnumerable<PlanResponseDto>>> GetAll()
        {
            var plans = await _db.Plans
                .Where(p => p.IsActive)
                .OrderBy(p => p.MonthlyPrice)
                .ToListAsync();

            return Ok(plans.Select(ToResponse).ToList());
        }

        // GET: api/plans/{id}
        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<ActionResult<PlanResponseDto>> GetById(Guid id)
        {
            var plan = await _db.Plans.FindAsync(id);
            if (plan == null) return NotFound();

            return Ok(ToResponse(plan));
        }

        // ═══════════════════════════════════════
        // POST: api/plans
        // ═══════════════════════════════════════
        [HttpPost]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<PlanResponseDto>> Create([FromBody] CreatePlanDto dto, [FromQuery] string lang = "ar")
        {
            var validationError = Validate(dto, lang);
            if (validationError != null) return BadRequest(validationError);

            var nameExists = await _db.Plans.AnyAsync(p => p.Name == dto.Name);
            if (nameExists)
                return BadRequest(Msg(lang, "اسم الخطة مستخدم مسبقاً", "This plan name is already in use"));

            var plan = new Plan
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                Name = dto.Name.Trim(),
                Description = dto.Description?.Trim(),
                MonthlyPrice = dto.MonthlyPrice,
                YearlyPrice = dto.YearlyPrice,
                MaxUsers = dto.MaxUsers,
                MaxDoctors = dto.MaxDoctors,
                MaxPatients = dto.MaxPatients,
                MaxDailyMessages = dto.MaxDailyMessages,
                FeaturesText = NormalizeFeatures(dto.FeaturesText),
                IsFeatured = dto.IsFeatured,
                HasElectronicInvoicing = dto.HasElectronicInvoicing,
                HasMultipleDepartments = dto.HasMultipleDepartments,

            };

            // ✅ قاعدة عمل: خطة واحدة بس تُعرض كـ "الأكثر اختياراً" بأي لحظة —
            // وإلا تظهر أكثر من بطاقة مميّزة بصفحة الأسعار بنفس الوقت، وهذا يكسر التصميم.
            if (plan.IsFeatured)
                await UnfeatureAllOtherPlans(exceptId: null);

            _db.Plans.Add(plan);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = plan.Id }, ToResponse(plan));
        }

        // ═══════════════════════════════════════
        // PUT: api/plans/{id}
        // ═══════════════════════════════════════
        [HttpPut("{id}")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<PlanResponseDto>> Update(Guid id, [FromBody] CreatePlanDto dto, [FromQuery] string lang = "ar")
        {
            var plan = await _db.Plans.FindAsync(id);
            if (plan == null) return NotFound();

            var validationError = Validate(dto, lang);
            if (validationError != null) return BadRequest(validationError);

            var nameExists = await _db.Plans.AnyAsync(p => p.Name == dto.Name && p.Id != id);
            if (nameExists)
                return BadRequest(Msg(lang, "اسم الخطة مستخدم مسبقاً", "This plan name is already in use"));

            plan.Name = dto.Name.Trim();
            plan.Description = dto.Description?.Trim();
            plan.MonthlyPrice = dto.MonthlyPrice;
            plan.YearlyPrice = dto.YearlyPrice;
            plan.MaxUsers = dto.MaxUsers;
            plan.MaxDoctors = dto.MaxDoctors;
            plan.MaxPatients = dto.MaxPatients;
            plan.MaxDailyMessages = dto.MaxDailyMessages;
            plan.FeaturesText = NormalizeFeatures(dto.FeaturesText);
            plan.IsFeatured = dto.IsFeatured;
            plan.HasElectronicInvoicing = dto.HasElectronicInvoicing;
            plan.HasMultipleDepartments = dto.HasMultipleDepartments;

            if (plan.IsFeatured)
                await UnfeatureAllOtherPlans(exceptId: plan.Id);

            await _db.SaveChangesAsync();
            return Ok(ToResponse(plan));
        }

        // ═══════════════════════════════════════
        // PATCH: api/plans/{id}/toggle
        // ═══════════════════════════════════════
        [HttpPatch("{id}/toggle")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult> Toggle(Guid id, [FromQuery] string lang = "ar")
        {
            var plan = await _db.Plans.FindAsync(id);
            if (plan == null) return NotFound();

            var wasActive = plan.IsActive;
            plan.IsActive = !plan.IsActive;
            await _db.SaveChangesAsync();

            // ✅ تحذير (مو منع) لو عطّلنا خطة عليها عيادات مشتركة حالياً —
            // القرار يبقى بيد SuperAdmin، لكن بدون مفاجأة صامتة لاحقاً.
            int? activeSubscriptionsCount = null;
            if (wasActive && !plan.IsActive)
            {
                activeSubscriptionsCount = await _db.Subscriptions
                    .CountAsync(s => s.PlanId == id && s.IsActive);
            }

            return Ok(new
            {
                message = plan.IsActive
                    ? Msg(lang, "تم تفعيل الخطة", "Plan activated")
                    : Msg(lang, "تم تعطيل الخطة", "Plan deactivated"),
                isActive = plan.IsActive,
                warning = activeSubscriptionsCount > 0
                    ? Msg(lang,
                        $"تنبيه: يوجد {activeSubscriptionsCount} عيادة مشتركة حالياً بهذه الخطة، لن تتمكن من التجديد بنفس الخطة",
                        $"Warning: {activeSubscriptionsCount} clinic(s) are currently subscribed to this plan and won't be able to renew on it")
                    : null,
            });
        }

        // ═══════════════════════════════════════
        // Helpers
        // ═══════════════════════════════════════

        /// <summary>يتحقق من صحة بيانات الخطة قبل الحفظ. يرجع رسالة خطأ أو null لو كل شيء سليم.</summary>
        private static object? Validate(CreatePlanDto dto, string lang)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return new { message = Msg(lang, "اسم الخطة مطلوب", "Plan name is required") };

            if (dto.MonthlyPrice < 0 || dto.YearlyPrice < 0)
                return new { message = Msg(lang, "السعر لا يمكن أن يكون سالباً", "Price cannot be negative") };

            // -1 تعني "غير محدود" حسب اتفاقية النظام؛ أي رقم أقل من -1 غير منطقي.
            // (MaxDailyMessages كان مستثنى من هذي القاعدة بالغلط — رغم إن الواجهة والخدمة
            // اللي تتحقق من الحد اليومي (NotificationService) كانتا تتعاملان معه كـ-1=غير محدود أصلاً)
            if (dto.MaxUsers < -1 || dto.MaxDoctors < -1 || dto.MaxPatients < -1 || dto.MaxDailyMessages < -1)
                return new { message = Msg(lang, "الحدود يجب أن تكون -1 (غير محدود) أو رقماً موجباً", "Limits must be -1 (unlimited) or a positive number") };

            return null;
        }



        /// <summary>يحوّل نص المميزات (سطر لكل ميزة) لصيغة موحّدة، ويزيل الأسطر الفارغة والمسافات الزائدة.</summary>
        private static string? NormalizeFeatures(string? featuresText)
        {
            if (string.IsNullOrWhiteSpace(featuresText)) return null;

            var lines = featuresText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => l.Length > 0);

            var joined = string.Join('\n', lines);
            return string.IsNullOrWhiteSpace(joined) ? null : joined;
        }

        /// <summary>يلغي تمييز أي خطة ثانية غير الخطة الحالية، عشان يبقى فيه خطة "مميّزة" واحدة بس بأي وقت.</summary>
        private async Task UnfeatureAllOtherPlans(Guid? exceptId)
        {
            var others = await _db.Plans
                .Where(p => p.IsFeatured && p.Id != exceptId)
                .ToListAsync();

            foreach (var p in others)
                p.IsFeatured = false;
        }

        private static PlanResponseDto ToResponse(Plan p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            MonthlyPrice = p.MonthlyPrice,
            YearlyPrice = p.YearlyPrice,
            MaxUsers = p.MaxUsers,
            MaxDoctors = p.MaxDoctors,
            MaxPatients = p.MaxPatients,
            MaxDailyMessages = p.MaxDailyMessages,
            IsActive = p.IsActive,
            CreatedAt = p.CreatedAt,
            Features = string.IsNullOrWhiteSpace(p.FeaturesText)
                ? new List<string>()
                : p.FeaturesText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            IsFeatured = p.IsFeatured,
            HasElectronicInvoicing = p.HasElectronicInvoicing,
            HasMultipleDepartments = p.HasMultipleDepartments,
        };
    }
}