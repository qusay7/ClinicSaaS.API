namespace ClinicSaaS.API.DTOs.Schedules
{
    public class CreateClinicScheduleDto
    {
        public DayOfWeek DayOfWeek { get; set; }  // 0=أحد ... 6=سبت
        public TimeOnly OpenTime { get; set; }    // 08:00
        public TimeOnly CloseTime { get; set; }   // 20:00
    }
}