using System.Text.Json.Serialization;

namespace ClinicSaaS.API.DTOs.Appointments
{
    // ما يُرسله المستخدم عند تعديل موعد
    // أضفنا Status لأن التعديل يسمح بتغيير حالة الموعد
    public class UpdateAppointmentDto
    {
       public Guid PatientId { get; set; }// معرف المريض (لربط الموعد بالمريض) — يجب أن يكون موجودًا لتحديد المريض الذي ينتمي إليه الموعد

        // ✅ يقبل "2026-07-01 09:00" و"2026-07-01T09:00:00" كلاهما — بدون هذا
        // المحوّل، أي وقت بلا ثوانٍ (وهو شكل قيمة datetime-local الطبيعي)
        // يفشل بالتحويل الافتراضي لـ System.Text.Json ويرجع 400
        [JsonConverter(typeof(NullableFlexibleDateTimeConverter))]
        public DateTime? AppointmentDate { get; set; }// تاريخ ووقت الموعد (اختياري للتعديل)
        public Guid? DoctorId { get; set; }
        public string? Type { get; set; }  // ✅ أضف هذا// نوع الموعد (اختياري للتعديل)
        public decimal? Price { get; set; }// سعر الموعد (اختياري للتعديل)
        public string? Status { get; set; } // حالة الموعد (اختياري للتعديل) — يمكن أن تكون "Scheduled", "Confirmed", "Completed", "Cancelled"
        public string? Notes { get; set; }// ملاحظات عامة عن الموعد (اختياري للتعديل)
        public string? Notes2 { get; set; }// ملاحظات إضافية (اختياري للتعديل)
        public string? Notes3 { get; set; }// ملاحظات إضافية أخرى (اختياري للتعديل)
    }

    // ✅ نفس صيغ FlexibleDateTimeConverter (CreateAppointmentDto.cs) لكن لـ DateTime? الاختياري
    public class NullableFlexibleDateTimeConverter : JsonConverter<DateTime?>
    {
        private static readonly string[] Formats = {
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
        };

        public override DateTime? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            if (reader.TokenType == System.Text.Json.JsonTokenType.Null) return null;

            var str = reader.GetString() ?? "";
            if (DateTime.TryParseExact(str, Formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
                return dt;

            if (DateTime.TryParse(str, out var dt2))
                return dt2;

            throw new System.Text.Json.JsonException($"Cannot convert '{str}' to DateTime");
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, DateTime? value, System.Text.Json.JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteStringValue(value.Value.ToString("yyyy-MM-ddTHH:mm:ss"));
            else writer.WriteNullValue();
        }
    }
}

