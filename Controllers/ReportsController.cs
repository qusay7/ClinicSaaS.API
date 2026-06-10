using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IClinicContext _clinicContext;

    public ReportsController(ApplicationDbContext db, IClinicContext clinicContext)
    {
        _db = db;
        _clinicContext = clinicContext;
    }

    [HttpGet]
    public async Task<ActionResult> GetReports()
    {
        if (!_clinicContext.HasPermission("reports.view"))
            return Forbid();

        if (_clinicContext.ClinicId == null)
            return Unauthorized();

        var clinicId = _clinicContext.ClinicId.Value;
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1);

        // ── إجماليات ──
        var totalPatients = await _db.Patients
            .CountAsync(p => p.ClinicId == clinicId && !p.isdeleted);

        var totalDoctors = await _db.Doctors
            .CountAsync(d => d.ClinicId == clinicId && !d.isdeleted && d.IsActive);

        var appointments = await _db.Appointments
            .Include(a => a.Doctor)
            .Where(a => a.ClinicId == clinicId && !a.isdeleted)
            .ToListAsync();

        var totalAppointments = appointments.Count;
        var totalRevenue = appointments
            .Where(a => a.Status == "completed")
            .Sum(a => a.Price ?? 0);

        var completedAppointments = appointments.Count(a => a.Status == "completed");
        var cancelledAppointments = appointments.Count(a => a.Status == "cancelled");
        var scheduledAppointments = appointments.Count(a =>
            a.Status == "scheduled" || a.Status == "confirmed");

        // ── هذا الشهر ──
        var newPatientsThisMonth = await _db.Patients
            .CountAsync(p => p.ClinicId == clinicId
                && !p.isdeleted
                && p.CreatedAt >= startOfMonth);

        var appointmentsThisMonth = appointments
            .Count(a => a.AppointmentDate >= startOfMonth);

        var revenueThisMonth = appointments
            .Where(a => a.Status == "completed" && a.AppointmentDate >= startOfMonth)
            .Sum(a => a.Price ?? 0);

        // ── أكثر الأطباء مواعيد ──
        var topDoctors = appointments
            .Where(a => a.DoctorId != null)
            .GroupBy(a => new { a.DoctorId, a.Doctor!.FullName })
            .Select(g => new {
                doctorName = g.Key.FullName,
                appointmentCount = g.Count()
            })
            .OrderByDescending(d => d.appointmentCount)
            .Take(5)
            .ToList();

        // ── المواعيد الشهرية (آخر 6 أشهر) ──
        var sixMonthsAgo = now.AddMonths(-5);
        var startOf6Months = new DateTime(sixMonthsAgo.Year, sixMonthsAgo.Month, 1);

        var appointmentsByMonth = appointments
            .Where(a => a.AppointmentDate >= startOf6Months)
            .GroupBy(a => new { a.AppointmentDate.Year, a.AppointmentDate.Month })
            .Select(g => new {
                year = g.Key.Year,
                month = g.Key.Month,
                count = g.Count(),
                revenue = g.Where(a => a.Status == "completed")
                           .Sum(a => a.Price ?? 0)
            })
            .OrderBy(g => g.year).ThenBy(g => g.month)
            .ToList();

        return Ok(new
        {
            totalPatients,
            totalAppointments,
            totalDoctors,
            totalRevenue,
            completedAppointments,
            cancelledAppointments,
            scheduledAppointments,
            newPatientsThisMonth,
            appointmentsThisMonth,
            revenueThisMonth,
            topDoctors,
            appointmentsByMonth,
        });
    }
}