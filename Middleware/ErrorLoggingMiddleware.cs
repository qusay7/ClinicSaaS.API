using System.Security.Claims;
using ClinicSaaS.API.Data;

namespace ClinicSaaS.API.Middleware
{
    // ✅ يلتقط أي استثناء غير معالج بالتطبيق كامل ويسجّله بجدول ErrorLogs —
    // بدون هذا، الاكتشاف الوحيد كان يدوي عبر stderr.log أو استعلام SQL مباشر
    public class ErrorLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ErrorLoggingMiddleware> _logger;

        public ErrorLoggingMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory, ILogger<ErrorLoggingMiddleware> logger)
        {
            _next = next;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception at {Method} {Path}", context.Request.Method, context.Request.Path);

                try
                {
                    // ✅ scope جديد مستقل — الـ DbContext الأصلي بالطلب ممكن يكون
                    // بحالة معطوبة لو الاستثناء نفسه جاي من EF Core
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    Guid? userId = Guid.TryParse(context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null;
                    Guid? clinicId = Guid.TryParse(context.User?.FindFirst("ClinicId")?.Value, out var cid) ? cid : null;

                    db.ErrorLogs.Add(new ErrorLog
                    {
                        Id = Guid.NewGuid(),
                        Message = ex.Message,
                        StackTrace = ex.ToString(),
                        RequestPath = context.Request.Path,
                        RequestMethod = context.Request.Method,
                        UserId = userId,
                        ClinicId = clinicId,
                        CreatedAt = DateTime.UtcNow,
                    });
                    await db.SaveChangesAsync();
                }
                catch (Exception logEx)
                {
                    _logger.LogError(logEx, "Failed to persist error log");
                }

                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"message\":\"حدث خطأ غير متوقع بالسيرفر\"}");
                }
            }
        }
    }
}
