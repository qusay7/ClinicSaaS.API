namespace ClinicSaaS.API.DTOs.Doctors
{
    public class CreateDoctorDto
    {
        public string FullName { get; set; } = default!;  // اسم الطبيب
        public string? Specialty { get; set; }            // التخصص
        public string? Phone { get; set; }                // الهاتف
        public string? Email { get; set; }                // البريد
        public string? Notes { get; set; }                // ملاحظات
     }
}
