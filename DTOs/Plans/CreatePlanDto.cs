namespace ClinicSaaS.API.DTOs.Plans
{
    public class CreatePlanDto
    {
        public string Name { get; set; } = default!;// اسم الباقة (مثلاً: Basic, Pro, Enterprise)
        public string? Description { get; set; }// وصف الباقة (اختياري)
        public decimal MonthlyPrice { get; set; }// السعر الشهري
        public decimal YearlyPrice { get; set; }// السعر السنوي
        public int MaxUsers { get; set; }    // -1 = غير محدود عدد المستخدمين
        public int MaxDoctors { get; set; }  // -1 = غير محدود عدد الأطباء
        public int MaxPatients { get; set; } // -1 = غير محدود عدد المرضى
    }
}
