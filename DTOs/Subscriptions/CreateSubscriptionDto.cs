namespace ClinicSaaS.API.DTOs.Subscriptions
{
    

    public class CreateSubscriptionDto
    {
        public Guid ClinicId { get; set; }
        public Guid PlanId { get; set; }
        public string BillingCycle { get; set; } = "monthly";
        public decimal? PricePaid { get; set; }
        public DateTime StartDate { get; set; } = DateTime.UtcNow; // ✅ افتراضي
    }
}
