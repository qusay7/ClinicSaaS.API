namespace ClinicSaaS.API.DTOs.Users
{
    public class UpdateUserDto
    {
        public string FullName { get; set; } = default!;
        public string? Username { get; set; }
        public string Role { get; set; } = default!;
        public Guid? DepartmentId { get; set; }

        // ✅ اختياري — فاضي يعني "ما تغيّرش كلمة المرور"
        public string? Password { get; set; }
    }
}
