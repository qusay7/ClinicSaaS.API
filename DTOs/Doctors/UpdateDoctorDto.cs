namespace ClinicSaaS.API.DTOs.Doctors
{
    public class UpdateDoctorDto
    {
        public string FullName { get; set; } = default!;
        public string? Specialty { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public Guid? DepartmentId { get; set; }        // ✅
        public string WorkType { get; set; } = "both"; // ✅
    }
}