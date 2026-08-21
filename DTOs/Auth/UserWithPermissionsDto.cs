namespace ClinicSaaS.API.DTOs.Auth
{
    // DTO جديد
    public class UserWithPermissionsDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public Guid? ClinicId { get; set; }
        public string ClinicName { get; set; }
        public List<string> Permissions { get; set; }
    }
}
