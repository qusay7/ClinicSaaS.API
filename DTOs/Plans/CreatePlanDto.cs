namespace ClinicSaaS.API.DTOs.Plans
{
    // CreatePlanDto.cs
    public class CreatePlanDto
    {
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public string? NameEn { get; set; }
        public string? DescriptionEn { get; set; }
        public decimal MonthlyPrice { get; set; }
        public decimal YearlyPrice { get; set; }
        public int MaxUsers { get; set; }
        public int MaxDoctors { get; set; }
        public int MaxPatients { get; set; }
        public string? FeaturesText { get; set; }   // ✅ جديد — كل ميزة بسطر (\n)
        public string? FeaturesTextEn { get; set; }  // ✅ النسخة الإنجليزية، بنفس التنسيق
        public bool IsFeatured { get; set; } = false; // ✅ جديد
        public int MaxDailyMessages { get; set; }
        public bool HasElectronicInvoicing { get; set; }
        public bool HasMultipleDepartments { get; set; }
    }
}
