using ClinicSaaS.API.Data;
using Microsoft.EntityFrameworkCore;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace ClinicSaaS.API.Services
{
    public interface INotificationService
    {
        Task<bool> SendWhatsApp(string toPhone, string message);
        Task<bool> SendSms(string toPhone, string message);
        Task SendAppointmentConfirmation(Appointment appointment);
        Task SendDayBeforeReminders();
        Task SendHourBeforeReminders();
    }

    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<NotificationService> _logger;

        private string AccountSid => _config["Twilio:AccountSid"]!;
        private string AuthToken => _config["Twilio:AuthToken"]!;
        private string FromNumber => _config["Twilio:FromNumber"]!;  // whatsapp:+14155238886
        private string FromSms => _config["Twilio:FromSms"]!;     // +19569173368

        public NotificationService(ApplicationDbContext db, IConfiguration config, ILogger<NotificationService> logger)
        {
            _db = db; _config = config; _logger = logger;
        }

        // ══════════════════════════════════════
        // إرسال WhatsApp
        // ══════════════════════════════════════
        public async Task<bool> SendWhatsApp(string toPhone, string message)
        {
            try
            {
                TwilioClient.Init(AccountSid, AuthToken);
                var to = new PhoneNumber($"whatsapp:{toPhone}");
                var from = new PhoneNumber(FromNumber);

                var msg = await MessageResource.CreateAsync(
                    body: message, from: from, to: to);

                _logger.LogInformation("WhatsApp sent to {Phone}: {Sid}", toPhone, msg.Sid);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send WhatsApp to {Phone}", toPhone);
                return false;
            }
        }

        // ══════════════════════════════════════
        // إرسال SMS
        // ══════════════════════════════════════
        public async Task<bool> SendSms(string toPhone, string message)
        {
            try
            {
                TwilioClient.Init(AccountSid, AuthToken);
                var msg = await MessageResource.CreateAsync(
                    body: message,
                    from: new PhoneNumber(FromSms),
                    to: new PhoneNumber(toPhone));

                _logger.LogInformation("SMS sent to {Phone}: {Sid}", toPhone, msg.Sid);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SMS to {Phone}", toPhone);
                return false;
            }
        }

        // ══════════════════════════════════════
        // 1 — تأكيد عند الحجز
        // ══════════════════════════════════════
        public async Task SendAppointmentConfirmation(Appointment appointment)
        {
            await _db.Entry(appointment).Reference(a => a.Patient).LoadAsync();
            if (appointment.DoctorId.HasValue)
                await _db.Entry(appointment).Reference(a => a.Doctor).LoadAsync();

            var patient = appointment.Patient;
            if (string.IsNullOrEmpty(patient?.Phone)) return;

            var clinic = await _db.Clinics.FindAsync(appointment.ClinicId);
            var localTime = ToJordanTime(appointment.AppointmentDate);

            var arMsg = $"""
                🏥 *{clinic?.Name ?? "العيادة"}*
                
                مرحباً {patient.FullName}،
                تم تأكيد موعدك بنجاح ✅
                
                📅 التاريخ: {localTime:dd/MM/yyyy}
                🕐 الوقت: {localTime:hh:mm tt}
                👨‍⚕️ الطبيب: {appointment.Doctor?.FullName ?? "—"}
                
                نراك قريباً 🌟
                للإلغاء أو التعديل يرجى الاتصال بنا.
                """;

            var enMsg = $"""
                🏥 *{clinic?.Name ?? "Clinic"}*
                
                Hello {patient.FullName},
                Your appointment is confirmed ✅
                
                📅 Date: {localTime:dd/MM/yyyy}
                🕐 Time: {localTime:hh:mm tt}
                👨‍⚕️ Doctor: {appointment.Doctor?.FullName ?? "—"}
                
                See you soon 🌟
                To cancel or reschedule, please contact us.
                """;

            var phone = NormalizePhone(patient.Phone);
            var msg = arMsg;

            await SendWhatsApp(phone, msg);
            await LogNotification(appointment.Id, appointment.ClinicId, patient.Id, "confirmation", phone, msg);
        }

        // ══════════════════════════════════════
        // 2 — تذكير قبل يوم (يعمل كل صباح 9:00)
        // ══════════════════════════════════════
        public async Task SendDayBeforeReminders()
        {
            var jordanNow = ToJordanTime(DateTime.UtcNow);
            var tomorrow = jordanNow.Date.AddDays(1);
            var tomorrowEnd = tomorrow.AddDays(1);

            var appointments = await _db.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .Include(a => a.Clinic)
                .Where(a => !a.IsDeleted
                    && a.Status != "cancelled"
                    && a.Status != "completed"
                    && a.AppointmentDate >= tomorrow
                    && a.AppointmentDate < tomorrowEnd)
                .ToListAsync();

            _logger.LogInformation("Day-before reminders: {Count} appointments", appointments.Count);

            foreach (var appt in appointments)
            {
                if (string.IsNullOrEmpty(appt.Patient?.Phone)) continue;

                // تحقق لم يُرسل مسبقاً
                var alreadySent = await _db.NotificationLogs.AnyAsync(n =>
                    n.AppointmentId == appt.Id && n.Type == "day_before");
                if (alreadySent) continue;

                var localTime = ToJordanTime(appt.AppointmentDate);
                var phone = NormalizePhone(appt.Patient.Phone);

                var arMsg = $"""
                    🔔 *تذكير بموعدك غداً*
                    
                    {appt.Patient.FullName}،
                    لديك موعد غداً في {appt.Clinic?.Name ?? "العيادة"} 📅
                    
                    🕐 الوقت: {localTime:hh:mm tt}
                    👨‍⚕️ الطبيب: {appt.Doctor?.FullName ?? "—"}
                    
                    يرجى الحضور قبل 10 دقائق ⏰
                    """;

                var enMsg = $"""
                    🔔 *Appointment Reminder — Tomorrow*
                    
                    {appt.Patient.FullName},
                    You have an appointment tomorrow at {appt.Clinic?.Name ?? "the clinic"} 📅
                    
                    🕐 Time: {localTime:hh:mm tt}
                    👨‍⚕️ Doctor: {appt.Doctor?.FullName ?? "—"}
                    
                    Please arrive 10 minutes early ⏰
                    """;

                var msg = arMsg;
                await SendWhatsApp(phone, msg);
                await LogNotification(appt.Id, appt.ClinicId, appt.Patient.Id, "day_before", phone, msg);
            }
        }

        // ══════════════════════════════════════
        // 3 — تذكير قبل ساعة (يعمل كل ساعة)
        // ══════════════════════════════════════
        public async Task SendHourBeforeReminders()
        {
            var jordanNow = ToJordanTime(DateTime.UtcNow);
            var from = jordanNow.AddMinutes(55);
            var to = jordanNow.AddMinutes(65);

            // حوّل للـ UTC للمقارنة مع قاعدة البيانات
            var fromUtc = from.ToUniversalTime();
            var toUtc = to.ToUniversalTime();

            var appointments = await _db.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .Include(a => a.Clinic)
                .Where(a => !a.IsDeleted
                    && a.Status != "cancelled"
                    && a.Status != "completed"
                    && a.AppointmentDate >= fromUtc
                    && a.AppointmentDate <= toUtc)
                .ToListAsync();

            _logger.LogInformation("Hour-before reminders: {Count} appointments", appointments.Count);

            foreach (var appt in appointments)
            {
                if (string.IsNullOrEmpty(appt.Patient?.Phone)) continue;

                var alreadySent = await _db.NotificationLogs.AnyAsync(n =>
                    n.AppointmentId == appt.Id && n.Type == "hour_before");
                if (alreadySent) continue;

                var localTime = ToJordanTime(appt.AppointmentDate);
                var phone = NormalizePhone(appt.Patient.Phone);

                var arMsg = $"""
                    ⏰ *موعدك بعد ساعة!*
                    
                    {appt.Patient.FullName}،
                    موعدك في {appt.Clinic?.Name ?? "العيادة"} بعد ساعة تقريباً 🏥
                    
                    🕐 الوقت: {localTime:hh:mm tt}
                    👨‍⚕️ الطبيب: {appt.Doctor?.FullName ?? "—"}
                    
                    في انتظارك 🌟
                    """;

                var enMsg = $"""
                    ⏰ *Your appointment is in 1 hour!*
                    
                    {appt.Patient.FullName},
                    Your appointment at {appt.Clinic?.Name ?? "the clinic"} is in about 1 hour 🏥
                    
                    🕐 Time: {localTime:hh:mm tt}
                    👨‍⚕️ Doctor: {appt.Doctor?.FullName ?? "—"}
                    
                    See you soon 🌟
                    """;

                var msg = arMsg;
                await SendWhatsApp(phone, msg);
                await LogNotification(appt.Id, appt.ClinicId, appt.Patient.Id, "hour_before", phone, msg);
            }
        }

        // ══════════════════════════════════════
        // دوال مساعدة
        // ══════════════════════════════════════
        private async Task LogNotification(Guid appointmentId, Guid clinicId, Guid patientId, string type, string phone, string message)
        {
            _db.NotificationLogs.Add(new NotificationLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointmentId,
                ClinicId = clinicId,
                PatientId = patientId,
                Type = type,
                Channel = "whatsapp",
                Phone = phone,
                Message = message,
                SentAt = DateTime.UtcNow,
                IsSuccess = true,
            });
            await _db.SaveChangesAsync();
        }

        private static DateTime ToJordanTime(DateTime utc)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Amman");
                return TimeZoneInfo.ConvertTimeFromUtc(
                    utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime(), tz);
            }
            catch { return utc.AddHours(3); }
        }

        private static string NormalizePhone(string phone)
        {
            phone = phone.Trim().Replace(" ", "").Replace("-", "");
            if (phone.StartsWith("07")) phone = "+962" + phone[1..];
            if (phone.StartsWith("7") && phone.Length == 9) phone = "+962" + phone;
            if (!phone.StartsWith("+")) phone = "+" + phone;
            return phone;
        }
    }
}