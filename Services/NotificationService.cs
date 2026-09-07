using ClinicSaaS.API.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ClinicSaaS.API.Services
{
    public interface INotificationService
    {
        Task<bool> SendWhatsApp(string toPhone, string message, Guid clinicId);
        Task<bool> SendSms(string toPhone, string message, Guid clinicId);

        Task SendAppointmentConfirmation(Appointment appointment);
        Task SendAppointmentCancellation(Appointment appointment);
        Task SendAppointmentUpdate(Appointment appointment);

        Task SendCustomReminder(Guid appointmentId, int hoursBeforeAppointment);
        Task SendCustomHourReminders();

        Task SendDayBeforeReminders();
        Task SendHourBeforeReminders();
    }

    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<NotificationService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;



        public async Task SendAppointmentUpdate(Appointment appointment)
        {

            try
            {
                await _db.Entry(appointment)
                         .Reference(a => a.Patient)
                         .LoadAsync();

                if (appointment.DoctorId.HasValue)
                {
                    await _db.Entry(appointment)
                        .Reference(a => a.Doctor)
                        .LoadAsync();
                }

                var patient = appointment.Patient;

                if (string.IsNullOrWhiteSpace(patient?.Phone))
                    return;

                var clinic = await _db.Clinics
                         .FindAsync(appointment.ClinicId);

                var localTime = ToJordanTime(appointment.AppointmentDate);



                var msg = $"""
            🔄 *تم تعديل موعدك*

            مرحباً {patient.FullName}،

            تم تعديل موعدك بنجاح.

            📅 التاريخ: {localTime:dd/MM/yyyy}
            🕐 الوقت: {localTime:hh:mm tt}
            👨‍⚕️ الطبيب: {appointment.Doctor?.FullName ?? "—"}

            🏥 العيادة:
            {clinic?.Name ?? "العيادة"}

            يرجى الالتزام بالموعد الجديد.
            نراك قريباً 🌟
            """;

                var phone = NormalizePhone(patient.Phone);

                var success = await SendWhatsApp(
            phone,
            msg,
            appointment.ClinicId);

                if (success)
                {
                    await LogNotification(
                        appointment.Id,
                        appointment.ClinicId,
                        patient.Id,
                        "update",
                        phone,
                        msg);



                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Error in SendAppointmentUpdate");
            }
        }
        public NotificationService(
            ApplicationDbContext db,
            ILogger<NotificationService> logger,
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _db = db;
            _logger = logger;
            _httpClient = httpClient;
            _configuration = configuration;
        }

        // ══════════════════════════════════════════════════════
        // WhatsApp
        // ══════════════════════════════════════════════════════

        public async Task<bool> SendWhatsApp(
            string toPhone,
            string message,
            Guid clinicId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(toPhone))
                {
                    _logger.LogWarning(
                        "⚠️ Cannot send WhatsApp: phone number is empty. Clinic: {ClinicId}",
                        clinicId);

                    return false;
                }

                if (string.IsNullOrWhiteSpace(message))
                {
                    _logger.LogWarning(
                        "⚠️ Cannot send WhatsApp: message is empty. Clinic: {ClinicId}",
                        clinicId);

                    return false;
                }

                // 1️⃣ جلب العيادة
                var clinic = await _db.Clinics
                    .FirstOrDefaultAsync(c => c.Id == clinicId);

                if (clinic == null)
                {
                    _logger.LogWarning(
                        "❌ Clinic {ClinicId} not found",
                        clinicId);

                    return false;
                }

                // 2️⃣ التحقق من تفعيل الإشعارات
                if (!clinic.IsNotificationsEnabled)
                {
                    _logger.LogWarning(
                        "⚠️ Notifications disabled for clinic {ClinicId}",
                        clinicId);

                    return false;
                }

                // 3.5️⃣ التحقق من حد الرسائل اليومي
                if (await IsDailyLimitReached(clinicId))
                    return false;

                // 4️⃣ تنسيق الرقم
                var phone = NormalizePhone(toPhone);

                if (string.IsNullOrWhiteSpace(phone))
                {
                    _logger.LogWarning(
                        "⚠️ Invalid phone number: {Phone}",
                        toPhone);

                    return false;
                }

                // 5️⃣ بناء URL — خدمة واتساب الخاصة بنا (Baileys)، جلسة مستقلة لكل عيادة
                var baseUrl = _configuration["WhatsAppService:BaseUrl"] ?? "http://localhost:3001";
                var url = $"{baseUrl}/clinics/{clinicId}/send";

                // 6️⃣ Payload
                var payload = new
                {
                    phone,
                    message
                };

                var json = JsonSerializer.Serialize(payload);

                using var content = new StringContent(
                    json,
                    System.Text.Encoding.UTF8,
                    "application/json");

                // 7️⃣ إرسال الطلب
                using var response = await _httpClient.PostAsync(
                    url,
                    content);

                var responseBody =
                    await response.Content.ReadAsStringAsync();

                // 8️⃣ التحقق من HTTP
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "❌ WhatsApp service HTTP error. Phone: {Phone}, Status: {Status}, Response: {Response}",
                        phone,
                        response.StatusCode,
                        responseBody);

                    return false;
                }

                // 9️⃣ محاولة قراءة استجابة الخدمة — { "ok": true } عند النجاح
                var whatsAppServiceSuccess =
                    IsWhatsAppServiceResponseSuccessful(responseBody);

                if (!whatsAppServiceSuccess)
                {
                    _logger.LogError(
                        "❌ WhatsApp service rejected message. Phone: {Phone}, Response: {Response}",
                        phone,
                        responseBody);

                    return false;
                }

                _logger.LogInformation(
                    "✅ WhatsApp sent successfully to {Phone} (Clinic: {ClinicId})",
                    phone,
                    clinicId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Exception sending WhatsApp to {Phone}",
                    toPhone);

                return false;
            }
        }

        // ══════════════════════════════════════════════════════
        // SMS
        // ══════════════════════════════════════════════════════

        public async Task<bool> SendSms(
            string toPhone,
            string message,
            Guid clinicId)
        {
            try
            {
                // Ultramsg المستخدم حاليًا يدعم WhatsApp.
                // لذلك نبقي هذه الدالة كواجهة مستقبلية.
                return await SendWhatsApp(
                    toPhone,
                    message,
                    clinicId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Failed to send SMS to {Phone}",
                    toPhone);

                return false;
            }
        }

        // ══════════════════════════════════════════════════════
        // 1️⃣ تأكيد الموعد
        // ══════════════════════════════════════════════════════

        public async Task SendAppointmentConfirmation(
            Appointment appointment)
        {
            try
            {
                if (appointment == null)
                    return;

                // تحميل Patient
                await _db.Entry(appointment)
                    .Reference(a => a.Patient)
                    .LoadAsync();

                // تحميل Doctor
                if (appointment.DoctorId.HasValue)
                {
                    await _db.Entry(appointment)
                        .Reference(a => a.Doctor)
                        .LoadAsync();
                }

                var patient = appointment.Patient;

                if (patient == null)
                {
                    _logger.LogWarning(
                        "⚠️ Appointment {AppointmentId} has no patient",
                        appointment.Id);

                    return;
                }

                if (string.IsNullOrWhiteSpace(patient.Phone))
                {
                    _logger.LogWarning(
                        "⚠️ Patient {PatientId} has no phone number",
                        patient.Id);

                    return;
                }

                var clinic = await _db.Clinics
                    .FirstOrDefaultAsync(c =>
                        c.Id == appointment.ClinicId);

                var localTime =
                    ToJordanTime(appointment.AppointmentDate);

                var msg = $"""
                    🏥 *{clinic?.Name ?? "العيادة"}*

                    مرحباً {patient.FullName}،

                    تم تأكيد موعدك بنجاح ✅

                    📅 التاريخ: {localTime:dd/MM/yyyy}
                    🕐 الوقت: {localTime:hh:mm tt}
                    👨‍⚕️ الطبيب: {appointment.Doctor?.FullName ?? "—"}

                    نراك قريباً 🌟

                    للإلغاء أو التعديل يرجى الاتصال بنا.
                    """;

                var phone = NormalizePhone(patient.Phone);

                var success = await SendWhatsApp(
      phone,
      msg,
      appointment.ClinicId);

                if (success)
                {
                    await LogNotification(
                        appointment.Id,
                        appointment.ClinicId,
                        patient.Id,
                        "confirmation",
                        phone,
                        msg);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Error in SendAppointmentConfirmation for Appointment {AppointmentId}",
                    appointment?.Id);
            }
        }

        // ══════════════════════════════════════════════════════
        // 2️⃣ إلغاء الموعد
        // ══════════════════════════════════════════════════════

        public async Task SendAppointmentCancellation(
            Appointment appointment)
        {
            try
            {
                if (appointment == null)
                    return;

                await _db.Entry(appointment)
                    .Reference(a => a.Patient)
                    .LoadAsync();

                if (appointment.DoctorId.HasValue)
                {
                    await _db.Entry(appointment)
                        .Reference(a => a.Doctor)
                        .LoadAsync();
                }

                var patient = appointment.Patient;

                if (patient == null)
                    return;

                if (string.IsNullOrWhiteSpace(patient.Phone))
                    return;

                var clinic = await _db.Clinics
                    .FirstOrDefaultAsync(c =>
                        c.Id == appointment.ClinicId);

                var localTime =
                    ToJordanTime(appointment.AppointmentDate);

                var msg = $"""
                    ❌ *إلغاء الموعد*

                    {patient.FullName}،

                    تم إلغاء موعدك بنجاح.

                    📅 التاريخ الملغي: {localTime:dd/MM/yyyy}
                    🕐 الوقت: {localTime:hh:mm tt}
                    👨‍⚕️ الطبيب: {appointment.Doctor?.FullName ?? "—"}

                    إذا كنت تريد حجز موعد آخر،
                    يرجى الاتصال بنا.

                    🏥 {clinic?.Name ?? "العيادة"}
                    """;

                var phone = NormalizePhone(patient.Phone);

                var success = await SendWhatsApp(
     phone,
     msg,
     appointment.ClinicId);

                if (success)
                {
                    await LogNotification(
                        appointment.Id,
                        appointment.ClinicId,
                        patient.Id,
                        "cancellation",
                        phone,
                        msg);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Error in SendAppointmentCancellation for Appointment {AppointmentId}",
                    appointment?.Id);
            }
        }

        // ══════════════════════════════════════════════════════
        // 3️⃣ تذكير مخصص قبل X ساعة
        // ══════════════════════════════════════════════════════

        public async Task SendCustomReminder(
            Guid appointmentId,
            int hoursBeforeAppointment)
        {
            try
            {
                if (hoursBeforeAppointment <= 0)
                {
                    _logger.LogWarning(
                        "⚠️ Invalid custom reminder hours: {Hours}",
                        hoursBeforeAppointment);

                    return;
                }

                var appointment = await _db.Appointments
                    .Include(a => a.Patient)
                    .Include(a => a.Doctor)
                    .Include(a => a.Clinic)
                    .FirstOrDefaultAsync(a =>
                        a.Id == appointmentId);

                if (appointment == null)
                {
                    _logger.LogWarning(
                        "⚠️ Appointment {AppointmentId} not found",
                        appointmentId);

                    return;
                }

                if (appointment.IsDeleted)
                    return;

                if (appointment.Status == "cancelled" ||
                    appointment.Status == "completed")
                {
                    return;
                }

                if (appointment.Patient == null ||
                    string.IsNullOrWhiteSpace(
                        appointment.Patient.Phone))
                {
                    return;
                }

                // وقت الأردن الحالي
                var jordanNow =
                    ToJordanTime(DateTime.UtcNow);

                // وقت الموعد في الأردن
                var appointmentLocalTime =
                    ToJordanTime(
                        appointment.AppointmentDate);

                // وقت إرسال التذكير
                var reminderTime =
                    appointmentLocalTime.AddHours(
                        -hoursBeforeAppointment);

                // إذا لم يحن وقت التذكير
                if (jordanNow < reminderTime)
                {
                    _logger.LogInformation(
                        "⏰ Too early for {Hours}h reminder. Appointment: {AppointmentId}",
                        hoursBeforeAppointment,
                        appointmentId);

                    return;
                }

                // إذا انتهى الموعد
                if (jordanNow >= appointmentLocalTime)
                {
                    return;
                }

                var notificationType =
                    $"custom_{hoursBeforeAppointment}h";

                // منع التكرار إذا كان الإشعار ناجحاً
                var alreadySent =
                    await _db.NotificationLogs.AnyAsync(n =>
                        n.AppointmentId == appointmentId &&
                        n.Type == notificationType &&
                        n.IsSuccess);

                if (alreadySent)
                    return;

                var phone =
                    NormalizePhone(
                        appointment.Patient.Phone);

                var msg = $"""
                    ⏰ *تذكير الموعد*

                    {appointment.Patient.FullName}،

                    موعدك بعد {hoursBeforeAppointment} ساعات ⏳

                    📅 التاريخ: {appointmentLocalTime:dd/MM/yyyy}
                    🕐 الوقت: {appointmentLocalTime:hh:mm tt}
                    👨‍⚕️ الطبيب: {appointment.Doctor?.FullName ?? "—"}
                    🏥 العيادة: {appointment.Clinic?.Name ?? "العيادة"}

                    يرجى تأكيد حضورك ✅
                    أو إلغاء الموعد إذا لزم الأمر.
                    """;

                var success = await SendWhatsApp(
     phone,
     msg,
     appointment.ClinicId);

                if (success)
                {
                    await LogNotification(
                        appointmentId,
                        appointment.ClinicId,
                        appointment.Patient.Id,
                        $"custom_{hoursBeforeAppointment}h",
                        phone,
                        msg);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Error in SendCustomReminder for Appointment {AppointmentId}",
                    appointmentId);
            }
        }

        // ══════════════════════════════════════════════════════
        // 4️⃣ التذكيرات المخصصة الدورية
        // ══════════════════════════════════════════════════════

        public async Task SendCustomHourReminders()
        {
            try
            {
                var appointments =
                    await _db.Appointments
                        .Include(a => a.Patient)
                        .Include(a => a.Doctor)
                        .Include(a => a.Clinic)
                        .Where(a =>
                            !a.IsDeleted &&
                            a.Status != "cancelled" &&
                            a.Status != "completed" &&
                            !string.IsNullOrEmpty(
                                a.CustomReminders))
                        .ToListAsync();

                _logger.LogInformation(
                    "📢 Custom hour reminders: {Count} appointments",
                    appointments.Count);

                foreach (var appointment in appointments)
                {
                    if (appointment.Patient == null ||
                        string.IsNullOrWhiteSpace(
                            appointment.Patient.Phone))
                    {
                        continue;
                    }

                    var reminderHours =
                        ParseCustomReminderHours(
                            appointment.CustomReminders);

                    foreach (var hours in reminderHours)
                    {
                        await SendCustomReminder(
                            appointment.Id,
                            hours);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Error in SendCustomHourReminders");
            }
        }

        // ══════════════════════════════════════════════════════
        // 5️⃣ تذكير قبل يوم
        // ══════════════════════════════════════════════════════

        public async Task SendDayBeforeReminders()
        {
            try
            {
                var jordanNow =
                    ToJordanTime(DateTime.UtcNow);

                var tomorrow =
                    jordanNow.Date.AddDays(1);

                var tomorrowEnd =
                    tomorrow.AddDays(1);

                // نحول النطاق إلى UTC
                var tomorrowUtc =
                    TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(
                            tomorrow,
                            DateTimeKind.Unspecified),
                        JordanTimeZone());

                var tomorrowEndUtc =
                    TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(
                            tomorrowEnd,
                            DateTimeKind.Unspecified),
                        JordanTimeZone());

                var appointments =
                    await _db.Appointments
                        .Include(a => a.Patient)
                        .Include(a => a.Doctor)
                        .Include(a => a.Clinic)
                        .Where(a =>
                            !a.IsDeleted &&
                            a.Status != "cancelled" &&
                            a.Status != "completed" &&
                            a.AppointmentDate >= tomorrowUtc &&
                            a.AppointmentDate < tomorrowEndUtc)
                        .ToListAsync();

                _logger.LogInformation(
                    "📢 Day-before reminders: {Count} appointments",
                    appointments.Count);

                foreach (var appointment in appointments)
                {
                    if (appointment.Patient == null ||
                        string.IsNullOrWhiteSpace(
                            appointment.Patient.Phone))
                    {
                        continue;
                    }

                    var alreadySent =
                        await _db.NotificationLogs.AnyAsync(n =>
                            n.AppointmentId == appointment.Id &&
                            n.Type == "day_before" &&
                            n.IsSuccess);

                    if (alreadySent)
                        continue;

                    var localTime =
                        ToJordanTime(
                            appointment.AppointmentDate);

                    var phone =
                        NormalizePhone(
                            appointment.Patient.Phone);

                    var msg = $"""
                        🔔 *تذكير بموعدك غداً*

                        {appointment.Patient.FullName}،

                        لديك موعد غداً في
                        {appointment.Clinic?.Name ?? "العيادة"} 📅

                        📅 التاريخ: {localTime:dd/MM/yyyy}
                        🕐 الوقت: {localTime:hh:mm tt}
                        👨‍⚕️ الطبيب: {appointment.Doctor?.FullName ?? "—"}

                        يرجى الحضور قبل 10 دقائق ⏰
                        """;

                    var success = await SendWhatsApp(
                        phone,
                        msg,
                        appointment.ClinicId);
                    if (success)
                    {
                        await LogNotification(
                        appointment.Id,
                        appointment.ClinicId,
                        appointment.Patient.Id,
                        "day_before",
                        phone,
                        msg);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Error in SendDayBeforeReminders");
            }
        }

        // ══════════════════════════════════════════════════════
        // 6️⃣ تذكير قبل ساعة
        // ══════════════════════════════════════════════════════

        public async Task SendHourBeforeReminders()
        {
            try
            {
                var jordanNow =
                    ToJordanTime(DateTime.UtcNow);

                // نبحث عن المواعيد بين 55 و65 دقيقة من الآن
                var fromLocal =
                    jordanNow.AddMinutes(55);

                var toLocal =
                    jordanNow.AddMinutes(65);

                var fromUtc =
                    TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(
                            fromLocal,
                            DateTimeKind.Unspecified),
                        JordanTimeZone());

                var toUtc =
                    TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(
                            toLocal,
                            DateTimeKind.Unspecified),
                        JordanTimeZone());

                var appointments =
                    await _db.Appointments
                        .Include(a => a.Patient)
                        .Include(a => a.Doctor)
                        .Include(a => a.Clinic)
                        .Where(a =>
                            !a.IsDeleted &&
                            a.Status != "cancelled" &&
                            a.Status != "completed" &&
                            a.AppointmentDate >= fromUtc &&
                            a.AppointmentDate <= toUtc)
                        .ToListAsync();

                _logger.LogInformation(
                    "📢 Hour-before reminders: {Count} appointments",
                    appointments.Count);

                foreach (var appointment in appointments)
                {
                    if (appointment.Patient == null ||
                        string.IsNullOrWhiteSpace(
                            appointment.Patient.Phone))
                    {
                        continue;
                    }

                    var alreadySent =
                        await _db.NotificationLogs.AnyAsync(n =>
                            n.AppointmentId == appointment.Id &&
                            n.Type == "hour_before" &&
                            n.IsSuccess);

                    if (alreadySent)
                        continue;

                    var localTime =
                        ToJordanTime(
                            appointment.AppointmentDate);

                    var phone =
                        NormalizePhone(
                            appointment.Patient.Phone);

                    var msg = $"""
                        ⏰ *موعدك بعد ساعة!*

                        {appointment.Patient.FullName}،

                        موعدك في
                        {appointment.Clinic?.Name ?? "العيادة"}
                        بعد ساعة تقريباً 🏥

                        📅 التاريخ: {localTime:dd/MM/yyyy}
                        🕐 الوقت: {localTime:hh:mm tt}
                        👨‍⚕️ الطبيب: {appointment.Doctor?.FullName ?? "—"}

                        في انتظارك 🌟
                        """;

                    var success = await SendWhatsApp(
                        phone,
                        msg,
                        appointment.ClinicId);
                    if (success)
                    {
                        await LogNotification(
                        appointment.Id,
                        appointment.ClinicId,
                        appointment.Patient.Id,
                        "hour_before",
                        phone,
                        msg
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Error in SendHourBeforeReminders");
            }
        }

        // ══════════════════════════════════════════════════════
        // تسجيل الإشعار
        // ══════════════════════════════════════════════════════

        private async Task LogNotification(
            Guid appointmentId,
            Guid clinicId,
            Guid patientId,
            string type,
            string phone,
            string message
            )
        {
            try
            {
                _db.NotificationLogs.Add(
                    new NotificationLog
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
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Error logging notification for Appointment {AppointmentId}",
                    appointmentId);
            }
        }

        // ══════════════════════════════════════════════════════
        // فحص حد الرسائل اليومي حسب خطة اشتراك العيادة
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// يرجع true لو العيادة وصلت حد الرسائل اليومي المسموح بخطتها.
        /// -1 = غير محدود. اليوم يُحسب بتوقيت الأردن.
        /// </summary>
        private async Task<bool> IsDailyLimitReached(Guid clinicId)
        {
            var limit = await _db.Subscriptions
                .Where(s => s.ClinicId == clinicId && s.IsActive)
                .Select(s => (int?)s.Plan.MaxDailyMessages)
                .FirstOrDefaultAsync();

            // لا يوجد اشتراك فعّال، أو الخطة غير محدودة
            if (limit == null || limit.Value == -1)
                return false;

            // حدود اليوم بتوقيت الأردن → UTC
            var jordanToday = ToJordanTime(DateTime.UtcNow).Date;

            var startUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(jordanToday, DateTimeKind.Unspecified),
                JordanTimeZone());

            var endUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(jordanToday.AddDays(1), DateTimeKind.Unspecified),
                JordanTimeZone());

            var sentToday = await _db.NotificationLogs
                .CountAsync(n =>
                    n.ClinicId == clinicId &&
                    n.IsSuccess &&
                    n.SentAt >= startUtc &&
                    n.SentAt < endUtc);

            if (sentToday >= limit.Value)
            {
                _logger.LogWarning(
                    "🚫 Daily message limit reached for clinic {ClinicId}. Sent: {Sent}/{Limit}",
                    clinicId, sentToday, limit.Value);

                return true;
            }

            return false;
        }

        // ══════════════════════════════════════════════════════
        // تحليل التذكيرات المخصصة
        // ══════════════════════════════════════════════════════

        private static List<int> ParseCustomReminderHours(
            string? customReminders)
        {
            if (string.IsNullOrWhiteSpace(customReminders))
                return new List<int>();

            return customReminders
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s =>
                    int.TryParse(
                        s.Trim(),
                        out var hours)
                        ? hours
                        : 0)
                .Where(hours => hours > 0)
                .Distinct()
                .OrderBy(hours => hours)
                .ToList();
        }

        // ══════════════════════════════════════════════════════
        // تحويل UTC → توقيت الأردن
        // ══════════════════════════════════════════════════════

        private static DateTime ToJordanTime(DateTime utc)
        {
            try
            {
                var timeZone = JordanTimeZone();

                var utcTime =
                    utc.Kind == DateTimeKind.Utc
                        ? utc
                        : utc.ToUniversalTime();

                return TimeZoneInfo.ConvertTimeFromUtc(
                    utcTime,
                    timeZone);
            }
            catch
            {
                // fallback
                return utc.AddHours(3);
            }
        }

        // ══════════════════════════════════════════════════════
        // TimeZone الأردن
        // ══════════════════════════════════════════════════════

        private static TimeZoneInfo JordanTimeZone()
        {
            try
            {
                // Linux / Docker / Linux hosting
                return TimeZoneInfo.FindSystemTimeZoneById(
                    "Asia/Amman");
            }
            catch
            {
                try
                {
                    // Windows
                    return TimeZoneInfo.FindSystemTimeZoneById(
                        "Jordan Standard Time");
                }
                catch
                {
                    return TimeZoneInfo.Utc;
                }
            }
        }

        // ══════════════════════════════════════════════════════
        // تنسيق رقم الهاتف الأردني
        // ══════════════════════════════════════════════════════

        private static string NormalizePhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return string.Empty;

            phone = phone
                .Trim()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("(", "")
                .Replace(")", "");

            // 0791234567
            if (phone.StartsWith("07") &&
                phone.Length == 10)
            {
                return "+962" + phone[1..];
            }

            // 791234567
            if (phone.StartsWith("7") &&
                phone.Length == 9)
            {
                return "+962" + phone;
            }

            // 962791234567
            if (phone.StartsWith("962") &&
                phone.Length == 12)
            {
                return "+" + phone;
            }

            // +962791234567
            if (phone.StartsWith("+962"))
            {
                return phone;
            }

            // إذا كان الرقم يبدأ بـ +
            if (phone.StartsWith("+"))
            {
                return phone;
            }

            return "+" + phone;
        }



        // ══════════════════════════════════════════════════════
        // فحص استجابة خدمة الواتساب — { "ok": true } عند النجاح
        // ══════════════════════════════════════════════════════

        private static bool IsWhatsAppServiceResponseSuccessful(
            string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return false;

            try
            {
                using var document =
                    JsonDocument.Parse(responseBody);

                var root = document.RootElement;

                if (root.TryGetProperty("ok", out var okProperty) &&
                    okProperty.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                // إذا لم تكن JSON،
                // نعتمد على HTTP StatusCode
                return true;
            }
        }
    }

}