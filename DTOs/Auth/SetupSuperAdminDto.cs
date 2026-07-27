namespace ClinicSaaS.API.DTOs.Auth
{
    public class SetupSuperAdminDto
    {
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string Password { get; set; } = default!;
    }
}
