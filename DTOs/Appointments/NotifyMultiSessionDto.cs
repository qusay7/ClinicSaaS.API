namespace ClinicSaaS.API.DTOs.Appointments
{
    public class NotifyMultiSessionDto
    {
        public Guid PatientId { get; set; }
        public string TemplateName { get; set; } = "";
        public List<MultiSessionEntryDto> Sessions { get; set; } = new();
    }

    public class MultiSessionEntryDto
    {
        public Guid AppointmentId { get; set; }
        public int SessionNumber { get; set; }
        public DateTime AppointmentDate { get; set; }
    }
}
