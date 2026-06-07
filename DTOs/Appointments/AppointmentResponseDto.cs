namespace ClinicSaaS.API.DTOs.Appointments
{
    // ما يُرجعه الـ API للمستخدم
    // لاحظ: أضفنا PatientName لعرضه مباشرة بدون طلب إضافي
    public class AppointmentResponseDto
    {
        public Guid Id { get; set; }// معرف الموعد
        public Guid PatientId { get; set; }// معرف المريض (لربط الموعد بالمريض)
        public string PatientName { get; set; } = null!;// اسم المريض (للعرض فقط)
        public int PatientNumber { get; set; }              // رقم المريض
        public DateTime AppointmentDate { get; set; }// تاريخ ووقت الموعد
        public Guid? DoctorId { get; set; }    // ✅ أضف
        public string? Type { get; set; }  // ✅ أضف هذا
        public string? DoctorName { get; set; } // ✅ يبقى للعرض        public string? Type { get; set; }// نوع الموعد
        public decimal? Price { get; set; }// سعر الموعد
        public string Status { get; set; } = null!;// حالة الموعد
        public string? Notes { get; set; }// ملاحظات عامة عن الموعد
        public string? Notes2 { get; set; }// ملاحظات إضافية
        public string? Notes3 { get; set; }// ملاحظات إضافية أخرى
        public DateTime CreatedAt { get; set; }// تاريخ إنشاء الموعد
    }
}


