namespace ClinicSaaS.API.DTOs.Schedules
{
    public class CreateDoctorScheduleDto
    {
        public Guid DoctorId { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public int SlotDuration { get; set; } = 30;
        public decimal? FirstVisitPrice { get; set; }
        public decimal? FollowUpPrice { get; set; }
    }
}