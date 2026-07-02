namespace ClinicSaaS.API.Services
{
    // يعمل في الخلفية — يُسجَّل في Program.cs كـ HostedService
    public class ReminderBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<ReminderBackgroundService> _logger;

        public ReminderBackgroundService(IServiceProvider services, ILogger<ReminderBackgroundService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Reminder Background Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var jordanNow = TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.UtcNow,
                        TimeZoneInfo.FindSystemTimeZoneById("Asia/Amman"));

                    using var scope = _services.CreateScope();
                    var notifService = scope.ServiceProvider.GetRequiredService<INotificationService>();

                    // ✅ تذكير قبل يوم — يعمل كل صباح بين 9:00 و 9:05
                    if (jordanNow.Hour == 9 && jordanNow.Minute < 5)
                    {
                        _logger.LogInformation("Running day-before reminders at {Time}", jordanNow);
                        await notifService.SendDayBeforeReminders();
                    }

                    // ✅ تذكير قبل ساعة — يعمل كل ساعة في الدقيقة 0-5
                    if (jordanNow.Minute < 5)
                    {
                        _logger.LogInformation("Running hour-before reminders at {Time}", jordanNow);
                        await notifService.SendHourBeforeReminders();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ReminderBackgroundService");
                }

                // انتظر 5 دقائق ثم تحقق مجدداً
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
