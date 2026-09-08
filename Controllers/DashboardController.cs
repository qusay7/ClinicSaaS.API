using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClinicSaaS.API.Filters;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [RequireActiveSubscription]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public DashboardController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        // GET: api/dashboard
        // إحصائيات العيادة الحالية
        [HttpGet]
        public async Task<ActionResult> GetDashboard()
        {
            // SuperAdmin يرى إحصائيات كاملة عن كل النظام
            if (_clinicContext.IsSuperAdmin)
            {
                var totalClinics = await _db.Clinics.CountAsync(c => c.IsActive);
                var totalUsers = await _db.Users.CountAsync(u => u.IsActive);
                var totalPatients = await _db.Patients.CountAsync(p => !p.IsDeleted);
                var totalDoctors = await _db.Doctors.CountAsync(d => !d.IsDeleted);
                var totalAppointments = await _db.Appointments.CountAsync(a => !a.IsDeleted);
                var activeSubscriptions = await _db.Subscriptions.CountAsync(s => s.IsActive);

                return Ok(new
                {
                    type = "SuperAdmin",
                    totalClinics = totalClinics,
                    totalUsers = totalUsers,
                    totalPatients = totalPatients,
                    totalDoctors = totalDoctors,
                    totalAppointments = totalAppointments,
                    activeSubscriptions = activeSubscriptions
                });
            }

            // باقي المستخدمين يرون إحصائيات عيادتهم فقط
            if (_clinicContext.ClinicId == null)
                return Unauthorized("لا توجد عيادة مرتبطة بهذا المستخدم");

            var clinicId = _clinicContext.ClinicId.Value;
            var today = DateTime.UtcNow.Date;

            // إحصائيات العيادة
            var patients = await _db.Patients
                .CountAsync(p => p.ClinicId == clinicId && !p.IsDeleted);

            var doctors = await _db.Doctors
                .CountAsync(d => d.ClinicId == clinicId && !d.IsDeleted && d.IsActive);
            var staff = await _db.Staff
                .CountAsync(s => s.ClinicId == clinicId && !s.IsDeleted && s.IsActive);

            var appointments = await _db.Appointments
                .CountAsync(a => a.ClinicId == clinicId && !a.IsDeleted);

            // مواعيد اليوم
            var todayAppointments = await _db.Appointments
                .CountAsync(a => a.ClinicId == clinicId
                    && !a.IsDeleted
                    && a.AppointmentDate.Date == today);

            // مواعيد قادمة
            var upcomingAppointments = await _db.Appointments
                .CountAsync(a => a.ClinicId == clinicId
                    && !a.IsDeleted
                    && a.AppointmentDate > DateTime.UtcNow
                    && a.Status == "scheduled");

            // الاشتراك الحالي
            var subscription = await _db.Subscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.IsActive);

            return Ok(new
            {
                type = "Clinic",
                totalPatients = patients,
                totalDoctors = doctors,
                totalStaff = staff,
                totalAppointments = appointments,
                todayAppointments = todayAppointments,
                upcomingAppointments = upcomingAppointments,
                subscription = subscription == null ? null : new
                {
                    planName = subscription.Plan.Name,
                    billingCycle = subscription.BillingCycle,
                    endDate = subscription.EndDate,
                    daysRemaining = (subscription.EndDate - DateTime.UtcNow).Days,
                    maxPatients = subscription.Plan.MaxPatients,
                    maxDoctors = subscription.Plan.MaxDoctors,
                    maxUsers = subscription.Plan.MaxUsers,
                    currentPatients = patients,
                    currentDoctors = doctors,
                    hasElectronicInvoicing = subscription.Plan.HasElectronicInvoicing,
                    hasMultipleDepartments = subscription.Plan.HasMultipleDepartments
                }
            });
        }
    }
}