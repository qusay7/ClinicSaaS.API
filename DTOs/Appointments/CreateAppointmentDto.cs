using System.Text.Json.Serialization;

namespace ClinicSaaS.API.DTOs.Appointments
{
    public class CreateAppointmentDto
    {
        public Guid PatientId { get; set; }

        // ✅ يقبل "2026-07-01 09:00" و "2026-07-01T09:00:00" كلاهما
        [JsonConverter(typeof(FlexibleDateTimeConverter))]
        public DateTime AppointmentDate { get; set; }

        public Guid? DoctorId { get; set; }
        public string? Type { get; set; }
        public decimal? Price { get; set; }
        public string? Notes { get; set; }
        public string? Notes2 { get; set; }
        public string? Notes3 { get; set; }
        public string? Lang { get; set; }
    }

    // ✅ Converter يقبل كل الصيغ
    public class FlexibleDateTimeConverter : System.Text.Json.Serialization.JsonConverter<DateTime>
    {
        private static readonly string[] Formats = {
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
        };

        public override DateTime Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            var str = reader.GetString() ?? "";
            if (DateTime.TryParseExact(str, Formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
                return dt;

            if (DateTime.TryParse(str, out var dt2))
                return dt2;

            throw new System.Text.Json.JsonException($"Cannot convert '{str}' to DateTime");
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, DateTime value, System.Text.Json.JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss"));
    }
}