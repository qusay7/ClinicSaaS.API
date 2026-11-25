using System.Security.Cryptography;

namespace ClinicSaaS.API.Services
{
    // ✅ كلمة مرور عشوائية حقيقية (غير قابلة للتنبؤ من اسم العيادة/النطاق الفرعي)
    // تُستخدم لأي حساب يُولَّد تلقائياً (تسجيل ذاتي، أو حساب ينشئه SuperAdmin)،
    // بدل نمط {subdomain}@123 القديم القابل للاستنتاج من رابط العيادة العام
    public static class PasswordGenerator
    {
        // ✅ بدون أحرف/أرقام متشابهة بصرياً (0/O، 1/l/I) لتسهيل كتابتها يدوياً
        private const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

        public static string Generate(int length = 12)
        {
            var result = new char[length];
            for (var i = 0; i < length; i++)
                result[i] = Chars[RandomNumberGenerator.GetInt32(Chars.Length)];
            return new string(result);
        }
    }
}
