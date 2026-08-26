using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClinicSaaS.API.Filters;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [RequireActiveSubscription]
    public class PaymentsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public PaymentsController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        private static string Msg(string lang, string ar, string en) => lang == "ar" ? ar : en;

        // ═══════════════════════════════════════
        // GET: api/payments/appointment/{appointmentId}
        // جلب تفاصيل الدفع لموعد محدد
        // ═══════════════════════════════════════
        [HttpGet("appointment/{appointmentId}")]
        public async Task<ActionResult> GetByAppointment(Guid appointmentId)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var payment = await _db.PaymentDetails
                .Include(p => p.Patient)
                .Include(p => p.InsuranceClaim)
                    .ThenInclude(c => c != null ? c.PatientInsurance : null)
                        .ThenInclude(i => i != null ? i.InsuranceCompany : null)
                .FirstOrDefaultAsync(p =>
                    p.AppointmentId == appointmentId &&
                    p.ClinicId == _clinicContext.ClinicId);

            if (payment == null)
                return Ok(new { hasPayment = false });

            return Ok(new
            {
                hasPayment = true,
                id = payment.Id,
                totalAmount = payment.TotalAmount,
                insuranceAmount = payment.InsuranceAmount,
                patientAmount = payment.PatientAmount,
                amountPaid = payment.AmountPaid,
                paymentMethod = payment.PaymentMethod,
                isPaid = payment.IsPaid,
                paidAt = payment.PaidAt?.ToString("yyyy-MM-dd HH:mm"),
                patientBalance = payment.AmountPaid - payment.PatientAmount,
                insuranceBalance = payment.InsuranceBalance,
                notes = payment.Notes,
                createdAt = payment.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                companyName = payment.InsuranceClaim?.PatientInsurance?.InsuranceCompany?.Name,
                claimNumber = payment.InsuranceClaim?.ClaimNumber,
                claimStatus = payment.InsuranceClaim?.Status,
                // ✅ نرجع RowVersion للفرونت إند عشان يستخدمه بأي تعديل لاحق
                rowVersion = Convert.ToBase64String(payment.RowVersion),
            });
        }

        // ═══════════════════════════════════════
        // POST: api/payments
        // تسجيل دفعة جديدة عند الخروج
        // ═══════════════════════════════════════
        [HttpPost]
        public async Task<ActionResult> CreatePayment([FromBody] CreatePaymentDto dto, [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var appointment = await _db.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == dto.AppointmentId && !a.IsDeleted);

            if (appointment == null)
                return NotFound(Msg(lang, "الموعد غير موجود", "Appointment not found"));

            if (appointment.ClinicId != _clinicContext.ClinicId)
                return Forbid();

            // تحقق لم يُسجل دفع مسبقاً (فحص أولي — الحماية الحقيقية بالـ Unique Index + catch أدناه)
            var existing = await _db.PaymentDetails.AnyAsync(p => p.AppointmentId == dto.AppointmentId);
            if (existing)
                return BadRequest(Msg(lang, "تم تسجيل الدفع مسبقاً لهذا الموعد", "Payment already recorded for this appointment"));

            var totalAmount = dto.TotalAmount ?? (appointment.Price ?? 0);

            // ── جلب بيانات التأمين ──
            // ✅ لو الفرونت إند أرسل مبلغ التأمين صراحة (من نافذة الفاتورة الجديدة)، نستخدمه مباشرة.
            // وإلا (نداء قديم بدون هذا الحقل)، نرجع لطريقة البحث عن InsuranceClaim كما كانت
            decimal insuranceAmount;
            decimal patientAmount;
            Guid? claimId = null;

            if (dto.InsuranceAmount.HasValue)
            {
                insuranceAmount = dto.InsuranceAmount.Value;
                patientAmount = totalAmount - insuranceAmount;

                var linkedClaim = await _db.InsuranceClaims
                    .FirstOrDefaultAsync(c => c.AppointmentId == dto.AppointmentId);
                if (linkedClaim != null) claimId = linkedClaim.Id;
            }
            else
            {
                insuranceAmount = 0;
                patientAmount = totalAmount;

                var claim = await _db.InsuranceClaims
                    .FirstOrDefaultAsync(c => c.AppointmentId == dto.AppointmentId);

                if (claim != null)
                {
                    insuranceAmount = claim.InsuranceAmount;
                    patientAmount = claim.PatientAmount;
                    claimId = claim.Id;
                }
            }

            // ── حساب المستحقات ──
            var amountPaid = dto.AmountPaid;
            var patientBalance = amountPaid - patientAmount;
            var insuranceBalance = insuranceAmount;

            var payment = new PaymentDetail
            {
                Id = Guid.NewGuid(),
                ClinicId = _clinicContext.ClinicId.Value,
                AppointmentId = dto.AppointmentId,
                PatientId = appointment.PatientId,
                TotalAmount = totalAmount,
                InsuranceAmount = insuranceAmount,
                PatientAmount = patientAmount,
                AmountPaid = amountPaid,
                PaymentMethod = dto.PaymentMethod ?? "cash",
                IsPaid = amountPaid >= patientAmount,
                PaidAt = amountPaid > 0 ? DateTime.UtcNow : null,
                InsuranceBalance = insuranceBalance,
                InsuranceClaimId = claimId,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow,
            };

            _db.PaymentDetails.Add(payment);

            // ✅ فحص أخير على مستوى قاعدة البيانات — يمنع الدفعة المزدوجة حتى مع التصادم اللحظي
            // (يتطلب Unique Index على PaymentDetail.AppointmentId بالموديل)
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return BadRequest(Msg(lang,
                    "تم تسجيل الدفع للتو من مستخدم آخر لهذا الموعد",
                    "Payment was just recorded by someone else for this appointment"));
            }

            return Ok(new
            {
                id = payment.Id,
                totalAmount = payment.TotalAmount,
                insuranceAmount = payment.InsuranceAmount,
                patientAmount = payment.PatientAmount,
                amountPaid = payment.AmountPaid,
                patientBalance,
                insuranceBalance = payment.InsuranceBalance,
                isPaid = payment.IsPaid,
                rowVersion = Convert.ToBase64String(payment.RowVersion),
                message = payment.IsPaid
                    ? Msg(lang, "تم تسجيل الدفع بنجاح ✅", "Payment recorded successfully ✅")
                    : patientBalance < 0
                        ? Msg(lang, $"يوجد مبلغ مستحق على المريض: {Math.Abs(patientBalance):F2} د.أ", $"Patient owes: {Math.Abs(patientBalance):F2} JD")
                        : Msg(lang, $"يوجد مبلغ مستحق للمريض: {patientBalance:F2} د.أ", $"Patient credit: {patientBalance:F2} JD"),
            });
        }

        // ═══════════════════════════════════════
        // PUT: api/payments/{id}
        // تحديث دفعة (دفع جزئي أو كامل لاحقاً)
        // ═══════════════════════════════════════
        [HttpPut("{id}")]
        public async Task<ActionResult> UpdatePayment(Guid id, [FromBody] UpdatePaymentDto dto, [FromQuery] string lang = "ar")
        {
            var payment = await _db.PaymentDetails.FindAsync(id);
            if (payment == null || payment.ClinicId != _clinicContext.ClinicId) return NotFound();

            // ✅ حماية من الكتابة فوق تعديل مستخدم آخر
            if (dto.RowVersion != null && dto.RowVersion.Length > 0)
                _db.Entry(payment).Property(p => p.RowVersion).OriginalValue = dto.RowVersion;

            // ✅ لو الفرونت إند أرسل فاتورة معدّلة (بنود/نسبة تأمين مختلفة)، نحدّث المجاميع كمان
            if (dto.TotalAmount.HasValue)
            {
                payment.TotalAmount = dto.TotalAmount.Value;
                payment.InsuranceAmount = dto.InsuranceAmount ?? payment.InsuranceAmount;
                payment.PatientAmount = dto.TotalAmount.Value - (dto.InsuranceAmount ?? payment.InsuranceAmount);
            }

            payment.AmountPaid = dto.AmountPaid;
            payment.PaymentMethod = dto.PaymentMethod ?? payment.PaymentMethod;
            payment.IsPaid = dto.AmountPaid >= payment.PatientAmount;
            payment.PaidAt = dto.AmountPaid > 0 ? DateTime.UtcNow : payment.PaidAt;
            payment.Notes = dto.Notes ?? payment.Notes;

            if (dto.InsuranceReceived.HasValue)
                payment.InsuranceBalance = payment.InsuranceAmount - dto.InsuranceReceived.Value;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(Msg(lang,
                    "تم تعديل هذا السجل من مستخدم آخر، يرجى تحديث الصفحة والمحاولة مرة أخرى",
                    "This record was modified by another user, please refresh and try again"));
            }

            return Ok(new
            {
                message = Msg(lang, "تم التحديث بنجاح", "Updated successfully"),
                isPaid = payment.IsPaid,
                patientBalance = payment.AmountPaid - payment.PatientAmount,
                insuranceBalance = payment.InsuranceBalance,
                rowVersion = Convert.ToBase64String(payment.RowVersion),
            });
        }

        // ═══════════════════════════════════════
        // GET: api/payments
        // قائمة المدفوعات مع فلتر
        // ═══════════════════════════════════════
        [HttpGet]
        public async Task<ActionResult> GetAll(
            [FromQuery] bool? isPaid,
            [FromQuery] string? from,
            [FromQuery] string? to,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var query = _db.PaymentDetails
                .Include(p => p.Patient)
                .Include(p => p.InsuranceClaim)
                .Where(p => p.ClinicId == _clinicContext.ClinicId);

            if (isPaid.HasValue)
                query = query.Where(p => p.IsPaid == isPaid);

            if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fromDate))
                query = query.Where(p => p.CreatedAt >= fromDate);

            if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var toDate))
                query = query.Where(p => p.CreatedAt < toDate.AddDays(1));

            var total = await query.CountAsync();

            var payments = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new
                {
                    p.Id,
                    p.AppointmentId,
                    patientName = p.Patient != null ? p.Patient.FullName : "—",
                    p.TotalAmount,
                    p.InsuranceAmount,
                    p.PatientAmount,
                    p.AmountPaid,
                    patientBalance = p.AmountPaid - p.PatientAmount,
                    p.InsuranceBalance,
                    p.PaymentMethod,
                    p.IsPaid,
                    paidAt = p.PaidAt.HasValue ? p.PaidAt.Value.ToString("yyyy-MM-dd HH:mm") : null,
                    claimNumber = p.InsuranceClaim != null ? p.InsuranceClaim.ClaimNumber : null,
                    claimStatus = p.InsuranceClaim != null ? p.InsuranceClaim.Status : null,
                    createdAt = p.CreatedAt.ToString("yyyy-MM-dd"),
                })
                .ToListAsync();

            var stats = await _db.PaymentDetails
                .Where(p => p.ClinicId == _clinicContext.ClinicId)
                .GroupBy(p => 1)
                .Select(g => new
                {
                    totalRevenue = g.Sum(p => p.TotalAmount),
                    totalPaid = g.Sum(p => p.AmountPaid),
                    totalInsurance = g.Sum(p => p.InsuranceAmount),
                    totalPatient = g.Sum(p => p.PatientAmount),
                    pendingInsurance = g.Sum(p => p.InsuranceBalance),
                    unpaidCount = g.Count(p => !p.IsPaid),
                })
                .FirstOrDefaultAsync();

            return Ok(new { total, page, pageSize, pages = (int)Math.Ceiling((double)total / pageSize), payments, stats });
        }

        // ═══════════════════════════════════════
        // GET: api/payments/stats
        // إحصائيات الدفع
        // ═══════════════════════════════════════
        [HttpGet("stats")]
        public async Task<ActionResult> GetStats()
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var now = DateTime.UtcNow;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);

            var all = await _db.PaymentDetails
                .Where(p => p.ClinicId == _clinicContext.ClinicId)
                .ToListAsync();

            return Ok(new
            {
                totalRevenue = all.Sum(p => p.TotalAmount),
                totalCollected = all.Sum(p => p.AmountPaid),
                totalInsurance = all.Sum(p => p.InsuranceAmount),
                pendingInsurance = all.Sum(p => p.InsuranceBalance),
                monthRevenue = all.Where(p => p.CreatedAt >= startOfMonth).Sum(p => p.TotalAmount),
                monthCollected = all.Where(p => p.CreatedAt >= startOfMonth).Sum(p => p.AmountPaid),
                paidCount = all.Count(p => p.IsPaid),
                unpaidCount = all.Count(p => !p.IsPaid),
                patientOwes = all.Where(p => p.AmountPaid < p.PatientAmount).Sum(p => p.PatientAmount - p.AmountPaid),
                patientCredit = all.Where(p => p.AmountPaid > p.PatientAmount).Sum(p => p.AmountPaid - p.PatientAmount),
                byMethod = all.GroupBy(p => p.PaymentMethod)
                    .Select(g => new { method = g.Key, count = g.Count(), total = g.Sum(p => p.AmountPaid) })
                    .ToList(),
            });
        }


    }

    // ═══════════════════════════════════════
    // DTOs
    // ═══════════════════════════════════════
    public class CreatePaymentDto
    {
        public Guid AppointmentId { get; set; }
        public decimal AmountPaid { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Notes { get; set; }

        // ✅ جديد — لو الفرونت إند يحسب فاتورة ببنود متعددة (زي نافذة إنهاء الزيارة)،
        // نستخدم هذي القيم مباشرة بدل ما نشتقها من Appointment.Price/InsuranceClaim
        public decimal? TotalAmount { get; set; }
        public decimal? InsuranceAmount { get; set; }
    }

    public class UpdatePaymentDto
    {
        public decimal AmountPaid { get; set; }
        public string? PaymentMethod { get; set; }
        public decimal? InsuranceReceived { get; set; }
        public string? Notes { get; set; }
        public byte[] RowVersion { get; set; } = default!;

        // ✅ جديد — لتحديث فاتورة معدّلة (بنود مختلفة أو نسبة تأمين معدّلة) بدل قيمها القديمة
        public decimal? TotalAmount { get; set; }
        public decimal? InsuranceAmount { get; set; }
    }
}