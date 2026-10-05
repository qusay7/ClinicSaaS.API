namespace ClinicSaaS.API.DTOs.Auth
{
    // هذا الكلاس يحدد الحقول التي سيردها النظام بعد محاولة تسجيل الدخول
    public class AuthResponseDto
    {
        public string Token { get; set; }      // البطاقة التي ستستخدمها في كل طلب
        public string RefreshToken { get; set; } = default!;// بطاقة تجديد الصلاحية
        public string FullName { get; set; }   // اسمك
        public string Email { get; set; }      // بريدك
        public string Role { get; set; }       // دورك (Admin / Doctor / ...)
        public Guid? ClinicId { get; set; }    // أي عيادة تنتمي إليها
        public string? ClinicName { get; set; }// اسم العيادة
        public string? TimeFormat { get; set; }// "12" أو "24" — تفضيل عرض الوقت بالعيادة
        public string? Country { get; set; }   // رمز بلد العيادة، مثل "JO"
        public string? Currency { get; set; }  // رمز عملة العيادة، مثل "JOD"
        public DateTime ExpiresAt { get; set; }// متى تنتهي صلاحية البطاقة
        public DateTime RefreshTokenExpiresAt { get; set; } // متى تنتهي صلاحية بطاقة التجديد
        public List<string> Permissions { get; set; } = new(); // قائمة الصلاحيات التي يمتلكها المستخدم

        // ✅ طبيب مرتبط بقسم طوارئ — الفرونت يستخدمها لتوجيهه للوحة الطوارئ بعد
        // تسجيل الدخول بدل جدول اليوم العادي (أطباء الطوارئ ما لهم جدول مواعيد ثابت)
        public bool IsEmergencyDoctor { get; set; }
    }
}
