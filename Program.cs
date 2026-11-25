using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ══════════════════════════════════════
// 🔐 JWT Authentication
// ══════════════════════════════════════
builder.Services.AddScoped<JwtService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
            ValidAudience = builder.Configuration["JwtSettings:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["JwtSettings:SecretKey"]!)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// ══════════════════════════════════════
// 📚 Swagger/OpenAPI
// ══════════════════════════════════════
builder.Services.AddControllers();

// ✅ لو فشل تحويل الـ JSON قبل وصوله حتى لفحص النموذج (مثلاً رقم أكبر من
// حجم int مكتوب بحقل رقمي)، الإطار الافتراضي كان يُرجع رسالة تقنية غامضة
// (JsonException مدموجة مع "The model field is required."). نستبدلها هنا
// برسالة واضحة بس لهذي الحالة تحديداً، وباقي أخطاء التحقق تبقى بسلوكها الافتراضي
// ✅ PostConfigure لا Configure — الإطار نفسه يضبط قيمته الافتراضية لـ
// InvalidModelStateResponseFactory عبر PostConfigure، وهذا دائماً يُنفَّذ بعد
// كل استدعاءات Configure بغض النظر عن ترتيب التسجيل، فيلغي أي تعديل عليها
// هنا لو استُخدم Configure العادي
builder.Services.PostConfigure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        // ✅ أخطاء فشل قراءة/تحويل الـ JSON نفسه تُسجَّل بمفتاح على شكل مسار JSON
        // ("$.patientNumber")، بعكس أخطاء التحقق العادية (مثل [Required]) اللي
        // تُسجَّل باسم الخاصية مباشرة ("FullName") — هذا أدق من فحص Exception
        // (اللي لا يُحفَظ دائماً بـ ModelState لهذا النوع من الأخطاء)
        var hasJsonError = context.ModelState.Keys.Any(k => k.StartsWith("$."));

        if (hasJsonError)
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult("أحد الحقول يحتوي على قيمة غير صالحة أو كبيرة جداً، يرجى التحقق منها");

        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new Microsoft.AspNetCore.Mvc.ValidationProblemDetails(context.ModelState));
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "أدخل التوكن: Bearer {token}"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

// ══════════════════════════════════════
// 🗄️ Database
// ══════════════════════════════════════
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.CommandTimeout(120)
    )
);

// ══════════════════════════════════════
// 🔔 Notifications & Background Services
// ══════════════════════════════════════
// ✅ بدون مهلة صريحة هون، HttpClient يعتمد الافتراضي 100 ثانية — لو خدمة
// الواتساب (Baileys) علّقت أو صارت بطيئة، أي طلب حجز/تعديل/إلغاء موعد يبقى
// ماسك اتصال قاعدة بياناته طول هالمدة بانتظار رد واتساب، لحد ما يمتلئ pool
// الاتصالات بالكامل ويعلّق الموقع كله (صار فعلياً — Cloudflare رجّع 524
// بعد بالضبط 100 ثانية). 15 ثانية كافية جداً لخدمة محلية على نفس السيرفر
builder.Services.AddHttpClient<INotificationService, NotificationService>()
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddHttpClient<IWhatsAppConnectionService, WhatsAppConnectionService>()
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<IWhatsAppConnectionService, WhatsAppConnectionService>();
builder.Services.AddHostedService<ReminderBackgroundService>();
// ✅ Singleton — بدون أي تبعية مرتبطة بنطاق الطلب (Scoped)، فلا داعي لربطها
// بعمر الطلب نفسه، وهذا مهم خصوصاً أنها تُستخدم بإرسال "fire-and-forget"
// (بدون await) لا يُفترض أن يرتبط بانتهاء الطلب الذي بدأه
builder.Services.AddSingleton<IEmailService, EmailService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IJoFotaraService, JoFotaraService>();
builder.Services.AddScoped<IInvoiceXmlBuilder, InvoiceXmlBuilder>();
// ✅ نسخ احتياطي تلقائي لقاعدة البيانات كاملة إلى Azure Blob Storage —
// معطَّل افتراضياً عبر BackupSettings:Enabled حتى يُفعَّل عند أول عميل حقيقي
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddHostedService<BackupBackgroundService>();
// ══════════════════════════════════════
// 🏢 Clinic Context & Services
// ══════════════════════════════════════
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IClinicContext, ClinicContext>();
builder.Services.AddScoped<SubscriptionService>();

// ══════════════════════════════════════
// 🌱 Seeding Services
// ══════════════════════════════════════
builder.Services.AddScoped<IRoleSeedingService, RoleSeedingService>();
builder.Services.AddScoped<IDepartmentSeedingService, DepartmentSeedingService>();
builder.Services.AddScoped<ITreatmentTemplateSeedingService, TreatmentTemplateSeedingService>();

// ══════════════════════════════════════
// 📊 Export Services
// ══════════════════════════════════════
builder.Services.AddScoped<IPdfExportService, PdfExportService>();
builder.Services.AddScoped<IExcelExportService, ExcelExportService>();

// ══════════════════════════════════════
// 🌐 CORS
// ══════════════════════════════════════
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
    {
        policy.WithOrigins(
            "http://localhost:5173",
            "http://localhost:4173",
            "http://localhost:4174",
            "https://localhost:5173",
            "http://192.168.1.52:4173",
            "http://192.168.1.52:4174",
            "http://192.168.1.52:5173"
        )
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});

// ══════════════════════════════════════
// 🌐 Forwarded Headers — السيرفر خلف نفق Cloudflare (cloudflared) على نفس
// الجهاز، فـHttpContext.Connection.RemoteIpAddress بدون هذا دائماً 127.0.0.1
// (عنوان النفق المحلي)، مما يكسر أي تحديد معدل طلبات أو تسجيل IP حقيقي للجلسات
// (AuthController.Login). نلغي قيد KnownProxies/KnownNetworks لأن القفزة
// الوحيدة قبل Kestrel هي عملية cloudflared المحلية نفسها (موثوقة أصلاً)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ══════════════════════════════════════
// 🚦 Rate Limiting — حماية نقاط عامة بدون مصادقة (مثل التسجيل الذاتي) من
// إساءة استخدام آلية بسيطة: حد عدد الطلبات من نفس IP، مش حماية ضد مستخدم
// شرعي عادي
// ══════════════════════════════════════
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("signup", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

// ══════════════════════════════════════
// 🌱 Data Seeding
// ══════════════════════════════════════
// ✅ تعريف logger خارج الـ using scope
var logger = app.Services.GetRequiredService<ILogger<Program>>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var roleSeedingService = scope.ServiceProvider.GetRequiredService<IRoleSeedingService>();

    try
    {
        logger.LogInformation("🌱 بدء تعبئة البيانات...");

        // 1️⃣ تحقق من الاتصال بقاعدة البيانات
        var canConnect = await db.Database.CanConnectAsync();
        if (!canConnect)
        {
            logger.LogError("❌ لا يمكن الاتصال بقاعدة البيانات!");
            throw new Exception("Database connection failed");
        }
        logger.LogInformation("✅ الاتصال بقاعدة البيانات نجح");

        // 2️⃣ تحقق من الصلاحيات
        var permCount = await db.Permissions.CountAsync();
        if (permCount == 0)
        {
            logger.LogWarning("⚠️ لم يتم العثور على صلاحيات. تأكد من تعبئة الصلاحيات أولاً.");
        }
        else
        {
            logger.LogInformation($"✅ عدد الصلاحيات: {permCount}");
        }

        // 3️⃣ Seed أدوار الإدارة
        logger.LogInformation("🌱 تعبئة أدوار الإدارة...");
        try
        {
            var adminAdded = await roleSeedingService.SeedAdminRoles();
            logger.LogInformation($"✅ تم تعبئة {adminAdded} أدوار إدارة");
        }
        catch (Exception ex)
        {
            logger.LogError($"⚠️ خطأ في تعبئة أدوار الإدارة: {ex.Message}");
        }

        // 4️⃣ Seed أدوار العيادات
        logger.LogInformation("🌱 تعبئة أدوار العيادات...");
        var clinics = await db.Clinics.AsNoTracking().ToListAsync();

        if (clinics.Count == 0)
        {
            logger.LogWarning("⚠️ لم يتم العثور على أي عيادات.");
        }
        else
        {
            int totalRolesAdded = 0;
            foreach (var clinic in clinics)
            {
                try
                {
                    var added = await roleSeedingService.SeedDefaultRoles(clinic.Id);
                    totalRolesAdded += added;
                    logger.LogInformation($"✅ تم تعبئة {added} أدوار للعيادة: {clinic.Name}");
                }
                catch (Exception ex)
                {
                    logger.LogError($"⚠️ خطأ في تعبئة أدوار العيادة {clinic.Name}: {ex.Message}");
                }
            }
            logger.LogInformation($"✅ إجمالي الأدوار المضافة: {totalRolesAdded}");
        }

        // 5️⃣ خطة التجربة المجانية — يحتاجها api/public/signup (زر "ابدأ مجاناً")
        var freePlanExists = await db.Plans.AnyAsync(p => p.Name == ClinicSaaS.API.Controllers.PublicSignupController.FreeTrialPlanName);
        if (!freePlanExists)
        {
            db.Plans.Add(new Plan
            {
                Id = Guid.NewGuid(),
                Name = ClinicSaaS.API.Controllers.PublicSignupController.FreeTrialPlanName,
                NameEn = "Free Trial",
                Description = "تجربة مجانية لمدة 14 يوم",
                DescriptionEn = "A 14-day free trial",
                MonthlyPrice = 0,
                YearlyPrice = 0,
                MaxUsers = 3,
                MaxDoctors = 1,
                MaxPatients = 50,
                MaxDailyMessages = 20,
                IsActive = true,
                ShowOnPricingPage = false,
                HasElectronicInvoicing = false,
                HasMultipleDepartments = false,
            });
            await db.SaveChangesAsync();
            logger.LogInformation("✅ تم إنشاء خطة التجربة المجانية");
        }

        logger.LogInformation("✅ اكتملت تعبئة البيانات بنجاح!");
    }
    catch (Exception ex)
    {
        logger.LogError($"❌ خطأ أثناء تعبئة البيانات: {ex.Message}");
        logger.LogError($"Stack Trace: {ex.StackTrace}");
        // لا نرمي الاستثناء — اترك التطبيق يشتغل حتى لو الـ seeding فشل
    }
}

// ══════════════════════════════════════
// 🛠️ Middleware
// ══════════════════════════════════════

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ClinicSaaS API v1");
        c.RoutePrefix = string.Empty;
    });
}

// ✅ أول middleware بالترتيب — يلف كل شي تحته عشان يلتقط أي استثناء مهما كان مصدره
app.UseMiddleware<ClinicSaaS.API.Middleware.ErrorLoggingMiddleware>();

// ✅ لازم قبل أي شيء يقرأ IP/scheme (قبل HttpsRedirection والـRate Limiter)
app.UseForwardedHeaders();

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();

// ✅ ترتيب الـ Middleware مهم جداً!
app.UseCors("AllowReact");
app.UseStaticFiles();
// ✅ الفرونت اند والباك اند عندهم دومين/منفذ منفصل بالإنتاج، وريفيرس بروكسي
// السيرفر بيمرّر بس /api للباك اند — شعارات العيادة مخزّنة كـ /logos/xxx.png
// (جذر الباك اند نفسه) فما توصل أبداً من دومين الفرونت اند. نعرض نفس مجلد
// wwwroot/logos إضافياً تحت /api/logos عشان تنعبر من نفس مسار البروكسي الشغّال أصلاً
app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"), "logos")),
    RequestPath = "/api/logos",
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

logger.LogInformation("🚀 تطبيق ClinicSaaS بدأ بنجاح على المنفذ {Port}",
    app.Urls.FirstOrDefault() ?? "Unknown");

app.Run();