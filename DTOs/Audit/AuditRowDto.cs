namespace ClinicSaaS.API.DTOs.Audit
{
    public class AuditRowDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public DateTime? CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
        public string? UpdatedByName { get; set; }
        public bool IsDeleted { get; set; }
    }
}
