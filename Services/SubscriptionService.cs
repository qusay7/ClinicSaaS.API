using ClinicSaaS.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Services
{
    public class SubscriptionService
    {
        private readonly ApplicationDbContext _db;

        public SubscriptionService(ApplicationDbContext db)
        {
            _db = db;
        }


        // جلب الاشتراك النشط للعيادة
        private async Task<Subscription?> GetActiveSubscription(Guid clinicId)
        {
            return await _db.Subscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.IsActive);
        }

        // هل يمكن إضافة مستخدم جديد؟
        public async Task<(bool CanAdd, string? Error)> CanAddUser(Guid clinicId)
        {
            var subscription = await GetActiveSubscription(clinicId);

            // ✅ بدون اشتراك — اسمح (للعيادات الجديدة)
            if (subscription == null)
                return (true, null);

            if (subscription.EndDate < DateTime.UtcNow)
                return (false, "انتهت صلاحية الاشتراك");

            if (subscription.Plan.MaxUsers == -1)
                return (true, null);

            return (true, null);
        }
        // هل يمكن إضافة طبيب جديد؟
        public async Task<(bool CanAdd, string? Error)> CanAddDoctor(Guid clinicId)
        {
            var subscription = await GetActiveSubscription(clinicId);

            if (subscription == null)
                return (false, "لا يوجد اشتراك نشط لهذه العيادة");

            if (subscription.EndDate < DateTime.UtcNow)
                return (false, "انتهت صلاحية الاشتراك");

            if (subscription.Plan.MaxDoctors == -1)
                return (true, null);

            var currentDoctors = await _db.Doctors
                .CountAsync(d => d.ClinicId == clinicId && !d.IsDeleted  && d.IsActive);

            if (currentDoctors >= subscription.Plan.MaxDoctors)
                return (false, $"وصلت للحد الأقصى ({subscription.Plan.MaxDoctors} أطباء) في خطتك");

            return (true, null);
        }
        // هل يمكن إضافة مريض جديد؟
        public async Task<(bool CanAdd, string? Error)> CanAddPatient(Guid clinicId)
        {
            var subscription = await GetActiveSubscription(clinicId);

            if (subscription == null)
                return (false, "لا يوجد اشتراك نشط لهذه العيادة");

            if (subscription.EndDate < DateTime.UtcNow)
                return (false, "انتهت صلاحية الاشتراك");

            if (subscription.Plan.MaxPatients == -1)
                return (true, null);

            var currentPatients = await _db.Patients
                .CountAsync(p => p.ClinicId == clinicId && !p.IsDeleted );

            if (currentPatients >= subscription.Plan.MaxPatients)
                return (false, $"وصلت للحد الأقصى ({subscription.Plan.MaxPatients} مريض) في خطتك");

            return (true, null);
        }
    }

}
