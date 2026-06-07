namespace ClinicSaaS.API.DTOs.Subscriptions
{
    public class CreateSubscriptionDto
    {
        public Guid ClinicId { get; set; }          // العيادة
        public Guid PlanId { get; set; }            // الخطة
        public string BillingCycle { get; set; } = "monthly"; // monthly / yearly
        public DateTime StartDate { get; set; }     // تاريخ البداية
    }
}
