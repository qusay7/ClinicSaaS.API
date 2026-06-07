namespace ClinicSaaS.API.DTOs.Patients
{
    // نفس حقول الإنشاء تقريباً
    // لكن أضفنا stopped لأن التعديل يسمح بتعليق المريض
    // بينما الإنشاء لا يسمح بذلك (دائماً false عند الإنشاء)

    public class UpdatePatientDto
    {
        public string FullName { get; set; } = default!;
        public DateTime? DateOfBirth { get; set; }
        public string? Phone { get; set; }
        public string? Phone2 { get; set; }
        public string? Gender { get; set; }
        public string? NationalId { get; set; }
        public bool stopped { get; set; } = false;
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
