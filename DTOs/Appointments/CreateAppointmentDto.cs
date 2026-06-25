namespace ClinicSaaS.API.DTOs.Appointments
{
    // ما يُرسله المستخدم عند حج
    //
    // ز موعد جديد
    // لاحظ: لا يوجد Id, CreatedAt, isdeleted — يحددها النظام تلقائياً
    public class CreateAppointmentDto
    {
        public Guid PatientId { get; set; }// ربط الموعد بالمريض — Foreign Key
        public DateTime AppointmentDate { get; set; }// تاريخ ووقت الموعد
        public Guid? DoctorId { get; set; }// ربط الموعد بالطبيب (اختياري)
        public string? Type { get; set; }// نوع الموعد (استشارة, متابعة, ...)
        public decimal? Price { get; set; }// سعر الموعد
        public string? Notes { get; set; }// ملاحظات عامة عن الموعد
        public string? Notes2 { get; set; }// ملاحظات إضافية (يمكن استخدامها لأي غرض)
        public string? Notes3 { get; set; }// ملاحظات إضافية أخرى (يمكن استخدامها لأي غرض)
        public string? Lang { get; set; } //
    }

}
