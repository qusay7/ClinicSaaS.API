namespace ClinicSaaS.API.DTOs.Clinics
{
    public class UpdateClinicDto
    {
        public string Name { get; set; } = default!;
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? Description { get; set; }
        public string? OwnerName { get; set; }
        public string? OwnerPhone { get; set; }
        public string? OwnerEmail { get; set; }
        public string? TaxNumber { get; set; }
        public string? TimeZone { get; set; }
    }
}