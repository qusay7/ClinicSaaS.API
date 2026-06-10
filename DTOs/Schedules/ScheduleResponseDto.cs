namespace ClinicSaaS.API.DTOs.Schedules
{
    public class ClinicScheduleResponseDto
    {
        public Guid Id { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public string DayName { get; set; } = default!;
        public TimeOnly OpenTime { get; set; }
        public TimeOnly CloseTime { get; set; }
        public bool IsActive { get; set; }
    }

    public class DoctorScheduleResponseDto
    {
        public Guid Id { get; set; }
        public Guid DoctorId { get; set; }
        public string DoctorName { get; set; } = default!;
        public DayOfWeek DayOfWeek { get; set; }
        public string DayName { get; set; } = default!;
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public int SlotDuration { get; set; }
        public decimal? FirstVisitPrice { get; set; }
        public decimal? FollowUpPrice { get; set; }
        public bool IsActive { get; set; }
    }
}