namespace ClinicSaaS.API.DTOs.Plans
{
    public class PlanResponseDto
    {
        public Guid Id { get; set; }// معرف الخطة
        public string Name { get; set; } = default!;// اسم الخطة (مثلاً: Basic, Pro, Enterprise)
        public string? Description { get; set; }// وصف الخطة (اختياري)
        public decimal MonthlyPrice { get; set; }// السعر الشهري
        public decimal YearlyPrice { get; set; }// السعر السنوي
        public int MaxUsers { get; set; }// -1 = غير محدود عدد المستخدمين
        public int MaxDoctors { get; set; }// -1 = غير محدود عدد الأطباء
        public int MaxPatients { get; set; }// -1 = غير محدود عدد المرضى
        public bool IsActive { get; set; }// هل الخطة نشطة حالياً
        public DateTime CreatedAt { get; set; }// تاريخ إنشاء الخطة
    }
}