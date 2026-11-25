using System.Net;
using System.Net.Mail;

namespace ClinicSaaS.API.Services
{
    public interface IEmailService
    {
        Task SendAsync(string toEmail, string subject, string htmlBody);
    }

    // ✅ إرسال SMTP بسيط — لا توجد مزوّد بريد (SendGrid/Mailgun...) بالمشروع
    // حالياً، فاستُخدمت SmtpClient المدمجة بـ.NET بدل إضافة تبعية جديدة.
    // بيانات الاتصال الحقيقية (Host/Username/Password) تُضاف فقط على سيرفر
    // الإنتاج (appsettings.Production.json أو متغيرات بيئة) — هنا دائماً فاضية
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendAsync(string toEmail, string subject, string htmlBody)
        {
            var host = _configuration["EmailSettings:Host"];
            if (string.IsNullOrWhiteSpace(host))
            {
                _logger.LogWarning("EmailSettings:Host غير مُهيّأ — تم تجاوز إرسال بريد إلى {Email}", toEmail);
                return;
            }

            var port = _configuration.GetValue("EmailSettings:Port", 587);
            var username = _configuration["EmailSettings:Username"];
            var password = _configuration["EmailSettings:Password"];
            var fromAddress = _configuration["EmailSettings:FromAddress"];
            var fromName = _configuration["EmailSettings:FromName"] ?? "Cura";
            var enableSsl = _configuration.GetValue("EmailSettings:EnableSsl", true);

            if (string.IsNullOrWhiteSpace(fromAddress))
                fromAddress = username;

            using var message = new MailMessage
            {
                From = new MailAddress(fromAddress!, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true,
            };
            message.To.Add(toEmail);

            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(username, password),
                EnableSsl = enableSsl,
            };

            await client.SendMailAsync(message);
        }
    }
}
