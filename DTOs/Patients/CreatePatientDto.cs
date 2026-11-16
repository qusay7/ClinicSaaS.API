namespace ClinicSaaS.API.DTOs.Patients
{
    // هذا الكلاس يحدد فقط الحقول التي يحق للمستخدم إرسالها عند إنشاء مريض
    // لاحظ: لا يوجد Id, isdeleted, CreatedAt — يحددها النظام تلقائياً دائماً.
    // PatientNumber استثناء: مُقترَح تلقائياً لكن قابل للتعديل (انظر التعليق تحت)
    public class CreatePatientDto
    {
        // ✅ اختياري — النموذج يعرض رقماً مقترحاً تلقائياً (GET next-number)
        // قابل للتعديل من المستخدم قبل الحفظ؛ لو ما أُرسل، النظام يولّده تلقائياً
        public int? PatientNumber { get; set; }
        public string FullName { get; set; } = default!;
        public DateTime? DateOfBirth { get; set; }
        public string? Phone { get; set; }
        public string? Phone2 { get; set; }
        public string? Gender { get; set; }
        public string? NationalId { get; set; }
        public string? Notes { get; set; }
        public string? Notes2 { get; set; }
        public string? Notes3 { get; set; }
        public string? BloodType { get; set; }     // فصيلة الدم (A+, B-, ...)
        public string? Address { get; set; }        // العنوان
        public string? Email { get; set; }          // البريد الإلكتروني
        public string? EmergencyContact { get; set; } // اسم شخص للطوارئ
        public string? EmergencyPhone { get; set; }   // هاتف شخص الطوارئ
        public string? Allergies { get; set; }      // الحساسية (بنسلين, ...)
        public string? ChronicDiseases { get; set; } // أمراض مزمنة (سكري, ضغط, ...)
        public string? Occupation { get; set; }     // المهنة
        public string? MaritalStatus { get; set; }  // الحالة الاجتماعية

    }
}
