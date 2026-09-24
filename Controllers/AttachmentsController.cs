using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClinicSaaS.API.Filters;

namespace ClinicSaaS.API.Controllers
{
    // ✅ مرفقات المريض (أشعة، تحاليل...) — الملفات تُخزَّن بمجلد "uploads/" الخاص
    // خارج wwwroot، فلا يوجد أي رابط عام لها؛ يُقرأ الملف فقط من هنا بعد التحقق
    // من الصلاحية وإن المريض ينتمي لنفس عيادة المستخدم.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [RequireActiveSubscription]
    public class AttachmentsController : ControllerBase
    {
        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".pdf" };
        private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20 MB

        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly IWebHostEnvironment _env;

        public AttachmentsController(ApplicationDbContext db, IClinicContext clinicContext, IWebHostEnvironment env)
        {
            _db = db;
            _clinicContext = clinicContext;
            _env = env;
        }

        private static string Msg(string lang, string ar, string en) => lang == "ar" ? ar : en;

        private string StorageRoot => Path.Combine(_env.ContentRootPath, "uploads", "attachments");

        private static string ContentTypeFor(string ext) => ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream",
        };

        // GET: api/attachments/patient/{patientId}
        [HttpGet("patient/{patientId}")]
        public async Task<ActionResult> GetForPatient(Guid patientId)
        {
            if (!_clinicContext.HasPermission("patients.view")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == patientId && !p.IsDeleted);
            if (patient == null) return NotFound();
            if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId) return Forbid();

            var items = await _db.Attachments
                .Where(a => a.PatientId == patientId && !a.IsDeleted)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new
                {
                    id = a.Id,
                    fileName = a.FileName,
                    fileType = a.FileType,
                    fileSize = a.FileSize,
                    category = a.Category,
                    notes = a.Notes,
                    createdAt = a.CreatedAt,
                    appointmentId = a.AppointmentId,
                    isImage = a.FileType.StartsWith("image/"),
                })
                .ToListAsync();

            return Ok(items);
        }

        // POST: api/attachments/upload
        [HttpPost("upload")]
        public async Task<ActionResult> Upload(IFormFile file, [FromForm] Guid patientId,
            [FromForm] string? category, [FromForm] Guid? appointmentId, [FromForm] string? notes,
            [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("patients.edit")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            if (file == null || file.Length == 0)
                return BadRequest(Msg(lang, "الملف مطلوب", "File is required"));

            if (file.Length > MaxFileSizeBytes)
                return BadRequest(Msg(lang, "حجم الملف أكبر من 20 ميجا", "File exceeds the 20 MB limit"));

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                return BadRequest(Msg(lang, "نوع الملف غير مسموح", "File type is not allowed"));

            var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == patientId && !p.IsDeleted);
            if (patient == null) return NotFound(Msg(lang, "المريض غير موجود", "Patient not found"));
            if (!_clinicContext.IsSuperAdmin && patient.ClinicId != _clinicContext.ClinicId) return Forbid();

            var clinicId = patient.ClinicId;
            var storedFileName = $"{Guid.NewGuid()}{ext}";
            var clinicDir = Path.Combine(StorageRoot, clinicId.ToString());
            Directory.CreateDirectory(clinicDir);
            var fullPath = Path.Combine(clinicDir, storedFileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var attachment = new Attachment
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                PatientId = patientId,
                AppointmentId = appointmentId,
                FileName = file.FileName,
                FilePath = Path.Combine(clinicId.ToString(), storedFileName),
                FileType = ContentTypeFor(ext),
                FileSize = file.Length,
                Category = string.IsNullOrWhiteSpace(category) ? "other" : category,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
                CreatedAt = DateTime.UtcNow,
            };

            _db.Attachments.Add(attachment);
            await _db.SaveChangesAsync();

            return Ok(new { id = attachment.Id, message = Msg(lang, "تم رفع الملف بنجاح", "File uploaded successfully") });
        }

        // GET: api/attachments/{id}/file
        [HttpGet("{id}/file")]
        public async Task<ActionResult> GetFile(Guid id)
        {
            if (!_clinicContext.HasPermission("patients.view")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var attachment = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
            if (attachment == null) return NotFound();
            if (!_clinicContext.IsSuperAdmin && attachment.ClinicId != _clinicContext.ClinicId) return Forbid();

            var fullPath = Path.Combine(StorageRoot, attachment.FilePath);
            if (!System.IO.File.Exists(fullPath)) return NotFound();

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
            return File(bytes, attachment.FileType, attachment.FileName);
        }

        // DELETE: api/attachments/{id}
        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(Guid id, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("patients.edit")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var attachment = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
            if (attachment == null) return NotFound();
            if (!_clinicContext.IsSuperAdmin && attachment.ClinicId != _clinicContext.ClinicId) return Forbid();

            attachment.IsDeleted = true;
            await _db.SaveChangesAsync();

            return Ok(new { message = Msg(lang, "تم الحذف", "Deleted") });
        }
    }
}
