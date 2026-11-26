using ClinicSaaS.API.DTOs.Auth;

namespace ClinicSaaS.API.DTOs.Public
{
    // ✅ نفس شكل AuthResponseDto بالضبط (عشان الفرونت يقدر يعامل نتيجة التسجيل
    // الذاتي كأنها نتيجة تسجيل دخول عادية ويخزّن التوكنات بنفس الطريقة) + حقول
    // إضافية خاصة بالتسجيل الذاتي: بيانات الدخول المُولَّدة ونهاية فترة التجربة
    public class PublicSignupResponseDto : AuthResponseDto
    {
        public string Subdomain { get; set; } = default!;
        public string Username { get; set; } = default!;
        public string Password { get; set; } = default!;
        public DateTime TrialEndsAt { get; set; }

        // ✅ طبيب وموظف استقبال يُنشآن تلقائياً مع كل عيادة تجريبية (جاهزون للعمل
        // فوراً بجدول دوام 24 ساعة)، فنُرجع بيانات دخولهم هنا ليعرضها الفرونت
        // بنفس شاشة بيانات مدير العيادة
        public List<StaffCredentialDto> ExtraAccounts { get; set; } = new();
    }

    public class StaffCredentialDto
    {
        public string Role { get; set; } = default!;
        public string Username { get; set; } = default!;
        public string Password { get; set; } = default!;
    }
}
