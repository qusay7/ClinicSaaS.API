/*البرنامج هنا يعمل على 3 مستويات: تسجيل الخدمات، ضبط JWT Authentication، ثم ترتيب الـ middleware قبل تشغيل التطبيق.
ترتيب UseAuthentication() قبل UseAuthorization() مهم لأن التحقق من الهوية يجب أن يحدث قبل تحديد الصلاحيات.
*/
using ClinicSaaS.API.Data;// استيراد مساحة الأسماء التي تحتوي على ApplicationDbContext، وهو كلاس يمثل قاعدة البيانات ويستخدم للوصول إلى الجداول والبيانات.
using ClinicSaaS.API.Services;// استيراد مساحة الأسماء التي تحتوي على JwtService، وهو كلاس مسؤول عن إنشاء رموز JWT لتوثيق المستخدمين.
using Microsoft.EntityFrameworkCore;// استيراد مساحة الأسماء التي تحتوي على أدوات Entity Framework Core، وهي مكتبة تستخدم للتعامل مع قواعد البيانات بطريقة كائنية (ORM).
using Microsoft.AspNetCore.Authentication.JwtBearer; // استيراد مساحة الأسماء التي تحتوي على أدوات لتكوين JWT Bearer Authentication، وهي طريقة لتوثيق المستخدمين باستخدام رموز JWT في تطبيقات ASP.NET Core.
using Microsoft.IdentityModel.Tokens;// استيراد مساحة الأسماء التي تحتوي على أدوات لتكوين معايير التحقق من صحة التوكن (TokenValidationParameters) وإنشاء مفاتيح التوقيع (SymmetricSecurityKey) وغيرها من الأدوات المتعلقة بالأمان والتوثيق في ASP.NET Core.
using System.Text;// استيراد مساحة الأسماء التي تحتوي على أدوات للتعامل مع النصوص، مثل Encoding.UTF8.GetBytes() الذي يستخدم لتحويل النص إلى بايتات، وهو أمر ضروري عند إنشاء مفاتيح التوقيع للتوكن.


AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// هذا هو ملف Program.cs، نقطة الدخول الرئيسية لتطبيق ASP.NET Core.
// في هذا الملف، نقوم بإعداد الخدمات التي يحتاجها التطبيق وتكوين Middleware الذي سيعالج الطلبات الواردة.
// 1️⃣ ابدأ تجهيز التطبيق
var builder = WebApplication.CreateBuilder(args);// إنشاء كائن Builder الذي يستخدم لإعداد الخدمات والتكوينات للتطبيق

// تسجيل JwtService
// هذا يعني أنه في أي مكان في التطبيق نحتاج إلى JwtService، سيتم إنشاء نسخة جديدة منه تلقائياً
// (Scoped يعني أن نفس النسخة ستستخدم خلال نفس الطلب، ولكن كل طلب جديد سيحصل على نسخة جديدة)
builder.Services.AddScoped<JwtService>();

// إعداد JWT Authentication
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
                Encoding.UTF8.GetBytes(builder.Configuration["JwtSettings:SecretKey"]!))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // إضافة زر Authorize في Swagger
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "اكتب التوكن هكذا: Bearer {token}"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            new string[] {}
        }
    });
});
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// تسجيل IHttpContextAccessor — يسمح بالوصول للـ HTTP Request
builder.Services.AddHttpContextAccessor();

// تسجيل ClinicContext
builder.Services.AddScoped<IClinicContext, ClinicContext>();

builder.Services.AddScoped<SubscriptionService>();// تسجيل SubscriptionService

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
    {
        policy.WithOrigins(
            "http://localhost:5173",
            "https://localhost:5173",
            "http://172.28.53.192:5173",     // ✅ أضف هذا (IP الخاص بك)
            "https://172.28.53.192:5173",    // ✅ أضف هذا (IP مع HTTPS)
            "http://127.0.0.1:5173",          // ✅ أضف هذا (اختياري)
            "https://127.0.0.1:5173"          // ✅ أضف هذا (اختياري)
        )
        .AllowAnyHeader()
        .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowReact");  // ✅ أضف هذا السطر
app.UseAuthentication();  // ✅ أولاً
app.UseAuthorization();   // ✅ ثانياً
app.MapControllers();

app.Run();