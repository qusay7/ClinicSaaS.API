namespace ClinicSaaS.API.DTOs.TreatmentPlans
{
    // ── القوالب ──
     

        public class CreateTreatmentTemplateDto
        {
            public string Name { get; set; } = "";
            public string? NameEn { get; set; }
            public Guid? DepartmentId { get; set; }
            public int DefaultSessionsCount { get; set; } = 1;
            public decimal? DefaultPricePerSession { get; set; }
            public decimal? DefaultTotalPrice { get; set; }

            // ✅ لقوالب الزيارة الواحدة (كشف/مراجعة/استشارة/متابعة)
            public decimal? FirstVisitPrice { get; set; }
            public decimal? FollowUpPrice { get; set; }
        }
    

    // ── الخطة العلاجية ──
    public class CreateTreatmentPlanDto
    {
        public Guid PatientId { get; set; }
        public Guid? DoctorId { get; set; }
        public Guid? TemplateId { get; set; }        // لو موجود، ناخذ منه الاسم/الجلسات/السعر كافتراضي

        // لو ما فيه TemplateId، أو حاب تتجاوز قيم القالب — هذي إلزامية بحالة "خطة مخصصة"
        public string? Name { get; set; }
        public int? TotalSessions { get; set; }
        public string PricingType { get; set; } = "per_session";  // per_session / total
        public decimal? PricePerSession { get; set; }
        public decimal? TotalPrice { get; set; }
        public string PaymentType { get; set; } = "per_session";  // per_session / upfront
    }

    public class UpdateTreatmentPlanStatusDto
    {
        public string Status { get; set; } = "";  // active / completed / cancelled / paused
    }

    // ── الجلسات ──
    public class UpdateSessionDto
    {
        public string Status { get; set; } = "";       // scheduled / completed / missed / cancelled
        public Guid? AppointmentId { get; set; }
        public DateTime? ScheduledDate { get; set; }
        public decimal? Cost { get; set; }
        public bool? IsPaid { get; set; }
        public string? Notes { get; set; }
    }
}