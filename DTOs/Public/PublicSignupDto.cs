namespace ClinicSaaS.API.DTOs.Public
{
    // ✅ نموذج "ابدأ مجاناً" العام بالموقع — الحد الأدنى من الحقول فقط،
    // بعكس CreateClinicDto (نموذج SuperAdmin الكامل) اللي فيه حقول كثيرة
    // غير ضرورية لعيادة تسجّل نفسها بنفسها بتجربة مجانية
    public class PublicSignupDto
    {
        public string ClinicName { get; set; } = default!;
        public string Subdomain { get; set; } = default!;
        public string OwnerName { get; set; } = default!;
        public string? OwnerEmail { get; set; }
        public string? OwnerPhone { get; set; }
    }
}
