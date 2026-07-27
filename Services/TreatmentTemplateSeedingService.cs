using ClinicSaaS.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Services
{
    public interface ITreatmentTemplateSeedingService
    {
        Task<int> SeedDefaultTemplates(Guid clinicId);
    }

    /// <summary>
    /// يُنشئ 4 قوالب زيارة افتراضية (كشف، مراجعة، استشارة، متابعة) للعيادة —
    /// بدون ربطها بأي قسم معيّن (DepartmentId = null)، يعني تظهر تلقائياً لكل الأقسام
    /// بما إن GET /api/treatmentplans/templates يرجّع كل قوالب العيادة بغض النظر عن القسم.
    /// آمنة التكرار (idempotent) — ما تكرر القوالب لو نُوديت مرتين.
    /// </summary>
    public class TreatmentTemplateSeedingService : ITreatmentTemplateSeedingService
    {
        private readonly ApplicationDbContext _db;

        public TreatmentTemplateSeedingService(ApplicationDbContext db)
        {
            _db = db;
        }

        private static readonly (string Name, string NameEn)[] DefaultTemplates = new[]
        {
            ("كشف",      "Examination"),
            ("مراجعة",   "Review"),
            ("استشارة",  "Consultation"),
            ("متابعة",   "Follow-up"),
        };

        public async Task<int> SeedDefaultTemplates(Guid clinicId)
        {
            int added = 0;

            foreach (var tpl in DefaultTemplates)
            {
                // ✅ نتحقق من القوالب "العامة" بس (DepartmentId == null) — عشان ما نتعارض
                // مع قالب مخصص بنفس الاسم أنشأه المستخدم يدوياً لقسم معيّن
                var exists = await _db.TreatmentPlanTemplates
                    .AnyAsync(t => t.ClinicId == clinicId && t.DepartmentId == null && t.Name == tpl.Name);
                if (exists) continue;

                _db.TreatmentPlanTemplates.Add(new TreatmentPlanTemplate
                {
                    Id = Guid.NewGuid(),
                    ClinicId = clinicId,
                    DepartmentId = null,   // ✅ عام لكل الأقسام
                    Name = tpl.Name,
                    NameEn = tpl.NameEn,
                    DefaultSessionsCount = 1,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                });
                added++;
            }

            if (added > 0)
                await _db.SaveChangesAsync();

            return added;
        }
    }
}