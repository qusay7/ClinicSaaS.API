namespace ClinicSaaS.API.DTOs.Plans
{
    // PlanResponseDto.cs
    public class PlanResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public decimal MonthlyPrice { get; set; }
        public decimal YearlyPrice { get; set; }
        public int MaxUsers { get; set; }
        public int MaxDoctors { get; set; }
        public int MaxPatients { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<string> Features { get; set; } = new();   // ✅ جديد
        public bool IsFeatured { get; set; }                    // ✅ جديد
        public int MaxDailyMessages { get; set; }
        public bool HasElectronicInvoicing { get; set; }
        public bool HasMultipleDepartments { get; set; }
    }
}