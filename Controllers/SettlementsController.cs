using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    /// <summary>
    /// محرك التسوية الموحّد — يخدم 3 استخدامات:
    /// 1) كشف ذمم المرضى (عرض بس، من PaymentDetail الموجود أصلاً)
    /// 2) مخالصة الطبيب حسب نسبته (Settlement بنوع "doctor")
    /// 3) مخالصة شركة التأمين (Settlement بنوع "insurance")
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SettlementsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly IPdfExportService _pdfExport;
        private readonly IExcelExportService _excelExport;
        private readonly IWebHostEnvironment _env;

        public SettlementsController(ApplicationDbContext db, IClinicContext clinicContext,
            IPdfExportService pdfExport, IExcelExportService excelExport, IWebHostEnvironment env)
        {
            _db = db;
            _clinicContext = clinicContext;
            _pdfExport = pdfExport;
            _excelExport = excelExport;
            _env = env;
        }

        // ✅ يحوّل رابط الشعار النسبي المخزّن (/logos/xxx.png?v=...) لمسار فعلي على القرص،
        // عشان QuestPDF يقدر يضمّنه بالـ PDF مباشرة
        private string? ResolveLogoPath(string? logoUrl)
        {
            if (string.IsNullOrEmpty(logoUrl)) return null;
            var cleanPath = logoUrl.Split('?')[0].TrimStart('/');
            var fullPath = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), cleanPath.Replace("logos/", "logos" + Path.DirectorySeparatorChar));
            return System.IO.File.Exists(fullPath) ? fullPath : null;
        }

        private static string Msg(string lang, string ar, string en) => lang == "ar" ? ar : en;

        // ═══════════════════════════════════════
        // 1) كشف ذمم المرضى — عرض بس
        // GET: api/settlements/patients-dues
        // ═══════════════════════════════════════
        [HttpGet("patients-dues")]
        public async Task<ActionResult> GetPatientsDues()
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            // 1) دفعات مسجّلة فيها رصيد متبقي (دُفع جزء بس)
            // ✅ نجيب الكيانات أول (بدون Select)، لأن Convert.ToBase64String ما تترجم لـ SQL مباشرة
            var partialDuesEntities = await _db.PaymentDetails
                .Include(p => p.Patient)
                .Where(p => p.ClinicId == _clinicContext.ClinicId && p.AmountPaid < p.PatientAmount)
                .ToListAsync();

            var partialDues = partialDuesEntities.Select(p => new
            {
                id = p.Id,
                appointmentId = p.AppointmentId,
                patientId = p.PatientId,
                patientName = p.Patient != null ? p.Patient.FullName : "—",
                total = p.TotalAmount,
                paid = p.AmountPaid,
                balance = p.PatientAmount - p.AmountPaid,
                date = p.CreatedAt,
                hasPaymentRecord = true,
                rowVersion = Convert.ToBase64String(p.RowVersion),
            }).ToList();

            // ✅ 2) مواعيد مكتملة بسعر محدد، لكن بدون أي دفعة مسجّلة إطلاقاً
            // (صار "تخطي وإنهاء فقط" وقت الـ Checkout) — دين كامل غير مسجّل
            var appointmentIdsWithPayment = await _db.PaymentDetails
                .Where(p => p.ClinicId == _clinicContext.ClinicId)
                .Select(p => p.AppointmentId)
                .ToListAsync();

            var unregisteredEntities = await _db.Appointments
                .Include(a => a.Patient)
                .Where(a => a.ClinicId == _clinicContext.ClinicId
                    && !a.IsDeleted
                    && a.Status == "completed"
                    && a.Price != null && a.Price > 0
                    && !appointmentIdsWithPayment.Contains(a.Id))
                .ToListAsync();

            var unregisteredDues = unregisteredEntities.Select(a => new
            {
                id = a.Id,
                appointmentId = a.Id,
                patientId = a.PatientId,
                patientName = a.Patient != null ? a.Patient.FullName : "—",
                total = a.Price!.Value,
                paid = 0m,
                balance = a.Price!.Value,
                date = a.CheckOutTime ?? a.AppointmentDate,
                hasPaymentRecord = false,
                rowVersion = (string?)null,
            }).ToList();

            var dues = partialDues.Concat(unregisteredDues)
                .OrderByDescending(d => d.balance)
                .ToList();

            return Ok(new
            {
                totalDue = dues.Sum(d => d.balance),
                count = dues.Count,
                items = dues,
            });
        }

        // ✅ GET: api/settlements/patients-dues/export?format=pdf|excel
        [HttpGet("patients-dues/export")]
        public async Task<ActionResult> ExportPatientsDues([FromQuery] string format = "pdf", [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var isRtl = lang == "ar";

            var partialDuesEntities = await _db.PaymentDetails
                .Include(p => p.Patient)
                .Where(p => p.ClinicId == _clinicContext.ClinicId && p.AmountPaid < p.PatientAmount)
                .ToListAsync();

            var appointmentIdsWithPayment = await _db.PaymentDetails
                .Where(p => p.ClinicId == _clinicContext.ClinicId)
                .Select(p => p.AppointmentId)
                .ToListAsync();

            var unregisteredEntities = await _db.Appointments
                .Include(a => a.Patient)
                .Where(a => a.ClinicId == _clinicContext.ClinicId
                    && !a.IsDeleted && a.Status == "completed"
                    && a.Price != null && a.Price > 0
                    && !appointmentIdsWithPayment.Contains(a.Id))
                .ToListAsync();

            var rows = new List<List<string>>();
            decimal totalBalance = 0;

            foreach (var p in partialDuesEntities.OrderByDescending(p => p.PatientAmount - p.AmountPaid))
            {
                var balance = p.PatientAmount - p.AmountPaid;
                totalBalance += balance;
                rows.Add(new List<string> {
                    p.Patient?.FullName ?? "—", p.TotalAmount.ToString("F2"),
                    p.AmountPaid.ToString("F2"), balance.ToString("F2"),
                    p.CreatedAt.ToString("yyyy-MM-dd"),
                });
            }
            foreach (var a in unregisteredEntities)
            {
                totalBalance += a.Price!.Value;
                rows.Add(new List<string> {
                    a.Patient?.FullName ?? "—", a.Price.Value.ToString("F2"),
                    "0.00", a.Price.Value.ToString("F2"),
                    (a.CheckOutTime ?? a.AppointmentDate).ToString("yyyy-MM-dd"),
                });
            }

            var columns = isRtl
                ? new List<string> { "المريض", "الإجمالي", "المدفوع", "المتبقي", "التاريخ" }
                : new List<string> { "Patient", "Total", "Paid", "Balance", "Date" };

            var clinic = await _db.Clinics.FindAsync(_clinicContext.ClinicId.Value);
            var summary = new List<(string, string)> {
                (isRtl ? "إجمالي المستحق" : "Total Due", totalBalance.ToString("F2")),
                (isRtl ? "عدد البنود" : "Items Count", rows.Count.ToString()),
            };

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "ذمم المرضى" : "Patient Dues",
                    Title = isRtl ? "كشف ذمم المرضى" : "Patient Dues Report",
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "patients-dues.xlsx");
            }
            else
            {
                var bytes = _pdfExport.GenerateTableReport(new PdfReportRequest
                {
                    Title = isRtl ? "كشف ذمم المرضى" : "Patient Dues Report",
                    ClinicName = clinic?.Name ?? "",
                    LogoPath = ResolveLogoPath(clinic?.Logo),
                    IsRtl = isRtl,
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                });
                return File(bytes, "application/pdf", "patients-dues.pdf");
            }
        }

        // ═══════════════════════════════════════
        // 2) مخالصة الطبيب — المستحقات غير المُسوّاة بعد
        // GET: api/settlements/doctor/{doctorId}/pending?from=&to=
        // ═══════════════════════════════════════
        [HttpGet("doctor/{doctorId}/pending")]
        public async Task<ActionResult> GetDoctorPending(Guid doctorId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var query = _db.Appointments
                .Include(a => a.Patient)
                .Where(a => a.ClinicId == _clinicContext.ClinicId
                    && a.DoctorId == doctorId
                    && !a.IsDeleted
                    && a.DoctorCommissionAmount != null
                    && a.CommissionSettlementId == null);

            if (from.HasValue) query = query.Where(a => a.AppointmentDate >= from.Value);
            if (to.HasValue) query = query.Where(a => a.AppointmentDate < to.Value.AddDays(1));

            var appointments = await query
                .OrderBy(a => a.AppointmentDate)
                .Select(a => new
                {
                    a.Id,
                    a.AppointmentDate,
                    a.Type,
                    patientName = a.Patient != null ? a.Patient.FullName : "—",
                    a.Price,
                    a.DoctorCommissionAmount,
                })
                .ToListAsync();

            return Ok(new
            {
                totalCommission = appointments.Sum(a => a.DoctorCommissionAmount ?? 0),
                count = appointments.Count,
                items = appointments,
            });
        }

        // ✅ GET: api/settlements/doctor/{doctorId}/pending/export?format=pdf|excel&from=&to=
        [HttpGet("doctor/{doctorId}/pending/export")]
        public async Task<ActionResult> ExportDoctorPending(Guid doctorId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
            [FromQuery] string format = "pdf", [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var isRtl = lang == "ar";

            var query = _db.Appointments.Include(a => a.Patient).Include(a => a.Doctor)
                .Where(a => a.ClinicId == _clinicContext.ClinicId && a.DoctorId == doctorId
                    && !a.IsDeleted && a.DoctorCommissionAmount != null && a.CommissionSettlementId == null);
            if (from.HasValue) query = query.Where(a => a.AppointmentDate >= from.Value);
            if (to.HasValue) query = query.Where(a => a.AppointmentDate < to.Value.AddDays(1));

            var appointments = await query.OrderBy(a => a.AppointmentDate).ToListAsync();
            var doctorName = appointments.FirstOrDefault()?.Doctor?.FullName
                ?? (await _db.Doctors.FindAsync(doctorId))?.FullName ?? "";

            var rows = appointments.Select(a => new List<string> {
                a.AppointmentDate.ToString("yyyy-MM-dd"), a.Patient?.FullName ?? "—",
                a.Type ?? "—", (a.DoctorCommissionAmount ?? 0).ToString("F2"),
            }).ToList();

            var columns = isRtl
                ? new List<string> { "التاريخ", "المريض", "نوع الزيارة", "الحصة" }
                : new List<string> { "Date", "Patient", "Visit Type", "Commission" };

            var total = appointments.Sum(a => a.DoctorCommissionAmount ?? 0);
            var clinic = await _db.Clinics.FindAsync(_clinicContext.ClinicId.Value);
            var summary = new List<(string, string)> {
                (isRtl ? "الطبيب" : "Doctor", doctorName),
                (isRtl ? "إجمالي المستحق" : "Total Due", total.ToString("F2")),
            };

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "مخالصة الطبيب" : "Doctor Settlement",
                    Title = isRtl ? $"مخالصة الطبيب — {doctorName}" : $"Doctor Settlement — {doctorName}",
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "doctor-settlement.xlsx");
            }
            else
            {
                var bytes = _pdfExport.GenerateTableReport(new PdfReportRequest
                {
                    Title = isRtl ? "مخالصة الطبيب" : "Doctor Settlement",
                    Subtitle = doctorName,
                    ClinicName = clinic?.Name ?? "",
                    LogoPath = ResolveLogoPath(clinic?.Logo),
                    IsRtl = isRtl,
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                });
                return File(bytes, "application/pdf", "doctor-settlement.pdf");
            }
        }

        // ═══════════════════════════════════════
        // POST: api/settlements/doctor
        // تسجيل تسوية طبيب — يقفل كل المواعيد المعروضة أعلاه دفعة واحدة
        // ═══════════════════════════════════════
        [HttpPost("doctor")]
        public async Task<ActionResult> CreateDoctorSettlement([FromBody] CreateSettlementDto dto, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("settlements.manage")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();
            if (!dto.DoctorId.HasValue)
                return BadRequest(Msg(lang, "الطبيب مطلوب", "Doctor is required"));

            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == dto.DoctorId && d.ClinicId == _clinicContext.ClinicId);
            if (doctor == null) return NotFound(Msg(lang, "الطبيب غير موجود", "Doctor not found"));

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var query = _db.Appointments.Where(a => a.ClinicId == _clinicContext.ClinicId
                    && a.DoctorId == dto.DoctorId
                    && !a.IsDeleted
                    && a.DoctorCommissionAmount != null
                    && a.CommissionSettlementId == null);

                // ✅ لو تحديد بنود معيّنة، نسوّي بس هذولا. وإلا (السلوك القديم)، كل مستحقات الفترة
                if (dto.AppointmentIds != null && dto.AppointmentIds.Count > 0)
                    query = query.Where(a => dto.AppointmentIds.Contains(a.Id));
                else
                    query = query.Where(a => a.AppointmentDate >= dto.PeriodStart && a.AppointmentDate < dto.PeriodEnd.AddDays(1));

                var appointments = await query.ToListAsync();
                if (appointments.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(Msg(lang, "لا توجد مستحقات لهذي الفترة", "No pending commission for this period"));
                }

                var total = appointments.Sum(a => a.DoctorCommissionAmount ?? 0);
                // ✅ لو ما تحدد مبلغ دفعة، نفترض دفعة كاملة (نفس السلوك القديم)
                var amountPaid = dto.AmountPaidNow ?? total;
                if (amountPaid > total) amountPaid = total;   // حماية — ما نسمح بدفعة أكبر من المستحق

                var settlement = new Settlement
                {
                    Id = Guid.NewGuid(),
                    ClinicId = _clinicContext.ClinicId.Value,
                    Type = "doctor",
                    DoctorId = dto.DoctorId,
                    PeriodStart = dto.PeriodStart,
                    PeriodEnd = dto.PeriodEnd,
                    TotalAmount = total,
                    AmountPaid = amountPaid,
                    Status = amountPaid >= total ? "paid" : "partial",
                    PaidAt = amountPaid > 0 ? DateTime.UtcNow : null,
                    PaymentMethod = dto.PaymentMethod,
                    Notes = dto.Notes,
                    CreatedAt = DateTime.UtcNow,
                };
                _db.Settlements.Add(settlement);

                foreach (var a in appointments)
                    a.CommissionSettlementId = settlement.Id;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new
                {
                    settlement.Id,
                    settlement.TotalAmount,
                    settlement.AmountPaid,
                    settlement.Status,
                    appointmentsCount = appointments.Count,
                    message = Msg(lang, "تم تسجيل تسوية الطبيب بنجاح ✅", "Doctor settlement recorded successfully ✅"),
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ═══════════════════════════════════════
        // 3) مخالصة التأمين — نفس الفكرة بالضبط لشركة تأمين
        // GET: api/settlements/insurance/{companyId}/pending?from=&to=
        // ═══════════════════════════════════════
        // ✅ مطالبات الشركة بأي حالة — يخدم التبويب الموحّد (فلترة معلّقة/مُرسلة/موافَق عليها/مدفوعة)
        // GET: api/settlements/insurance/{companyId}/claims?status=&from=&to=
        [HttpGet("insurance/{companyId}/claims")]
        public async Task<ActionResult> GetInsuranceClaimsByStatus(Guid companyId, [FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var query = _db.InsuranceClaims
                .Include(c => c.Patient)
                .Include(c => c.PatientInsurance)
                .Where(c => c.ClinicId == _clinicContext.ClinicId
                    && !c.IsDeleted
                    && c.PatientInsurance != null
                    && c.PatientInsurance.InsuranceCompanyId == companyId);

            if (!string.IsNullOrEmpty(status)) query = query.Where(c => c.Status == status);
            if (from.HasValue) query = query.Where(c => c.ServiceDate >= from.Value);
            if (to.HasValue) query = query.Where(c => c.ServiceDate < to.Value.AddDays(1));

            var claims = await query
                .OrderBy(c => c.ServiceDate)
                .Select(c => new
                {
                    c.Id,
                    c.ClaimNumber,
                    c.ServiceDate,
                    c.Status,
                    patientName = c.Patient != null ? c.Patient.FullName : "—",
                    c.InsuranceAmount,
                    c.TotalAmount,
                })
                .ToListAsync();

            return Ok(new
            {
                totalAmount = claims.Sum(c => c.InsuranceAmount),
                count = claims.Count,
                items = claims,
            });
        }

        // ✅ GET: api/settlements/insurance/{companyId}/claims/export?status=&format=pdf|excel
        [HttpGet("insurance/{companyId}/claims/export")]
        public async Task<ActionResult> ExportInsuranceClaims(Guid companyId, [FromQuery] string? status,
            [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string format = "pdf", [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var isRtl = lang == "ar";

            var query = _db.InsuranceClaims.Include(c => c.Patient).Include(c => c.PatientInsurance)
                .Where(c => c.ClinicId == _clinicContext.ClinicId && !c.IsDeleted
                    && c.PatientInsurance != null && c.PatientInsurance.InsuranceCompanyId == companyId);
            if (!string.IsNullOrEmpty(status)) query = query.Where(c => c.Status == status);
            if (from.HasValue) query = query.Where(c => c.ServiceDate >= from.Value);
            if (to.HasValue) query = query.Where(c => c.ServiceDate < to.Value.AddDays(1));

            var claims = await query.OrderBy(c => c.ServiceDate).ToListAsync();
            var companyName = (await _db.InsuranceCompanies.FindAsync(companyId))?.Name ?? "";

            var rows = claims.Select(c => new List<string> {
                c.ServiceDate.ToString("yyyy-MM-dd"), c.ClaimNumber ?? "—",
                c.Patient?.FullName ?? "—", c.InsuranceAmount.ToString("F2"),
            }).ToList();

            var columns = isRtl
                ? new List<string> { "التاريخ", "رقم المطالبة", "المريض", "مبلغ التأمين" }
                : new List<string> { "Date", "Claim #", "Patient", "Insurance Amount" };

            var clinic = await _db.Clinics.FindAsync(_clinicContext.ClinicId.Value);
            var summary = new List<(string, string)> {
                (isRtl ? "شركة التأمين" : "Insurance Company", companyName),
                (isRtl ? "الإجمالي" : "Total", claims.Sum(c => c.InsuranceAmount).ToString("F2")),
            };

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "مخالصة التأمين" : "Insurance Settlement",
                    Title = isRtl ? $"مخالصة التأمين — {companyName}" : $"Insurance Settlement — {companyName}",
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "insurance-settlement.xlsx");
            }
            else
            {
                var bytes = _pdfExport.GenerateTableReport(new PdfReportRequest
                {
                    Title = isRtl ? "مخالصة التأمين" : "Insurance Settlement",
                    Subtitle = companyName,
                    ClinicName = clinic?.Name ?? "",
                    LogoPath = ResolveLogoPath(clinic?.Logo),
                    IsRtl = isRtl,
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                });
                return File(bytes, "application/pdf", "insurance-settlement.pdf");
            }
        }

        // ✅ إرسال دفعة مطالبات جماعياً لشركة التأمين — يحوّل كل "المعلّقة" بالفترة لـ"مُرسلة" بضغطة وحدة
        // POST: api/settlements/insurance/submit-batch
        [HttpPost("insurance/submit-batch")]
        public async Task<ActionResult> SubmitClaimsBatch([FromBody] SubmitBatchDto dto, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("settlements.manage")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var claims = await _db.InsuranceClaims
                .Where(c => c.ClinicId == _clinicContext.ClinicId
                    && !c.IsDeleted
                    && c.Status == "pending"
                    && c.PatientInsurance != null
                    && c.PatientInsurance.InsuranceCompanyId == dto.InsuranceCompanyId
                    && c.ServiceDate >= dto.PeriodStart
                    && c.ServiceDate < dto.PeriodEnd.AddDays(1))
                .ToListAsync();

            if (claims.Count == 0)
                return BadRequest(Msg(lang, "لا توجد مطالبات معلّقة بهذي الفترة", "No pending claims for this period"));

            foreach (var c in claims)
            {
                c.Status = "submitted";
                c.SubmittedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();

            return Ok(new
            {
                count = claims.Count,
                message = Msg(lang, $"تم إرسال {claims.Count} مطالبة بنجاح ✅", $"{claims.Count} claim(s) submitted successfully ✅"),
            });
        }

        // ✅ المطالبات "الموافَق عليها" بس — الجاهزة فعلياً للتسوية
        // (المعلّقة/المرفوضة ما تظهر هنا — التسوية لازم تشمل بس اللي وافقت عليها الشركة)
        [HttpGet("insurance/{companyId}/pending")]
        public async Task<ActionResult> GetInsurancePending(Guid companyId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var query = _db.InsuranceClaims
                .Include(c => c.Patient)
                .Include(c => c.PatientInsurance)
                .Where(c => c.ClinicId == _clinicContext.ClinicId
                    && !c.IsDeleted
                    && c.SettlementId == null
                    && c.Status == "approved"
                    && c.PatientInsurance != null
                    && c.PatientInsurance.InsuranceCompanyId == companyId);

            if (from.HasValue) query = query.Where(c => c.ServiceDate >= from.Value);
            if (to.HasValue) query = query.Where(c => c.ServiceDate < to.Value.AddDays(1));

            var claims = await query
                .OrderBy(c => c.ServiceDate)
                .Select(c => new
                {
                    c.Id,
                    c.ClaimNumber,
                    c.ServiceDate,
                    patientName = c.Patient != null ? c.Patient.FullName : "—",
                    c.InsuranceAmount,
                    c.Status,
                })
                .ToListAsync();

            return Ok(new
            {
                totalInsuranceAmount = claims.Sum(c => c.InsuranceAmount),
                count = claims.Count,
                items = claims,
            });
        }

        // ═══════════════════════════════════════
        // POST: api/settlements/insurance
        // ═══════════════════════════════════════
        [HttpPost("insurance")]
        public async Task<ActionResult> CreateInsuranceSettlement([FromBody] CreateSettlementDto dto, [FromQuery] string lang = "ar")
        {
            if (!_clinicContext.HasPermission("settlements.manage")) return Forbid();
            if (_clinicContext.ClinicId == null) return Unauthorized();
            if (!dto.InsuranceCompanyId.HasValue)
                return BadRequest(Msg(lang, "شركة التأمين مطلوبة", "Insurance company is required"));

            var company = await _db.InsuranceCompanies.FirstOrDefaultAsync(c => c.Id == dto.InsuranceCompanyId && c.ClinicId == _clinicContext.ClinicId);
            if (company == null) return NotFound(Msg(lang, "شركة التأمين غير موجودة", "Insurance company not found"));

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var claimsQuery = _db.InsuranceClaims
                    .Where(c => c.ClinicId == _clinicContext.ClinicId
                        && !c.IsDeleted
                        && c.SettlementId == null
                        && c.Status == "approved"
                        && c.PatientInsurance != null
                        && c.PatientInsurance.InsuranceCompanyId == dto.InsuranceCompanyId);

                // ✅ لو تحديد مطالبات معيّنة، نسوّي بس هذولا. وإلا (السلوك القديم)، كل مطالبات الفترة
                if (dto.ClaimIds != null && dto.ClaimIds.Count > 0)
                    claimsQuery = claimsQuery.Where(c => dto.ClaimIds.Contains(c.Id));
                else
                    claimsQuery = claimsQuery.Where(c => c.ServiceDate >= dto.PeriodStart && c.ServiceDate < dto.PeriodEnd.AddDays(1));

                var claims = await claimsQuery.ToListAsync();

                if (claims.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(Msg(lang, "لا توجد مطالبات لهذي الفترة", "No pending claims for this period"));
                }

                var total = claims.Sum(c => c.InsuranceAmount);
                var amountPaid = dto.AmountPaidNow ?? total;
                if (amountPaid > total) amountPaid = total;

                var settlement = new Settlement
                {
                    Id = Guid.NewGuid(),
                    ClinicId = _clinicContext.ClinicId.Value,
                    Type = "insurance",
                    InsuranceCompanyId = dto.InsuranceCompanyId,
                    PeriodStart = dto.PeriodStart,
                    PeriodEnd = dto.PeriodEnd,
                    TotalAmount = total,
                    AmountPaid = amountPaid,
                    Status = amountPaid >= total ? "paid" : "partial",
                    PaidAt = amountPaid > 0 ? DateTime.UtcNow : null,
                    PaymentMethod = dto.PaymentMethod,
                    Notes = dto.Notes,
                    CreatedAt = DateTime.UtcNow,
                };
                _db.Settlements.Add(settlement);

                // ✅ لو دفعة جزئية، المطالبات تفضل حالتها "approved" (لسا مو مقفولة بالكامل) —
                // بس نربطها بالتسوية عشان تختفي من قائمة "المستحق" وما تتكرر بتسوية ثانية
                var claimStatus = amountPaid >= total ? "paid" : "approved";
                foreach (var c in claims)
                {
                    c.SettlementId = settlement.Id;
                    c.Status = claimStatus;
                    if (amountPaid >= total) c.PaidAt = DateTime.UtcNow;
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new
                {
                    settlement.Id,
                    settlement.TotalAmount,
                    settlement.AmountPaid,
                    settlement.Status,
                    claimsCount = claims.Count,
                    message = Msg(lang, "تم تسجيل تسوية التأمين بنجاح ✅", "Insurance settlement recorded successfully ✅"),
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ═══════════════════════════════════════
        // GET: api/settlements — سجل كل التسويات السابقة (طبيب + تأمين مع بعض)
        // ═══════════════════════════════════════
        [HttpGet]
        public async Task<ActionResult> GetAll([FromQuery] string? type)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var query = _db.Settlements
                .Include(s => s.Doctor)
                .Include(s => s.InsuranceCompany)
                .Where(s => s.ClinicId == _clinicContext.ClinicId);

            if (!string.IsNullOrEmpty(type)) query = query.Where(s => s.Type == type);

            var settlements = await query
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new
                {
                    s.Id,
                    s.Type,
                    doctorName = s.Doctor != null ? s.Doctor.FullName : null,
                    insuranceCompanyName = s.InsuranceCompany != null ? s.InsuranceCompany.Name : null,
                    s.PeriodStart,
                    s.PeriodEnd,
                    s.TotalAmount,
                    s.AmountPaid,
                    s.Status,
                    s.PaidAt,
                    s.PaymentMethod,
                    s.Notes,
                    s.CreatedAt,
                })
                .ToListAsync();

            return Ok(settlements);
        }

        // ✅ GET: api/settlements/export?type=&format=pdf|excel
        [HttpGet("export")]
        public async Task<ActionResult> ExportHistory([FromQuery] string? type, [FromQuery] string format = "pdf", [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var isRtl = lang == "ar";

            var query = _db.Settlements.Include(s => s.Doctor).Include(s => s.InsuranceCompany)
                .Where(s => s.ClinicId == _clinicContext.ClinicId);
            if (!string.IsNullOrEmpty(type)) query = query.Where(s => s.Type == type);

            var settlements = await query.OrderByDescending(s => s.CreatedAt).ToListAsync();

            var rows = settlements.Select(s => new List<string> {
                s.Type == "doctor" ? (isRtl ? "طبيب" : "Doctor") : (isRtl ? "تأمين" : "Insurance"),
                s.Doctor?.FullName ?? s.InsuranceCompany?.Name ?? "—",
                $"{s.PeriodStart:yyyy-MM-dd} — {s.PeriodEnd:yyyy-MM-dd}",
                s.TotalAmount.ToString("F2"), s.AmountPaid.ToString("F2"),
                s.Status == "paid" ? (isRtl ? "مدفوعة" : "Paid") : (isRtl ? "جزئية" : "Partial"),
                s.CreatedAt.ToString("yyyy-MM-dd"),
            }).ToList();

            var columns = isRtl
                ? new List<string> { "النوع", "الطرف", "الفترة", "الإجمالي", "المدفوع", "الحالة", "التاريخ" }
                : new List<string> { "Type", "Party", "Period", "Total", "Paid", "Status", "Date" };

            var clinic = await _db.Clinics.FindAsync(_clinicContext.ClinicId.Value);
            var summary = new List<(string, string)> {
                (isRtl ? "إجمالي المدفوع" : "Total Paid", settlements.Sum(s => s.AmountPaid).ToString("F2")),
                (isRtl ? "عدد التسويات" : "Settlements Count", settlements.Count.ToString()),
            };

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "سجل التسويات" : "Settlement History",
                    Title = isRtl ? "سجل التسويات المالية" : "Settlement History",
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "settlement-history.xlsx");
            }
            else
            {
                var bytes = _pdfExport.GenerateTableReport(new PdfReportRequest
                {
                    Title = isRtl ? "سجل التسويات المالية" : "Settlement History",
                    ClinicName = clinic?.Name ?? "",
                    LogoPath = ResolveLogoPath(clinic?.Logo),
                    IsRtl = isRtl,
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                });
                return File(bytes, "application/pdf", "settlement-history.pdf");
            }
        }
    }

    public class CreateSettlementDto
    {
        public Guid? DoctorId { get; set; }
        public Guid? InsuranceCompanyId { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Notes { get; set; }

        // ✅ جديد — لو مُرسلة، نسوّي بس هالبنود المحدَّدة (بدل كل مستحقات الفترة تلقائياً)
        public List<Guid>? AppointmentIds { get; set; }
        public List<Guid>? ClaimIds { get; set; }

        // ✅ جديد — دفعة جزئية (لو أقل من الإجمالي). لو فاضي، نفترض دفعة كاملة (نفس السلوك القديم)
        public decimal? AmountPaidNow { get; set; }
    }

    public class SubmitBatchDto
    {
        public Guid InsuranceCompanyId { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
    }
}