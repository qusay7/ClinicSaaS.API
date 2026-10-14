namespace ClinicSaaS.API.DTOs.Doctors
{
    public class DoctorResponseDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = default!;
        public string? Specialty { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; }
        public Guid ClinicId { get; set; }
        public string? ClinicName { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? DepartmentId { get; set; }        // ✅
        public string? DepartmentName { get; set; }    // ✅ اسم القسم
        public string WorkType { get; set; } = "both"; // ✅
        // ✅ طبيب قسم الطوارئ يتابع مرضاه عبر طابور الطوارئ (تسجيل دخول بدون
        // موعد)، مو عبر حجز موعد عادي — تُستخدم لإخفائه من قوائم اختيار الطبيب
        // بشاشات حجز المواعيد
        public bool IsEmergencyDoctor { get; set; }
    }
}