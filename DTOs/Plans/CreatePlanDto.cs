namespace ClinicSaaS.API.DTOs.Plans
{
    // CreatePlanDto.cs
    public class CreatePlanDto
    {
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public decimal MonthlyPrice { get; set; }
        public decimal YearlyPrice { get; set; }
        public int MaxUsers { get; set; }
        public int MaxDoctors { get; set; }
        public int MaxPatients { get; set; }
        public string? FeaturesText { get; set; }   // ✅ جديد — كل ميزة بسطر (\n)
        public bool IsFeatured { get; set; } = false; // ✅ جديد
    }
}
