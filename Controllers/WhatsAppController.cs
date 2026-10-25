using ClinicSaaS.API.Data;
using ClinicSaaS.API.Filters;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly IWhatsAppConnectionService _whatsAppConnection;
        private readonly INotificationService _notificationService;

        public WhatsAppController(ApplicationDbContext db, IClinicContext clinicContext, IWhatsAppConnectionService whatsAppConnection, INotificationService notificationService)
        {
            _db = db;
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

            // ✅ نخزّن آخر حالة معروفة محلياً — SendWhatsApp يعتمد عليها ليتجنب
            // محاولة الاتصال بالخدمة إطلاقاً لو العيادة غير متصلة
            var isConnected = status == "connected";
            await _db.Clinics
                .Where(c => c.Id == _clinicContext.ClinicId.Value && c.WhatsAppConnected != isConnected)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.WhatsAppConnected, isConnected));

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
