namespace ClinicSaaS.API.DTOs.Appointments
{
    // ما يُرجعه الـ API للمستخدم
    // لاحظ: أضفنا PatientName لعرضه مباشرة بدون طلب إضافي
    public class AppointmentResponseDto
    {
        public Guid Id { get; set; }
        public Guid PatientId { get; set; }
        public string PatientName { get; set; } = default!; // اسم المريض مباشرة
        public int PatientNumber { get; set; }              // رقم المريض
        public DateTime AppointmentDate { get; set; }
        public Guid? DoctorId { get; set; }
        public string? DoctorName { get; set; }
        public Guid? TemplateId { get; set; }   // ✅ جديد — كان ناقصاً، سبب اختفاء نوع الزيارة بكل الشاشات
        public string? Type { get; set; }
        public decimal? Price { get; set; }
        public string Status { get; set; } = default!;
        public string? Notes { get; set; }
        public string? Notes2 { get; set; }
        public string? Notes3 { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CheckInTime { get; set; }   // ✅ وقت الدخول
        public DateTime? CheckOutTime { get; set; }  // ✅ وقت الخروج
        public decimal? DoctorCommissionAmount { get; set; }
        public bool? IsPaid { get; set; }
        public decimal? AmountPaid { get; set; }
        public decimal? PatientBalance { get; set; }
    }
}