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
    }
}