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
        public string? TimeFormat { get; set; }
        public bool NotifyOnCreate { get; set; } = true;
        public bool NotifyOnEdit { get; set; } = true;
        public bool NotifyOnCancel { get; set; } = true;
        public bool NotifyBefore12h { get; set; } = true;
        public bool NotifyBefore1h { get; set; } = true;
    }
}