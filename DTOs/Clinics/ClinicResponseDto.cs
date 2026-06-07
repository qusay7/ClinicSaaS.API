namespace ClinicSaaS.API.DTOs.Clinics
{
    public class ClinicResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string Subdomain { get; set; } = default!;
        public string? Logo { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Website { get; set; }
        public string? Email { get; set; }
        public string? OwnerName { get; set; }
        public string? OwnerEmail { get; set; }
        public string? OwnerPhone { get; set; }
        public string? TaxNumber { get; set; }
        public string? CommercialRegister { get; set; }
        public string? InvoiceId { get; set; }
        public string? InvoiceKey { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
