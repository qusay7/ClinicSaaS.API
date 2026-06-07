namespace ClinicSaaS.API.DTOs.Appointments
{
    // ما يُرسله المستخدم عند تعديل موعد
    // أضفنا Status لأن التعديل يسمح بتغيير حالة الموعد
    public class UpdateAppointmentDto
    {
       public Guid PatientId { get; set; }// معرف المريض (لربط الموعد بالمريض) — يجب أن يكون موجودًا لتحديد المريض الذي ينتمي إليه الموعد
        public DateTime? AppointmentDate { get; set; }// تاريخ ووقت الموعد (اختياري للتعديل)
        public Guid? DoctorId { get; set; }
        public string? Type { get; set; }  // ✅ أضف هذا// نوع الموعد (اختياري للتعديل)
        public decimal? Price { get; set; }// سعر الموعد (اختياري للتعديل)
        public string? Status { get; set; } // حالة الموعد (اختياري للتعديل) — يمكن أن تكون "Scheduled", "Confirmed", "Completed", "Cancelled"
        public string? Notes { get; set; }// ملاحظات عامة عن الموعد (اختياري للتعديل)
        public string? Notes2 { get; set; }// ملاحظات إضافية (اختياري للتعديل)
        public string? Notes3 { get; set; }// ملاحظات إضافية أخرى (اختياري للتعديل)
    }
}

