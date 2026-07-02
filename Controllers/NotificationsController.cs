using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notif;
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public NotificationsController(INotificationService notif, ApplicationDbContext db, IClinicContext clinicContext)
        {
            _notif = notif; _db = db; _clinicContext = clinicContext;
        }

        // POST: api/notifications/send-confirmation/{appointmentId}
        // إرسال تأكيد يدوي لموعد محدد
        [HttpPost("send-confirmation/{appointmentId}")]
        public async Task<ActionResult> SendConfirmation(Guid appointmentId)
        {
            var appointment = await _db.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .FirstOrDefaultAsync(a => a.Id == appointmentId && !a.isdeleted);

            if (appointment == null) return NotFound("الموعد غير موجود");
            if (appointment.ClinicId != _clinicContext.ClinicId) return Forbid();

            await _notif.SendAppointmentConfirmation(appointment);
            return Ok(new { message = "تم إرسال التأكيد" });
        }

        // POST: api/notifications/send-day-before
        // تشغيل يدوي لتذكيرات اليوم السابق
        [HttpPost("send-day-before")]
        [Authorize(Roles = "SuperAdmin,ClinicAdmin")]
        public async Task<ActionResult> SendDayBefore()
        {
            await _notif.SendDayBeforeReminders();
            return Ok(new { message = "تم إرسال تذكيرات اليوم السابق" });
        }

        // POST: api/notifications/send-hour-before
        // تشغيل يدوي لتذكيرات قبل الساعة
        [HttpPost("send-hour-before")]
        [Authorize(Roles = "SuperAdmin,ClinicAdmin")]
        public async Task<ActionResult> SendHourBefore()
        {
            await _notif.SendHourBeforeReminders();
            return Ok(new { message = "تم إرسال تذكيرات قبل الساعة" });
        }

        // GET: api/notifications/logs
        // سجل الإشعارات المرسلة
        [HttpGet("logs")]
        public async Task<ActionResult> GetLogs(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var query = _db.NotificationLogs
                .Where(n => n.ClinicId == _clinicContext.ClinicId)
                .OrderByDescending(n => n.SentAt);

            var total = await query.CountAsync();
            var logs  = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(n => new {
                    n.Id, n.Type, n.Channel, n.Phone,
                    n.IsSuccess, n.SentAt, n.ErrorMessage,
                    appointmentId = n.AppointmentId,
                })
                .ToListAsync();

            return Ok(new { total, page, pageSize, logs });
        }

        // GET: api/notifications/stats
        // إحصائيات الإشعارات
        [HttpGet("stats")]
        public async Task<ActionResult> GetStats()
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var stats = await _db.NotificationLogs
                .Where(n => n.ClinicId == _clinicContext.ClinicId)
                .GroupBy(n => n.Type)
                .Select(g => new { type = g.Key, count = g.Count(), success = g.Count(x => x.IsSuccess) })
                .ToListAsync();

            return Ok(stats);
        }
    }
}
