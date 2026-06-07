namespace ClinicSaaS.API.DTOs.Subscriptions
{
    public class SubscriptionResponseDto
    {
        public Guid Id { get; set; }// معرف الاشتراك
        public Guid ClinicId { get; set; }// ربط الاشتراك بالعيادة — Foreign Key
        public string ClinicName { get; set; } = default!;// اسم العيادة (لراحة العميل، لا يحتاج لطلب بيانات العيادة بشكل منفصل)
        public Guid PlanId { get; set; }// ربط الاشتراك بالخطة — Foreign Key
        public string PlanName { get; set; } = default!;// اسم الخطة (لراحة العميل، لا يحتاج لطلب بيانات الخطة بشكل منفصل)
        public decimal MonthlyPrice { get; set; }// السعر الشهري للخطة وقت الاشتراك
        public decimal YearlyPrice { get; set; }// السعر السنوي للخطة وقت الاشتراك
        public int MaxUsers { get; set; }// -1 = غير محدود عدد المستخدمين
        public int MaxDoctors { get; set; }// -1 = غير محدود عدد الأطباء
        public int MaxPatients { get; set; }// -1 = غير محدود عدد المرضى
        public string BillingCycle { get; set; } = default!;// دورة الفوترة (monthly / yearly)
        public decimal PricePaid { get; set; }// المبلغ المدفوع فعلياً
        public DateTime StartDate { get; set; }// تاريخ بداية الاشتراك
        public DateTime EndDate { get; set; }// تاريخ انتهاء الاشتراك
        public bool IsActive { get; set; }// هل الاشتراك نشط حالياً
        public DateTime CreatedAt { get; set; }// تاريخ إنشاء الاشتراك

        // إحصائيات الاستخدام الحالي
        public int CurrentUsers { get; set; }// عدد المستخدمين الحاليين في العيادة
        public int CurrentDoctors { get; set; }// عدد الأطباء الحاليين في العيادة
        public int CurrentPatients { get; set; }// عدد المرضى الحاليين في العيادة
    }
}
