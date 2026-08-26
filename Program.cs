using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
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
builder.Services.AddHttpClient<INotificationService, NotificationService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddHostedService<ReminderBackgroundService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IJoFotaraService, JoFotaraService>();
builder.Services.AddScoped<IInvoiceXmlBuilder, InvoiceXmlBuilder>();
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

app.UseHttpsRedirection();
app.UseRouting();

// ✅ ترتيب الـ Middleware مهم جداً!
app.UseCors("AllowReact");
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

logger.LogInformation("🚀 تطبيق ClinicSaaS بدأ بنجاح على المنفذ {Port}",
    app.Urls.FirstOrDefault() ?? "Unknown");

app.Run();