using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ClinicSaaS.API.Services
{
    public interface IEmailService
    {
        Task SendAsync(string toEmail, string subject, string htmlBody);
    }

    // ✅ MailKit بدل System.Net.Mail.SmtpClient المدمجة — مايكروسوفت نفسها توصي
    // بعدم استخدامها بمشاريع جديدة، وفعلياً فشلت مع Hostinger (STARTTLS ثم AUTH
    // لم يُرسَل بترتيب صحيح، فرجع السيرفر "Authentication required"). بيانات
    // الاتصال الحقيقية (Host/Username/Password) تُضاف فقط على سيرفر الإنتاج
    // (appsettings.Development.json أو متغيرات بيئة) — هنا دائماً فاضية
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
                _logger.LogWarning("EMAIL_SEND_SKIPPED EmailSettings:Host not configured, to={Email}", toEmail);
                return;
            }

            var port = _configuration.GetValue("EmailSettings:Port", 587);
            var username = _configuration["EmailSettings:Username"];
            var password = _configuration["EmailSettings:Password"];
            var fromAddress = _configuration["EmailSettings:FromAddress"];
            var fromName = _configuration["EmailSettings:FromName"] ?? "Cura";

            if (string.IsNullOrWhiteSpace(fromAddress))
                fromAddress = username;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromAddress));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient();
            // ✅ Auto يختار بنفسه STARTTLS (587) أو SSL مباشر (465) حسب المنفذ،
            // ويضمن اكتمال التشفير قبل إرسال AUTH — هذا بالضبط ما كانت
            // SmtpClient المدمجة تفشل فيه
            await client.ConnectAsync(host, port, SecureSocketOptions.Auto);
            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}
