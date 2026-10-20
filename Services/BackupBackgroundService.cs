namespace ClinicSaaS.API.Services
{
    // ✅ يعمل بالخلفية دائماً (نفس نمط ReminderBackgroundService)، لكنه
    // no-op فعلياً ما لم يُفعَّل BackupSettings:Enabled صراحة بالإعدادات —
    // جاهز بالكود من الآن، ويُفعَّل بتعديل إعداد واحد فقط (بدون نشر كود جديد)
    // عند بيع أول عميل حقيقي.
    public class BackupBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<BackupBackgroundService> _logger;
        private DateTime _lastRunDateUtc = DateTime.MinValue;

        public BackupBackgroundService(IServiceProvider services, ILogger<BackupBackgroundService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Backup Background Service started (checks every 5 min; no-op unless BackupSettings:Enabled=true)");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                    if (config.GetValue<bool>("BackupSettings:Enabled"))
                    {
                        var runAtHour = config.GetValue<int?>("BackupSettings:RunAtHour") ?? 3;
                        var utcNow = DateTime.UtcNow;

                        // ✅ نافذة 5 دقائق + علم "آخر يوم نفّذ فيه" يمنع التكرار المزدوج
                        // بنفس اليوم (الحلقة تتحقق كل 5 دقائق، فبدون هذا الحارس كانت
                        // ستنفّذ نسخة لكل تكرار يقع داخل نافذة الـ5 دقائق)
                        if (utcNow.Hour == runAtHour && utcNow.Minute < 5 && utcNow.Date != _lastRunDateUtc)
                        {
                            _logger.LogInformation("🔄 Running scheduled backup at {Time} UTC", utcNow);
                            var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
                            await backupService.RunBackupAsync();
                            _lastRunDateUtc = utcNow.Date;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error in BackupBackgroundService");
                }

                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
