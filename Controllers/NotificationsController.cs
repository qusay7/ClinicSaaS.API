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
    [Authorize]
    [RequireActiveSubscription]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notif;
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(
            INotificationService notif,
            ApplicationDbContext db,
            IClinicContext clinicContext,
            ILogger<NotificationsController> logger)
        {
            _notif = notif;
            _db = db;
            _clinicContext = clinicContext;
            _logger = logger;
        }

        // ══════════════════════════════════════
        // POST - إرسال تأكيد يدوي
        // ══════════════════════════════════════
        /// <summary>
        /// إرسال تأكيد الحجز يدويًا لموعد محدد
        /// </summary>
        [HttpPost("send-confirmation/{appointmentId}")]
        public async Task<ActionResult> SendConfirmation(Guid appointmentId)
        {
            if (!_clinicContext.HasPermission("appointments.edit")) return Forbid();
            try
            {
                var appointment = await _db.Appointments
                    .Include(a => a.Patient)
                    .Include(a => a.Doctor)
                    .FirstOrDefaultAsync(a => a.Id == appointmentId && !a.IsDeleted);

                if (appointment == null)
                    return NotFound(new { error = "الموعد غير موجود" });

                if (appointment.ClinicId != _clinicContext.ClinicId)
                    return Forbid();

                await _notif.SendAppointmentConfirmation(appointment);

                return Ok(new
                {
                    message = "✅ تم إرسال التأكيد",
                    appointmentId = appointment.Id,
                    phone = appointment.Patient?.Phone
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending confirmation");
                return BadRequest(new { error = ex.Message });
            }
        }

        // ══════════════════════════════════════
        // ✅ POST - إرسال إشعار الحذف (جديد)
        // ══════════════════════════════════════
        /// <summary>
        /// إرسال إشعار إلغاء الموعد للمريض
        /// </summary>
        [HttpPost("send-cancellation/{appointmentId}")]
        public async Task<ActionResult> SendCancellation(Guid appointmentId)
        {
            if (!_clinicContext.HasPermission("appointments.edit")) return Forbid();
            try
            {
                var appointment = await _db.Appointments
                    .Include(a => a.Patient)
                    .Include(a => a.Doctor)
                    .FirstOrDefaultAsync(a => a.Id == appointmentId);

                if (appointment == null)
                    return NotFound(new { error = "الموعد غير موجود" });

                if (appointment.ClinicId != _clinicContext.ClinicId)
                    return Forbid();

                // ✅ إرسال إشعار الحذف
                await _notif.SendAppointmentCancellation(appointment);

                return Ok(new
                {
                    message = "✅ تم إرسال إشعار الإلغاء",
                    appointmentId = appointment.Id,
                    phone = appointment.Patient?.Phone
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending cancellation");
                return BadRequest(new { error = ex.Message });
            }
        }

        // ══════════════════════════════════════
        // ✅ POST - إرسال تذكير مخصص (جديد)
        // ══════════════════════════════════════
        /// <summary>
        /// إرسال تذكير مخصص قبل X ساعة
        /// </summary>
        [HttpPost("send-custom-reminder/{appointmentId}")]
        public async Task<ActionResult> SendCustomReminder(
            Guid appointmentId,
            [FromQuery] int hoursBeforeAppointment = 1)
        {
            if (!_clinicContext.HasPermission("appointments.edit")) return Forbid();
            try
            {
                if (hoursBeforeAppointment <= 0)
                    return BadRequest(new { error = "عدد الساعات يجب أن يكون أكبر من صفر" });

                var appointment = await _db.Appointments
                    .Include(a => a.Patient)
                    .Include(a => a.Doctor)
                    .FirstOrDefaultAsync(a => a.Id == appointmentId && !a.IsDeleted);

                if (appointment == null)
                    return NotFound(new { error = "الموعد غير موجود" });

                if (appointment.ClinicId != _clinicContext.ClinicId)
                    return Forbid();

                // ✅ إرسال التذكير المخصص
                await _notif.SendCustomReminder(appointmentId, hoursBeforeAppointment);

                return Ok(new
                {
                    message = $"✅ تم إرسال تذكير قبل {hoursBeforeAppointment} ساعات",
                    appointmentId = appointment.Id,
                    phone = appointment.Patient?.Phone,
                    hoursBeforeAppointment
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending custom reminder");
                return BadRequest(new { error = ex.Message });
            }
        }

        // ══════════════════════════════════════
        // POST - إرسال تذكيرات اليوم السابق
        // ══════════════════════════════════════
        /// <summary>
        /// تشغيل يدوي لتذكيرات اليوم السابق
        /// (عادة ما يعمل تلقائياً في الخلفية)
        /// </summary>
        [HttpPost("send-day-before")]
        [Authorize(Roles = "SuperAdmin,ClinicAdmin")]
        [RequireActiveSubscription]
        public async Task<ActionResult> SendDayBefore()
        {
            try
            {
                await _notif.SendDayBeforeReminders();
                return Ok(new { message = "✅ تم إرسال تذكيرات اليوم السابق" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending day before reminders");
                return BadRequest(new { error = ex.Message });
            }
        }

        // ══════════════════════════════════════
        // POST - إرسال تذكيرات قبل الساعة
        // ══════════════════════════════════════
        /// <summary>
        /// تشغيل يدوي لتذكيرات قبل الساعة
        /// (عادة ما يعمل تلقائياً في الخلفية)
        /// </summary>
        [HttpPost("send-hour-before")]
        [RequireActiveSubscription]
        [Authorize(Roles = "SuperAdmin,ClinicAdmin")]
        public async Task<ActionResult> SendHourBefore()
        {
            try
            {
                await _notif.SendHourBeforeReminders();
                return Ok(new { message = "✅ تم إرسال تذكيرات قبل الساعة" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending hour before reminders");
                return BadRequest(new { error = ex.Message });
            }
        }

        // ══════════════════════════════════════
        // ✅ POST - إرسال التذكيرات المخصصة (جديد)
        // ══════════════════════════════════════
        /// <summary>
        /// تشغيل يدوي للتذكيرات المخصصة (المحددة لكل موعد)
        /// </summary>
        [HttpPost("send-custom-hour-reminders")]
        [RequireActiveSubscription]
        [Authorize(Roles = "SuperAdmin,ClinicAdmin")]
        public async Task<ActionResult> SendCustomHourReminders()
        {
            try
            {
                await _notif.SendCustomHourReminders();
                return Ok(new { message = "✅ تم إرسال التذكيرات المخصصة" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending custom hour reminders");
                return BadRequest(new { error = ex.Message });
            }
        }

        // ══════════════════════════════════════
        // GET - سجل الإشعارات
        // ══════════════════════════════════════
        /// <summary>
        /// الحصول على سجل الإشعارات المرسلة للعيادة الحالية
        /// </summary>
        [HttpGet("logs")]
        public async Task<ActionResult> GetLogs(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? type = null,  // ✅ تصفية حسب النوع
            [FromQuery] string? channel = null)  // ✅ تصفية حسب القناة
        {
            try
            {
                if (_clinicContext.ClinicId == null)
                    return Unauthorized(new { error = "No clinic context" });

                var query = _db.NotificationLogs
                    .Where(n => n.ClinicId == _clinicContext.ClinicId);

                // ✅ تصفية حسب النوع
                if (!string.IsNullOrEmpty(type))
                    query = query.Where(n => n.Type == type);

                // ✅ تصفية حسب القناة
                if (!string.IsNullOrEmpty(channel))
                    query = query.Where(n => n.Channel == channel);

                query = query.OrderByDescending(n => n.SentAt);

                var total = await query.CountAsync();
                var logs = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(n => new
                    {
                        n.Id,
                        n.Type,
                        n.Channel,
                        n.Phone,
                        n.Message,
                        n.IsSuccess,
                        n.SentAt,
                        n.ErrorMessage,
                        appointmentId = n.AppointmentId,
                        patientId = n.PatientId
                    })
                    .ToListAsync();

                return Ok(new
                {
                    total,
                    page,
                    pageSize,
                    totalPages = (int)Math.Ceiling(total / (double)pageSize),
                    logs
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notification logs");
                return BadRequest(new { error = ex.Message });
            }
        }

        // ══════════════════════════════════════
        // GET - إحصائيات الإشعارات
        // ══════════════════════════════════════
        /// <summary>
        /// إحصائيات الإشعارات المرسلة (حسب النوع والنجاح)
        /// </summary>
        [HttpGet("stats")]
        public async Task<ActionResult> GetStats()
        {
            try
            {
                if (_clinicContext.ClinicId == null)
                    return Unauthorized(new { error = "No clinic context" });

                // ✅ إحصائيات حسب النوع
                var byType = await _db.NotificationLogs
                    .Where(n => n.ClinicId == _clinicContext.ClinicId)
                    .GroupBy(n => n.Type)
                    .Select(g => new
                    {
                        type = g.Key,
                        total = g.Count(),
                        success = g.Count(x => x.IsSuccess),
                        failed = g.Count(x => !x.IsSuccess)
                    })
                    .ToListAsync();

                // ✅ إحصائيات حسب القناة
                var byChannel = await _db.NotificationLogs
                    .Where(n => n.ClinicId == _clinicContext.ClinicId)
                    .GroupBy(n => n.Channel)
                    .Select(g => new
                    {
                        channel = g.Key,
                        total = g.Count(),
                        success = g.Count(x => x.IsSuccess)
                    })
                    .ToListAsync();

                // ✅ إجمالي الإحصائيات
                var total = await _db.NotificationLogs
                    .Where(n => n.ClinicId == _clinicContext.ClinicId)
                    .CountAsync();

                var successCount = await _db.NotificationLogs
                    .Where(n => n.ClinicId == _clinicContext.ClinicId && n.IsSuccess)
                    .CountAsync();

                return Ok(new
                {
                    summary = new
                    {
                        total,
                        success = successCount,
                        failed = total - successCount,
                        successRate = total > 0 ? Math.Round((successCount / (double)total) * 100, 2) : 0
                    },
                    byType,
                    byChannel
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notification stats");
                return BadRequest(new { error = ex.Message });
            }
        }

        // ══════════════════════════════════════
        // ✅ GET - إحصائيات يومية (جديد)
        // ══════════════════════════════════════
        /// <summary>
        /// إحصائيات الإشعارات حسب التاريخ
        /// </summary>
        [HttpGet("stats/daily")]
        public async Task<ActionResult> GetDailyStats(
            [FromQuery] int days = 7)  // آخر 7 أيام
        {
            try
            {
                if (_clinicContext.ClinicId == null)
                    return Unauthorized(new { error = "No clinic context" });

                var startDate = DateTime.UtcNow.AddDays(-days);

                var dailyStats = await _db.NotificationLogs
                    .Where(n => n.ClinicId == _clinicContext.ClinicId
                        && n.SentAt >= startDate)
                    .GroupBy(n => n.SentAt.Date)
                    .Select(g => new
                    {
                        date = g.Key,
                        total = g.Count(),
                        success = g.Count(x => x.IsSuccess),
                        failed = g.Count(x => !x.IsSuccess)
                    })
                    .OrderBy(x => x.date)
                    .ToListAsync();

                return Ok(dailyStats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting daily stats");
                return BadRequest(new { error = ex.Message });
            }
        }
        // ═══════════════════════════════════════════════════════════════════════════
        // NotificationsController.cs — إضافة endpoint واحد فقط
        // ═══════════════════════════════════════════════════════════════════════════

        // أضيف هذا الـ endpoint بعد SendCancellation (في نفس المكان تقريباً):

        // ══════════════════════════════════════
        // ✅ POST - إرسال إشعار التعديل (ناقص!)
        // ══════════════════════════════════════
        /// <summary>
        /// إرسال إشعار تعديل الموعد للمريض
        /// </summary>
        [HttpPost("send-update/{appointmentId}")]
        public async Task<ActionResult> SendUpdate(Guid appointmentId)
        {
            if (!_clinicContext.HasPermission("appointments.edit")) return Forbid();
            try
            {
                var appointment = await _db.Appointments
                    .Include(a => a.Patient)
                    .Include(a => a.Doctor)
                    .FirstOrDefaultAsync(a => a.Id == appointmentId && !a.IsDeleted);

                if (appointment == null)
                    return NotFound(new { error = "الموعد غير موجود" });

                if (appointment.ClinicId != _clinicContext.ClinicId)
                    return Forbid();

                // ✅ إرسال إشعار التعديل
                await _notif.SendAppointmentUpdate(appointment);

                return Ok(new
                {
                    message = "✅ تم إرسال إشعار التعديل",
                    appointmentId = appointment.Id,
                    phone = appointment.Patient?.Phone
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending update notification");
                return BadRequest(new { error = ex.Message });
            }
        }

        /*
        ═══════════════════════════════════════════════════════════════════════════
        ملخص الإضافة:

        في NotificationsController، أضيف endpoint واحد فقط:

        [HttpPost("send-update/{appointmentId}")]
        → POST /api/notifications/send-update/{appointmentId}
        → يستدعي: _notif.SendAppointmentUpdate(appointment)
        → الرسالة: "تم تعديل موعدك 🔄"

        هذا الـ endpoint يقابل الـ endpoints الأخرى:
        ✅ send-confirmation/{appointmentId}
        ✅ send-cancellation/{appointmentId}
        ✅ send-update/{appointmentId} ← جديد!
        ✅ send-custom-reminder/{appointmentId}

        ═══════════════════════════════════════════════════════════════════════════
        */
        // ══════════════════════════════════════
        // ✅ DELETE - حذف سجل إشعار (جديد)
        // ══════════════════════════════════════
        /// <summary>
        /// حذف سجل إشعار محدد
        /// </summary>
        [HttpDelete("logs/{notificationId}")]
        [RequireActiveSubscription]
        [Authorize(Roles = "SuperAdmin,ClinicAdmin")]
        public async Task<ActionResult> DeleteNotificationLog(Guid notificationId)
        {
            try
            {
                var log = await _db.NotificationLogs
                    .FirstOrDefaultAsync(n => n.Id == notificationId);

                if (log == null)
                    return NotFound(new { error = "السجل غير موجود" });

                if (log.ClinicId != _clinicContext.ClinicId)
                    return Forbid();

                _db.NotificationLogs.Remove(log);
                await _db.SaveChangesAsync();

                return Ok(new { message = "✅ تم حذف السجل" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting notification log");
                return BadRequest(new { error = ex.Message });
            }
        }

        // ══════════════════════════════════════
        // جرس إشعارات الواجهة — قائمة موحّدة لكل موظفي العيادة
        // ══════════════════════════════════════

        // GET: api/notifications?take=20
        [HttpGet]
        public async Task<ActionResult> GetAll([FromQuery] int take = 20)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var items = await _db.AppNotifications
                .Where(n => n.ClinicId == _clinicContext.ClinicId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(take)
                .Select(n => new
                {
                    id = n.Id,
                    title = n.Title,
                    message = n.Message,
                    type = n.Type,
                    read = n.IsRead,
                    createdAt = n.CreatedAt,
                })
                .ToListAsync();

            return Ok(items);
        }

        // PUT: api/notifications/{id}/read
        [HttpPut("{id}/read")]
        public async Task<ActionResult> MarkAsRead(Guid id)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var notif = await _db.AppNotifications.FirstOrDefaultAsync(n => n.Id == id);
            if (notif == null) return NotFound();
            if (notif.ClinicId != _clinicContext.ClinicId) return Forbid();

            notif.IsRead = true;
            await _db.SaveChangesAsync();
            return Ok();
        }

        // PUT: api/notifications/read-all
        [HttpPut("read-all")]
        public async Task<ActionResult> MarkAllAsRead()
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            await _db.AppNotifications
                .Where(n => n.ClinicId == _clinicContext.ClinicId && !n.IsRead)
                .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true));

            return Ok();
        }
    }
}