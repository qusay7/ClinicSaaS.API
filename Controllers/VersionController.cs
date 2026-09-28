using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class VersionController : ControllerBase
    {
        // ✅ نقرأ الـ commit الحقيقي من مجلد السورس وقت الطلب، مش رقم ثابت بننساه
        // — عشان نعرف بالضبط شو الكود المنشور فعلياً بأي بيئة (تفادياً لالتباس صار سابقاً)
        private static string GetCommitHash()
        {
            try
            {
                var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                var output = process?.StandardOutput.ReadToEnd().Trim();
                process?.WaitForExit(3000);
                return string.IsNullOrWhiteSpace(output) ? "unknown" : output;
            }
            catch
            {
                return "unknown";
            }
        }

        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new { commit = GetCommitHash() });
        }
    }
}
