namespace ClinicSaaS.API.DTOs.Auth
{
    // ما يُرسله المستخدم عند محاولة تسجيل الدخول
    public class LoginDto
    {
        public string Email { get; set; } // البريد الإلكتروني للمستخدم
        public string Password { get; set; } // كلمة المرور للمستخدم
    }
}
