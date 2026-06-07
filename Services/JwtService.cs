using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ClinicSaaS.API.Data;
using Microsoft.IdentityModel.Tokens;

 using System.Security.Cryptography;
 

namespace ClinicSaaS.API.Services
{
    public class JwtService
    {
        // هذا الكلاس مسؤول عن إنشاء رموز JWT لتوثيق المستخدمين
        // نستخدم IConfiguration للوصول إلى إعدادات التطبيق مثل المفتاح السري ومدة صلاحية التوكن
        private readonly IConfiguration _config;

        public JwtService(IConfiguration config)
        {
            _config = config;
        }

        // دالة توليد التوكن
        public string GenerateToken(User user)
        {
            //2️⃣ الـ Claims — معلومات المستخدم داخل التوكن
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),// هذا Claim مهم جداً لأنه يحدد هوية المستخدم في التوكن
                new Claim(ClaimTypes.Email, user.Email),// إضافة البريد الإلكتروني كمعلومة في التوكن
                new Claim(ClaimTypes.Name, user.FullName),// إضافة الاسم الكامل كمعلومة في التوكن
                new Claim(ClaimTypes.Role, user.Role),// إضافة الدور (Role) كمعلومة في التوكن، هذا سيساعد في التحكم في الوصول (Authorization) بناءً على الدور
                new Claim("ClinicId", user.ClinicId?.ToString() ?? "")// إضافة معرف العيادة كمعلومة في التوكن، هذا سيساعد في تحديد العيادة التي ينتمي إليها المستخدم
           };
            //3️⃣ المفتاح السري — التوقيع
            // نستخدم مفتاح سري لتوقيع التوكن، هذا المفتاح يجب أن يكون قوي وسري جداً
            var key = new SymmetricSecurityKey(
                                // نأخذ المفتاح من إعدادات التطبيق ونحول النص إلى بايتات لاستخدامه في التوقيع
                                Encoding.UTF8.GetBytes(_config["JwtSettings:SecretKey"]!));
            // نحدد خوارزمية التوقيع (HMAC SHA256 في هذه الحالة)
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // إنشاء التوكن
            var token = new JwtSecurityToken(
                issuer: _config["JwtSettings:Issuer"],// الجهة التي تصدر التوكن (عادةً يكون اسم التطبيق أو الشركة)
                audience: _config["JwtSettings:Audience"],// الجهة المستهدفة للتوكن (عادةً يكون نفس اسم التطبيق أو مجموعة المستخدمين المستهدفين)
                claims: claims,// إضافة المعلومات (Claims) التي قمنا بتحديدها في بداية الدالة
                expires: DateTime.UtcNow.AddDays(// تحديد مدة صلاحية التوكن، نأخذها من إعدادات التطبيق
                    int.Parse(_config["JwtSettings:ExpiryDays"]!)),// في هذا المثال، التوكن سيكون صالحاً لعدد معين من الأيام
                signingCredentials: creds// إضافة معلومات التوقيع التي قمنا بإعدادها باستخدام المفتاح والخوارزمية
            );
            //5️⃣ تحويل التوكن لنص
            return new JwtSecurityTokenHandler().WriteToken(token);// تحويل التوكن إلى نص يمكن إرساله للمستخدم

        }

        // ✅ دالة توليد Refresh Token — نص عشوائي مشفّر
        // لا يحتوي على معلومات مثل JWT — فقط نص عشوائي يُحفظ في DB
        public string GenerateRefreshToken()
        {
            var randomBytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }
    }
}
