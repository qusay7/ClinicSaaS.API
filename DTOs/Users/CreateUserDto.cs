namespace ClinicSaaS.API.DTOs.Users
{
    public class CreateUserDto
    {
        public string FullName { get; set; } = default!;
        public string? Username { get; set; }   // ✅ اختياري
        public string Email { get; set; } = default!;
        public string Password { get; set; } = default!;
        public string Role { get; set; } = default!;
        public Guid? ClinicId { get; set; }
    }
}
