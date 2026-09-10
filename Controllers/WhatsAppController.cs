using ClinicSaaS.API.Filters;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicSaaS.API.Controllers
{
    // ✅ ربط واتساب العيادة — يمرّر الطلبات لخدمة الواتساب الداخلية (Node/Baileys)،
    // اللي غير مكشوفة للإنترنت وبدون توثيق خاص بها. هذا الكونترولر هو الوسيط
    // الوحيد المسموح، ويتحقق من صلاحيات وclinicId المستخدم قبل أي استدعاء.
    [ApiController]
    [Route("api/whatsapp")]
    [Authorize]
    [RequireActiveSubscription]
    public class WhatsAppController : ControllerBase
    {
        private readonly IClinicContext _clinicContext;
        private readonly IWhatsAppConnectionService _whatsAppConnection;
        private readonly INotificationService _notificationService;

        public WhatsAppController(IClinicContext clinicContext, IWhatsAppConnectionService whatsAppConnection, INotificationService notificationService)
        {
            _clinicContext = clinicContext;
            _whatsAppConnection = whatsAppConnection;
            _notificationService = notificationService;
        }

        // GET: api/whatsapp/status — الحالة الحالية + QR (لو بمرحلة الربط) + الرقم المتصل وعدد الرسائل المتبقية
        [HttpGet("status")]
        public async Task<ActionResult> GetStatus()
        {
            if (_clinicContext.ClinicId == null)
                return Unauthorized();

            var (status, qrDataUrl, phoneNumber) = await _whatsAppConnection.GetStateAsync(_clinicContext.ClinicId.Value);
            var (used, limit) = await _notificationService.GetMessageQuota(_clinicContext.ClinicId.Value);
            return Ok(new { status, qrDataUrl, phoneNumber, messagesUsedToday = used, messagesLimit = limit });
        }

        // POST: api/whatsapp/connect — يبدأ جلسة ربط جديدة (يولّد QR للمسح)
        [HttpPost("connect")]
        public async Task<ActionResult> Connect()
        {
            if (!_clinicContext.HasPermission("settings.edit"))
                return Forbid();

            if (_clinicContext.ClinicId == null)
                return Unauthorized();

            var status = await _whatsAppConnection.ConnectAsync(_clinicContext.ClinicId.Value);
            return Ok(new { status });
        }
    }
}
