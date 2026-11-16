namespace ClinicSaaS.API.DTOs.Users
{
    public class UserResponseDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = default!;
        public string? Username { get; set; }
        public string Email { get; set; } = default!;
        public string Role { get; set; } = default!;
        public bool IsActive { get; set; }
        public Guid? ClinicId { get; set; }
        public string? ClinicName { get; set; }
        public Guid? DepartmentId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
