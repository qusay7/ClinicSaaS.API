using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class InsuranceController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;

        public InsuranceController(ApplicationDbContext db, IClinicContext clinicContext)
        {
            _db = db;
            _clinicContext = clinicContext;
        }

        private static string Msg(string lang, string ar, string en) => lang == "ar" ? ar : en;

        private static string GenerateClaimNumber() =>
            $"CLM-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}";

        // ════════════════════════════════════════
        // شركات التأمين
        // ════════════════════════════════════════

        // GET: api/insurance/companies
        [HttpGet("companies")]
        public async Task<ActionResult> GetCompanies()
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var companies = await _db.InsuranceCompanies
                .Where(c => c.ClinicId == _clinicContext.ClinicId)
                .OrderBy(c => c.Name)
                .Select(c => new {
                    c.Id,
                    c.Name,
                    c.NameEn,
                    c.Phone,
                    c.Email,
                    c.ContactName,
                    c.CoverageRate,
                    c.IsActive,
                    patientsCount = _db.PatientInsurances.Count(p => p.InsuranceCompanyId == c.Id && p.IsActive),
                })
                .ToListAsync();
            return Ok(companies);
        }

        // POST: api/insurance/companies
        [HttpPost("companies")]
        public async Task<ActionResult> CreateCompany([FromBody] CreateInsuranceCompanyDto dto, [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            if (string.IsNullOrEmpty(dto.Name))
                return BadRequest(Msg(lang, "اسم الشركة مطلوب", "Company name is required"));

            var exists = await _db.InsuranceCompanies.AnyAsync(c =>
                c.ClinicId == _clinicContext.ClinicId && c.Name == dto.Name);
            if (exists)
                return BadRequest(Msg(lang, "شركة التأمين موجودة مسبقاً", "Insurance company already exists"));

            var company = new InsuranceCompany
            {
                Id = Guid.NewGuid(),
                ClinicId = _clinicContext.ClinicId.Value,
                Name = dto.Name,
                NameEn = dto.NameEn,
                Phone = dto.Phone,
                Email = dto.Email,
                ContactName = dto.ContactName,
                CoverageRate = dto.CoverageRate,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };
            _db.InsuranceCompanies.Add(company);
            await _db.SaveChangesAsync();
            return Ok(new { company.Id, message = Msg(lang, "تم إضافة شركة التأمين", "Insurance company added") });
        }

        // PUT: api/insurance/companies/{id}
        [HttpPut("companies/{id}")]
        public async Task<ActionResult> UpdateCompany(Guid id, [FromBody] CreateInsuranceCompanyDto dto, [FromQuery] string lang = "ar")
        {
            var company = await _db.InsuranceCompanies.FindAsync(id);
            if (company == null || company.ClinicId != _clinicContext.ClinicId) return NotFound();
            company.Name = dto.Name; company.NameEn = dto.NameEn;
            company.Phone = dto.Phone; company.Email = dto.Email;
            company.ContactName = dto.ContactName; company.CoverageRate = dto.CoverageRate;
            company.IsActive = dto.IsActive;
            await _db.SaveChangesAsync();
            return Ok(new { message = Msg(lang, "تم التحديث", "Updated successfully") });
        }

        // DELETE: api/insurance/companies/{id}
        [HttpDelete("companies/{id}")]
        public async Task<ActionResult> DeleteCompany(Guid id, [FromQuery] string lang = "ar")
        {
            var company = await _db.InsuranceCompanies.FindAsync(id);
            if (company == null || company.ClinicId != _clinicContext.ClinicId) return NotFound();
            var hasPatients = await _db.PatientInsurances.AnyAsync(p => p.InsuranceCompanyId == id);
            if (hasPatients)
                return BadRequest(Msg(lang, "لا يمكن حذف شركة مرتبطة بمرضى", "Cannot delete company linked to patients"));
            _db.InsuranceCompanies.Remove(company);
            await _db.SaveChangesAsync();
            return Ok(new { message = Msg(lang, "تم الحذف", "Deleted") });
        }

        // ════════════════════════════════════════
        // بوالص المرضى
        // ════════════════════════════════════════

        // GET: api/insurance/patient/{patientId}
        [HttpGet("patient/{patientId}")]
        public async Task<ActionResult> GetPatientInsurances(Guid patientId)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var insurances = await _db.PatientInsurances
                .Include(p => p.InsuranceCompany)
                .Where(p => p.PatientId == patientId && p.ClinicId == _clinicContext.ClinicId)
                .OrderByDescending(p => p.IsPrimary)
                .Select(p => new {
                    p.Id,
                    p.PolicyNumber,
                    p.MembershipNumber,
                    p.CoverageRate,
                    p.MaxCoverageAmount,
                    startDate = p.StartDate.ToString("yyyy-MM-dd"),
                    endDate = p.EndDate.ToString("yyyy-MM-dd"),
                    p.IsActive,
                    p.IsPrimary,
                    p.Notes,
                    companyId = p.InsuranceCompanyId,
                    companyName = p.InsuranceCompany!.Name,
                    isExpired = p.EndDate < DateTime.UtcNow,
                    claimsCount = _db.InsuranceClaims.Count(c => c.PatientInsuranceId == p.Id),
                })
                .ToListAsync();
            return Ok(insurances);
        }

        // POST: api/insurance/patient
        [HttpPost("patient")]
        public async Task<ActionResult> AddPatientInsurance([FromBody] CreatePatientInsuranceDto dto, [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var patient = await _db.Patients.FindAsync(dto.PatientId);
            if (patient == null) return NotFound(Msg(lang, "المريض غير موجود", "Patient not found"));

            var company = await _db.InsuranceCompanies.FindAsync(dto.InsuranceCompanyId);
            if (company == null) return NotFound(Msg(lang, "شركة التأمين غير موجودة", "Insurance company not found"));

            if (dto.EndDate <= dto.StartDate)
                return BadRequest(Msg(lang, "تاريخ الانتهاء يجب أن يكون بعد تاريخ البداية", "End date must be after start date"));

            // إذا هذه بوليصة رئيسية — ألغِ الرئيسية السابقة
            if (dto.IsPrimary)
            {
                var existing = await _db.PatientInsurances
                    .Where(p => p.PatientId == dto.PatientId && p.IsPrimary && p.ClinicId == _clinicContext.ClinicId)
                    .ToListAsync();
                existing.ForEach(p => p.IsPrimary = false);
            }

            var insurance = new PatientInsurance
            {
                Id = Guid.NewGuid(),
                PatientId = dto.PatientId,
                ClinicId = _clinicContext.ClinicId.Value,
                InsuranceCompanyId = dto.InsuranceCompanyId,
                PolicyNumber = dto.PolicyNumber,
                MembershipNumber = dto.MembershipNumber,
                CoverageRate = dto.CoverageRate ?? company.CoverageRate,
                MaxCoverageAmount = dto.MaxCoverageAmount,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                IsActive = true,
                IsPrimary = dto.IsPrimary,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow,
            };
            _db.PatientInsurances.Add(insurance);
            await _db.SaveChangesAsync();
            return Ok(new { insurance.Id, message = Msg(lang, "تم إضافة بوليصة التأمين", "Insurance policy added") });
        }

        // DELETE: api/insurance/patient/{id}
        [HttpDelete("patient/{id}")]
        public async Task<ActionResult> DeletePatientInsurance(Guid id, [FromQuery] string lang = "ar")
        {
            var insurance = await _db.PatientInsurances.FindAsync(id);
            if (insurance == null || insurance.ClinicId != _clinicContext.ClinicId) return NotFound();
            var hasClaims = await _db.InsuranceClaims.AnyAsync(c => c.PatientInsuranceId == id);
            if (hasClaims)
                return BadRequest(Msg(lang, "لا يمكن حذف بوليصة مرتبطة بمطالبات", "Cannot delete policy linked to claims"));
            _db.PatientInsurances.Remove(insurance);
            await _db.SaveChangesAsync();
            return Ok(new { message = Msg(lang, "تم الحذف", "Deleted") });
        }

        // ════════════════════════════════════════
        // حساب حصة المريض (بدون حفظ)
        // ════════════════════════════════════════

        // GET: api/insurance/calculate?patientId=&amount=
        [HttpGet("calculate")]
        public async Task<ActionResult> Calculate([FromQuery] Guid patientId, [FromQuery] decimal amount)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var insurance = await _db.PatientInsurances
                .Include(p => p.InsuranceCompany)
                .FirstOrDefaultAsync(p =>
                    p.PatientId == patientId &&
                    p.ClinicId == _clinicContext.ClinicId &&
                    p.IsActive && p.IsPrimary &&
                    p.EndDate >= DateTime.UtcNow);

            if (insurance == null)
                return Ok(new { hasInsurance = false, patientAmount = amount, insuranceAmount = 0 });

            var insuranceAmount = Math.Round(amount * insurance.CoverageRate / 100, 3);
            var patientAmount = amount - insuranceAmount;

            return Ok(new
            {
                hasInsurance = true,
                companyName = insurance.InsuranceCompany?.Name,
                policyNumber = insurance.PolicyNumber,
                coverageRate = insurance.CoverageRate,
                totalAmount = amount,
                insuranceAmount,
                patientAmount,
                insuranceId = insurance.Id,
                expiryDate = insurance.EndDate.ToString("yyyy-MM-dd"),
            });
        }

        // ════════════════════════════════════════
        // المطالبات
        // ════════════════════════════════════════

        // GET: api/insurance/claims
        [HttpGet("claims")]
        public async Task<ActionResult> GetClaims(
            [FromQuery] string? status,
            [FromQuery] Guid? patientId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var query = _db.InsuranceClaims
                .Include(c => c.Patient)
                .Include(c => c.PatientInsurance).ThenInclude(p => p!.InsuranceCompany)
                .Where(c => c.ClinicId == _clinicContext.ClinicId);

            if (!string.IsNullOrEmpty(status)) query = query.Where(c => c.Status == status);
            if (patientId.HasValue) query = query.Where(c => c.PatientId == patientId);

            var total = await query.CountAsync();
            var claims = await query
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(c => new {
                    c.Id,
                    c.ClaimNumber,
                    c.Status,
                    c.TotalAmount,
                    c.InsuranceAmount,
                    c.PatientAmount,
                    c.CoverageRate,
                    c.ApprovalNumber,
                    c.RejectionReason,
                    c.Notes,
                    serviceDate = c.ServiceDate.ToString("yyyy-MM-dd"),
                    submittedAt = c.SubmittedAt.HasValue ? c.SubmittedAt.Value.ToString("yyyy-MM-dd") : null,
                    approvedAt = c.ApprovedAt.HasValue ? c.ApprovedAt.Value.ToString("yyyy-MM-dd") : null,
                    paidAt = c.PaidAt.HasValue ? c.PaidAt.Value.ToString("yyyy-MM-dd") : null,
                    patientName = c.Patient != null ? c.Patient.FullName : "—",
                    companyName = c.PatientInsurance != null && c.PatientInsurance.InsuranceCompany != null
                        ? c.PatientInsurance.InsuranceCompany.Name : "—",
                    policyNumber = c.PatientInsurance != null ? c.PatientInsurance.PolicyNumber : "—",
                    c.AppointmentId,
                })
                .ToListAsync();

            return Ok(new { total, page, pageSize, pages = (int)Math.Ceiling((double)total / pageSize), claims });
        }

        // POST: api/insurance/claims
        [HttpPost("claims")]
        public async Task<ActionResult> CreateClaim([FromBody] CreateInsuranceClaimDto dto, [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var insurance = await _db.PatientInsurances
                .Include(p => p.InsuranceCompany)
                .FirstOrDefaultAsync(p => p.Id == dto.PatientInsuranceId && p.ClinicId == _clinicContext.ClinicId);
            if (insurance == null)
                return NotFound(Msg(lang, "بوليصة التأمين غير موجودة", "Insurance policy not found"));

            if (insurance.EndDate < DateTime.UtcNow)
                return BadRequest(Msg(lang, "بوليصة التأمين منتهية الصلاحية", "Insurance policy is expired"));

            var insuranceAmount = Math.Round(dto.TotalAmount * insurance.CoverageRate / 100, 3);
            var patientAmount = dto.TotalAmount - insuranceAmount;

            var claim = new InsuranceClaim
            {
                Id = Guid.NewGuid(),
                ClinicId = _clinicContext.ClinicId.Value,
                PatientId = insurance.PatientId,
                PatientInsuranceId = dto.PatientInsuranceId,
                AppointmentId = dto.AppointmentId,
                ClaimNumber = GenerateClaimNumber(),
                TotalAmount = dto.TotalAmount,
                CoverageRate = insurance.CoverageRate,
                InsuranceAmount = insuranceAmount,
                PatientAmount = patientAmount,
                Status = "pending",
                ApprovalNumber = dto.ApprovalNumber,
                Notes = dto.Notes,
                ServiceDate = dto.ServiceDate,
                CreatedAt = DateTime.UtcNow,
            };
            _db.InsuranceClaims.Add(claim);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                claim.Id,
                claim.ClaimNumber,
                claim.TotalAmount,
                claim.InsuranceAmount,
                claim.PatientAmount,
                message = Msg(lang, "تم إنشاء المطالبة بنجاح", "Claim created successfully"),
            });
        }

        // PUT: api/insurance/claims/{id}/status
        [HttpPut("claims/{id}/status")]
        public async Task<ActionResult> UpdateClaimStatus(Guid id, [FromBody] UpdateClaimStatusDto dto, [FromQuery] string lang = "ar")
        {
            var claim = await _db.InsuranceClaims.FindAsync(id);
            if (claim == null || claim.ClinicId != _clinicContext.ClinicId) return NotFound();

            var validStatuses = new[] { "pending", "submitted", "approved", "rejected", "paid" };
            if (!validStatuses.Contains(dto.Status))
                return BadRequest(Msg(lang, "حالة غير صحيحة", "Invalid status"));

            claim.Status = dto.Status;
            if (dto.ApprovalNumber != null) claim.ApprovalNumber = dto.ApprovalNumber;
            if (dto.RejectionReason != null) claim.RejectionReason = dto.RejectionReason;
            if (dto.Notes != null) claim.Notes = dto.Notes;

            if (dto.Status == "submitted") claim.SubmittedAt = DateTime.UtcNow;
            if (dto.Status == "approved") claim.ApprovedAt = DateTime.UtcNow;
            if (dto.Status == "paid") claim.PaidAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Ok(new { message = Msg(lang, "تم تحديث حالة المطالبة", "Claim status updated") });
        }

        // GET: api/insurance/claims/stats
        [HttpGet("claims/stats")]
        public async Task<ActionResult> GetClaimsStats()
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var claims = await _db.InsuranceClaims
                .Where(c => c.ClinicId == _clinicContext.ClinicId)
                .ToListAsync();

            return Ok(new
            {
                total = claims.Count,
                pending = claims.Count(c => c.Status == "pending"),
                submitted = claims.Count(c => c.Status == "submitted"),
                approved = claims.Count(c => c.Status == "approved"),
                rejected = claims.Count(c => c.Status == "rejected"),
                paid = claims.Count(c => c.Status == "paid"),
                totalAmount = claims.Sum(c => c.TotalAmount),
                insuranceAmount = claims.Sum(c => c.InsuranceAmount),
                patientAmount = claims.Sum(c => c.PatientAmount),
                pendingAmount = claims.Where(c => c.Status == "pending" || c.Status == "submitted").Sum(c => c.InsuranceAmount),
                paidAmount = claims.Where(c => c.Status == "paid").Sum(c => c.InsuranceAmount),
            });
        }
    }

    // ════════════════════════════════════════
    // DTOs
    // ════════════════════════════════════════
    public class CreateInsuranceCompanyDto
    {
        public string Name { get; set; } = "";
        public string? NameEn { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? ContactName { get; set; }
        public decimal CoverageRate { get; set; } = 80;
        public bool IsActive { get; set; } = true;
    }

    public class CreatePatientInsuranceDto
    {
        public Guid PatientId { get; set; }
        public Guid InsuranceCompanyId { get; set; }
        public string PolicyNumber { get; set; } = "";
        public string? MembershipNumber { get; set; }
        public decimal? CoverageRate { get; set; }
        public decimal? MaxCoverageAmount { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsPrimary { get; set; } = true;
        public string? Notes { get; set; }
    }

    public class CreateInsuranceClaimDto
    {
        public Guid PatientInsuranceId { get; set; }
        public Guid? AppointmentId { get; set; }
        public decimal TotalAmount { get; set; }
        public string? ApprovalNumber { get; set; }
        public string? Notes { get; set; }
        public DateTime ServiceDate { get; set; }
    }

    public class UpdateClaimStatusDto
    {
        public string Status { get; set; } = "";
        public string? ApprovalNumber { get; set; }
        public string? RejectionReason { get; set; }
        public string? Notes { get; set; }
    }
}