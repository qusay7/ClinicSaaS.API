namespace ClinicSaaS.API.DTOs.Auth
{
    // ما يُرسله المستخدم عند محاولة تسجيل الدخول
    public class LoginDto
    {
        public string? EmailOrUsername { get; set; }  // ✅ بدل Email
        public string Password { get; set; } = default!;
    }
}
