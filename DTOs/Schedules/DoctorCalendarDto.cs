namespace ClinicSaaS.API.DTOs.Schedules
{
    public class DoctorCalendarDto
    {
        public Guid DoctorId { get; set; }
        public string DoctorName { get; set; } = "";
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        public List<DoctorCalendarDayDto> Days { get; set; } = new();
    }

    public class DoctorCalendarDayDto
    {
        public DateTime Date { get; set; }
        public int DayOfWeek { get; set; }

        public bool IsWorkingDay { get; set; }
        public bool IsAbsent { get; set; }

        public List<DoctorCalendarSlotDto> Slots { get; set; } = new();
    }

    public class DoctorCalendarSlotDto
    {
        public string Start { get; set; } = "";
        public string End { get; set; } = "";

        // available
        // booked
        // absent
        // outside
        public string Status { get; set; } = "";

        public Guid? AppointmentId { get; set; }
        public string? PatientName { get; set; }
        public string? AppointmentStatus { get; set; }
    }
}