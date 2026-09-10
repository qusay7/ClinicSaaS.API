using System.Text.Json;

namespace ClinicSaaS.API.Services
{
    public interface IWhatsAppConnectionService
    {
        Task<(string Status, string? QrDataUrl, string? PhoneNumber)> GetStateAsync(Guid clinicId);
        Task<string> ConnectAsync(Guid clinicId);
    }

    // ✅ يتواصل مع خدمة الواتساب الداخلية (Node/Baileys) — جلسة مستقلة لكل عيادة.
    // هذي الخدمة الداخلية غير مكشوفة للإنترنت وبدون توثيق خاص بها، فالفرونت اند
    // لا يناديها مباشرة أبداً — يمر دايماً عبر هذا الوسيط المحمي بصلاحيات العيادة.
    public class WhatsAppConnectionService : IWhatsAppConnectionService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public WhatsAppConnectionService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        private string BaseUrl => _configuration["WhatsAppService:BaseUrl"] ?? "http://localhost:3001";

        public async Task<(string Status, string? QrDataUrl, string? PhoneNumber)> GetStateAsync(Guid clinicId)
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/clinics/{clinicId}/qr-data");
            if (!response.IsSuccessStatusCode)
                return ("not_started", null, null);

            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var status = root.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "not_started" : "not_started";
            var qrDataUrl = root.TryGetProperty("qrDataUrl", out var qrProp) && qrProp.ValueKind == JsonValueKind.String
                ? qrProp.GetString()
                : null;
            var phoneNumber = root.TryGetProperty("phoneNumber", out var phoneProp) && phoneProp.ValueKind == JsonValueKind.String
                ? phoneProp.GetString()
                : null;

            return (status, qrDataUrl, phoneNumber);
        }

        public async Task<string> ConnectAsync(Guid clinicId)
        {
            var response = await _httpClient.PostAsync($"{BaseUrl}/clinics/{clinicId}/start", null);
            if (!response.IsSuccessStatusCode)
                return "error";

            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "connecting" : "connecting";
        }
    }
}
