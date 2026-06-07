using System.Security.Claims;

namespace ClinicSaaS.API.Services
{
    // هذا الكلاس يقرأ معلومات المستخدم من التوكن تلقائياً
    public class ClinicContext : IClinicContext
    {
        public Guid? ClinicId { get; }
        public string? Role { get; }
        public Guid? UserId { get; }
        public bool IsSuperAdmin => Role == "SuperAdmin";
        public bool IsCompanyStaff => Role == "SuperAdmin" || Role == "ClinicStaff"; // ✅
        public bool IsClinicUser => ClinicId != null; // ✅



        // IHttpContextAccessor يسمح لنا بالوصول للـ HTTP Request الحالي
        // IHttpContextAccessor يسمح لنا بالوصول للـ HTTP Request الحالي
        public ClinicContext(IHttpContextAccessor httpContextAccessor)
        {
            var user = httpContextAccessor.HttpContext?.User;

            if (user == null) return;

            // قراءة UserId من التوكن
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
                UserId = userId;

            // قراءة Role من التوكن
            Role = user.FindFirst(ClaimTypes.Role)?.Value;

            // قراءة ClinicId من التوكن
            var clinicIdClaim = user.FindFirst("ClinicId")?.Value;
            if (Guid.TryParse(clinicIdClaim, out var clinicId))
                ClinicId = clinicId;
        }


    }
}
