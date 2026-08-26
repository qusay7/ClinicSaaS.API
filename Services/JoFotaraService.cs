using System.Text;
using System.Text.Json;

namespace ClinicSaaS.API.Services
{
    public class JoFotaraResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string? ResponseText { get; set; }
        public string? QrCode { get; set; }
    }

    public interface IJoFotaraService
    {
        Task<JoFotaraResult> SendInvoiceAsync(string invoiceXml, string clientId, string secretKey);
    }

    public class JoFotaraService : IJoFotaraService
    {
        private const string Url = "https://backend.jofotara.gov.jo/core/invoices/";
        private readonly IHttpClientFactory _httpFactory;

        public JoFotaraService(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

        public async Task<JoFotaraResult> SendInvoiceAsync(string invoiceXml, string clientId, string secretKey)
        {
            // ✅ نفس آلية SendInvoiceRequest_NoCLR: XML مُرمّز Base64 داخل {"invoice": "..."}
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(invoiceXml));
            var body = JsonSerializer.Serialize(new { invoice = base64 });

            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            using var req = new HttpRequestMessage(HttpMethod.Post, Url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("Client-Id", clientId);
            req.Headers.Add("Secret-Key", secretKey);

            var res = await client.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();

            var result = new JoFotaraResult
            {
                StatusCode = (int)res.StatusCode,
                ResponseText = text,
                Success = res.IsSuccessStatusCode,
            };

            if (res.IsSuccessStatusCode)
                result.QrCode = ExtractQr(text);

            return result;
        }

        // ✅ نفس مسارات البحث بالإجراء المخزّن، مع fallback نصي
        private static string? ExtractQr(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                foreach (var path in new[] { "EINV_QR", "einv_qr" })
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(path, out var v))
                        return v.GetString();

                foreach (var parent in new[] { "EINV_RESULTS", "result", "data" })
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(parent, out var p)
                        && p.TryGetProperty("EINV_QR", out var q))
                        return q.GetString();

                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
                    && root[0].TryGetProperty("EINV_QR", out var a))
                    return a.GetString();
            }
            catch { /* الرد مو JSON صالح — نكمل بالبحث النصي */ }

            const string tag = "\"EINV_QR\":\"";
            var start = text.IndexOf(tag, StringComparison.Ordinal);
            if (start < 0) return null;
            start += tag.Length;
            var end = text.IndexOf('"', start);
            return end > start ? text[start..end] : null;
        }
    }
}