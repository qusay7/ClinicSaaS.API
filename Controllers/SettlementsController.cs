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

        public SettlementsController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
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