namespace ClinicSaaS.API.DTOs.Roles
{
    public class RoleResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public Guid? ClinicId { get; set; }

        public List<PermissionDto> Permissions { get; set; } = new();
    }

    public class PermissionDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string DisplayName { get; set; } = default!;
        public string Group { get; set; } = default!;
        public bool IsGranted { get; set; }
    }

    public class UpdateRolePermissionsDto
    {
        public List<Guid> PermissionIds { get; set; } = new();
    }
}