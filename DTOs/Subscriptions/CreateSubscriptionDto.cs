namespace ClinicSaaS.API.DTOs.Subscriptions
{
    

    public class CreateSubscriptionDto
    {
        public Guid ClinicId { get; set; }
        public Guid PlanId { get; set; }
        public string BillingCycle { get; set; } = "monthly";
        public decimal? PricePaid { get; set; }
        public DateTime StartDate { get; set; } = DateTime.UtcNow; // ✅ افتراضي

        // ✅ استثناء مقاعد مستخدمين إضافية — SuperAdmin فقط يقدر يحدده فعلياً
        // (الكونترولر يتجاهله لو المستخدم ClinicStaff)
        public int ExtraUserAllowance { get; set; } = 0;
    }
}
