using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicSaaS.API.Controllers
{
    // ✅ نسخ احتياطي للنظام ككل (قاعدة بيانات المنصة بكاملها، لا عيادة واحدة) —
    // SuperAdmin فقط، نفس مستوى حساسية إدارة الخطط والعيادات
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "SuperAdmin")]
    public class BackupController : ControllerBase
    {
        private readonly IBackupService _backupService;
        private readonly IConfiguration _config;

        public BackupController(IBackupService backupService, IConfiguration config)
        {
            _backupService = backupService;
            _config = config;
        }

        // GET: api/backup/status
        [HttpGet("status")]
        public ActionResult GetStatus()
        {
            return Ok(new
            {
                enabled = _config.GetValue<bool>("BackupSettings:Enabled"),
                containerName = _config["BackupSettings:ContainerName"] ?? "clinicsaas-backups",
                runAtHourUtc = _config.GetValue<int?>("BackupSettings:RunAtHour") ?? 3,
                retentionDays = _config.GetValue<int?>("BackupSettings:RetentionDays") ?? 30,
                azureConfigured = !string.IsNullOrWhiteSpace(_config["BackupSettings:AzureConnectionString"]),
            });
        }

        // POST: api/backup/run
        // ✅ نسخة فورية يدوية — تعمل بغض النظر عن BackupSettings:Enabled (مفيدة
        // للاختبار قبل التفعيل الرسمي، أو لأخذ نسخة قبل تغيير كبير)
        [HttpPost("run")]
        public async Task<ActionResult> RunNow()
        {
            try
            {
                var fileName = await _backupService.RunBackupAsync();
                return Ok(new { message = "تم إنشاء النسخة الاحتياطية ورفعها بنجاح", fileName });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "فشل إنشاء النسخة الاحتياطية", error = ex.Message });
            }
        }
    }
}
