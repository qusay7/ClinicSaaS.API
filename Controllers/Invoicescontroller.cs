using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using ClinicSaaS.API.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [RequireActiveSubscription]
    public class InvoicesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClinicContext _clinicContext;
        private readonly IInvoiceXmlBuilder _xmlBuilder;
        private readonly IJoFotaraService _joFotara;
        private readonly IPdfExportService _pdfExport;
        private readonly IExcelExportService _excelExport;

        public InvoicesController(
            ApplicationDbContext db,
            IClinicContext clinicContext,
            IInvoiceXmlBuilder xmlBuilder,
            IJoFotaraService joFotara,
            IPdfExportService pdfExport,
            IExcelExportService excelExport)
        {
            _db = db;
            _clinicContext = clinicContext;
            _xmlBuilder = xmlBuilder;
            _joFotara = joFotara;
            _pdfExport = pdfExport;
            _excelExport = excelExport;
        }

        private static string Msg(string lang, string ar, string en) => lang == "ar" ? ar : en;

        // ✅ نمط الضريبة — نفس ترميز TBL134
        private static string TaxTypeOf(int taxMethod) => taxMethod switch
        {
            1 => "S",
            3 => "Z",
            _ => "O",
        };

        // ✅ رقم تسلسلي لكل عيادة
        private async Task<string> NextInvoiceNumber(Clinic clinic)
        {
            clinic.LastInvoiceNumber++;
            await _db.SaveChangesAsync();
            return clinic.LastInvoiceNumber.ToString("D6");
        }

        // ═══════════════════════════════════════
        // GET: api/invoices?documentType=&isSubmitted=&from=&to=
        // ═══════════════════════════════════════
        [HttpGet]
        public async Task<ActionResult> GetAll(
            [FromQuery] string? documentType,
            [FromQuery] bool? isSubmitted,
            [FromQuery] string? from,
            [FromQuery] string? to,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var query = _db.Invoices
                .Include(i => i.Patient)
                .Where(i => i.ClinicId == _clinicContext.ClinicId);

            if (!string.IsNullOrEmpty(documentType))
                query = query.Where(i => i.DocumentType == documentType);
            if (isSubmitted.HasValue)
                query = query.Where(i => i.IsSubmitted == isSubmitted);
            if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fromDate))
                query = query.Where(i => i.IssueDate >= fromDate);
            if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var toDate))
                query = query.Where(i => i.IssueDate < toDate.AddDays(1));

            var total = await query.CountAsync();

            var invoices = await query
                .OrderByDescending(i => i.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(i => new
                {
                    i.Id,
                    i.InvoiceNumber,
                    i.DocumentType,
                    patientName = i.Patient != null ? i.Patient.FullName : "—",
                    issueDate = i.IssueDate.ToString("yyyy-MM-dd"),
                    i.TotalAmount,
                    i.DiscountAmount,
                    i.TaxAmount,
                    i.PayableAmount,
                    i.TaxMethod,
                    i.TaxRate,
                    i.IsSubmitted,
                    submittedAt = i.SubmittedAt.HasValue ? i.SubmittedAt.Value.ToString("yyyy-MM-dd HH:mm") : null,
                    i.InvoiceXml,
                    i.TaxResponse,
                    i.QrCode,
                    i.SourceInvoiceId,
                    i.PaymentDetailId,
                    i.Notes,
                })
                .ToListAsync();

            return Ok(new { total, page, pageSize, pages = (int)Math.Ceiling((double)total / pageSize), invoices });
        }
        // ═══════════════════════════════════════
        // GET: api/invoices/export?format=pdf|excel
        // ═══════════════════════════════════════
        [HttpGet("export")]
        public async Task<ActionResult> Export(
            [FromQuery] string? documentType, [FromQuery] bool? isSubmitted,
            [FromQuery] string? from, [FromQuery] string? to,
            [FromQuery] string format = "pdf", [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();
            var isRtl = lang == "ar";

            var query = _db.Invoices.Include(i => i.Patient)
                .Where(i => i.ClinicId == _clinicContext.ClinicId);

            if (!string.IsNullOrEmpty(documentType)) query = query.Where(i => i.DocumentType == documentType);
            if (isSubmitted.HasValue) query = query.Where(i => i.IsSubmitted == isSubmitted);
            if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var f)) query = query.Where(i => i.IssueDate >= f);
            if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var tt)) query = query.Where(i => i.IssueDate < tt.AddDays(1));

            var invoices = await query.OrderByDescending(i => i.CreatedAt).ToListAsync();

            var rows = invoices.Select(i => new List<string> {
                i.InvoiceNumber,
                i.DocumentType == "381" ? (isRtl ? "مرتجع" : "Credit Note") : (isRtl ? "فاتورة بيع" : "Sales Invoice"),
                i.Patient?.FullName ?? "—",
                i.IssueDate.ToString("yyyy-MM-dd"),
                i.TotalAmount.ToString("F2"),
                i.TaxAmount.ToString("F2"),
                i.PayableAmount.ToString("F2"),
                i.IsSubmitted ? (isRtl ? "مُرحّلة" : "Submitted") : (isRtl ? "غير مُرحّلة" : "Not submitted"),
            }).ToList();

            var columns = isRtl
                ? new List<string> { "رقم الفاتورة", "النوع", "المريض", "التاريخ", "الإجمالي", "الضريبة", "المستحق", "حالة الترحيل" }
                : new List<string> { "Invoice #", "Type", "Patient", "Date", "Total", "Tax", "Payable", "Submission" };

            var clinic = await _db.Clinics.FindAsync(_clinicContext.ClinicId.Value);
            var summary = new List<(string, string)> {
                (isRtl ? "عدد الفواتير" : "Invoices", invoices.Count.ToString()),
                (isRtl ? "مُرحّلة" : "Submitted", invoices.Count(i => i.IsSubmitted).ToString()),
                (isRtl ? "إجمالي المستحق" : "Total Payable", invoices.Sum(i => i.PayableAmount).ToString("F2")),
            };

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "الفواتير" : "Invoices",
                    Title = isRtl ? "قائمة الفواتير" : "Invoices List",
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "invoices.xlsx");
            }

            var pdf = _pdfExport.GenerateTableReport(new PdfReportRequest
            {
                Title = isRtl ? "قائمة الفواتير" : "Invoices List",
                ClinicName = clinic?.Name ?? "",
                IsRtl = isRtl,
                Columns = columns,
                Rows = rows,
                SummaryLines = summary,
            });
            return File(pdf, "application/pdf", "invoices.pdf");
        }
        // ═══════════════════════════════════════
        // GET: api/invoices/{id}
        // ═══════════════════════════════════════
        [HttpGet("{id}")]
        public async Task<ActionResult> GetById(Guid id)
        {
            var invoice = await _db.Invoices
                .Include(i => i.Patient)
                .Include(i => i.Items)
                .Include(i => i.SourceInvoice)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (invoice == null) return NotFound();
            if (!_clinicContext.IsSuperAdmin && invoice.ClinicId != _clinicContext.ClinicId) return Forbid();

            return Ok(new
            {
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.DocumentType,
                patientName = invoice.Patient?.FullName,
                issueDate = invoice.IssueDate.ToString("yyyy-MM-dd"),
                invoice.TotalAmount,
                invoice.DiscountAmount,
                invoice.TaxAmount,
                invoice.PayableAmount,
                invoice.TaxMethod,
                invoice.TaxRate,
                invoice.Notes,
                invoice.IsSubmitted,
                submittedAt = invoice.SubmittedAt?.ToString("yyyy-MM-dd HH:mm"),
                invoice.InvoiceXml,
                invoice.TaxResponse,
                invoice.QrCode,
                invoice.SourceInvoiceId,
                sourceInvoiceNumber = invoice.SourceInvoice?.InvoiceNumber,
                invoice.PaymentDetailId,
                items = invoice.Items.Select(it => new
                {
                    it.Id,
                    it.Name,
                    it.TemplateId,
                    it.SourceItemId,
                    it.Quantity,
                    it.UnitPrice,
                    it.Discount,
                    it.TaxRate,
                    it.TaxAmount,
                    it.TaxType,
                }),
            });
        }

        // ═══════════════════════════════════════
        // GET: api/invoices/uninvoiced-payments
        // الدفعات اللي ما انعمل لها فاتورة بيع بعد
        // ═══════════════════════════════════════
        [HttpGet("uninvoiced-payments")]
        public async Task<ActionResult> GetUninvoicedPayments()
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var invoicedIds = await _db.Invoices
                .Where(i => i.ClinicId == _clinicContext.ClinicId && i.DocumentType == "388" && i.PaymentDetailId != null)
                .Select(i => i.PaymentDetailId!.Value)
                .ToListAsync();

            var payments = await _db.PaymentDetails
                .Include(p => p.Patient)
                .Where(p => p.ClinicId == _clinicContext.ClinicId && !invoicedIds.Contains(p.Id))
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    p.Id,
                    patientName = p.Patient != null ? p.Patient.FullName : "—",
                    p.TotalAmount,
                    p.AmountPaid,
                    p.IsPaid,
                    createdAt = p.CreatedAt.ToString("yyyy-MM-dd"),
                })
                .ToListAsync();

            return Ok(payments);
        }
        // ═══════════════════════════════════════
        // POST: api/invoices/from-payment
        // إنشاء فاتورة بيع (388) من دفعة — البنود تُنسخ من أنواع الزيارة الفعلية
        // ═══════════════════════════════════════
        [HttpPost("from-payment")]
        public async Task<ActionResult> CreateFromPayment([FromBody] CreateInvoiceDto dto, [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var clinic = await _db.Clinics.FindAsync(_clinicContext.ClinicId.Value);
            if (clinic == null) return Unauthorized();

            var payment = await _db.PaymentDetails
                .FirstOrDefaultAsync(p => p.Id == dto.PaymentDetailId && p.ClinicId == clinic.Id);
            if (payment == null)
                return NotFound(Msg(lang, "الدفعة غير موجودة", "Payment not found"));

            var already = await _db.Invoices.AnyAsync(i =>
                i.PaymentDetailId == payment.Id && i.DocumentType == "388");
            if (already)
                return BadRequest(Msg(lang, "تم إنشاء فاتورة لهذه الدفعة مسبقاً", "An invoice already exists for this payment"));

            var visitTypes = await _db.AppointmentVisitTypes
                .Where(v => v.AppointmentId == payment.AppointmentId)
                .Include(v => v.Template)
                .ToListAsync();

            var taxMethod = dto.TaxMethod ?? clinic.DefaultTaxMethod;
            var taxRate = taxMethod == 1 ? (dto.TaxRate ?? clinic.DefaultTaxRate) : 0m;
            var taxType = TaxTypeOf(taxMethod);

            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                ClinicId = clinic.Id,
                PatientId = payment.PatientId,
                InvoiceNumber = await NextInvoiceNumber(clinic),
                DocumentType = "388",
                IssueDate = payment.PaidAt ?? payment.CreatedAt,
                PaymentDetailId = payment.Id,
                TaxMethod = taxMethod,
                TaxRate = taxRate,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow,
            };

            // ✅ كل بند زيارة = سطر فاتورة، وحصة التأمين تُعامل كخصم
            if (visitTypes.Count > 0)
            {
                foreach (var v in visitTypes)
                {
                    var net = v.Price - v.InsuranceAmount;
                    invoice.Items.Add(new InvoiceItem
                    {
                        Id = Guid.NewGuid(),
                        InvoiceId = invoice.Id,
                        TemplateId = v.TemplateId,
                        Name = v.Template?.Name ?? "",
                        Quantity = 1,
                        UnitPrice = v.Price,
                        Discount = v.InsuranceAmount,
                        TaxRate = taxRate,
                        TaxAmount = Math.Round(net * taxRate / 100m, 9),
                        TaxType = taxType,
                        CreatedAt = DateTime.UtcNow,
                    });
                }
            }
            else
            {
                // ما فيه بنود مسجّلة — سطر واحد بإجمالي الدفعة
                var net = payment.TotalAmount - payment.InsuranceAmount;
                invoice.Items.Add(new InvoiceItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    Name = Msg(lang, "خدمة طبية", "Medical service"),
                    Quantity = 1,
                    UnitPrice = payment.TotalAmount,
                    Discount = payment.InsuranceAmount,
                    TaxRate = taxRate,
                    TaxAmount = Math.Round(net * taxRate / 100m, 9),
                    TaxType = taxType,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            ApplyTotals(invoice);

            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync();

            return Ok(new { invoice.Id, invoice.InvoiceNumber, invoice.PayableAmount });
        }

        // ═══════════════════════════════════════
        // POST: api/invoices/return
        // إنشاء مرتجع (381) من فاتورة بيع — كل بند مربوط ببند الفاتورة الأصلية
        // ═══════════════════════════════════════
        [HttpPost("return")]
        public async Task<ActionResult> CreateReturn([FromBody] CreateReturnDto dto, [FromQuery] string lang = "ar")
        {
            if (_clinicContext.ClinicId == null) return Unauthorized();

            var clinic = await _db.Clinics.FindAsync(_clinicContext.ClinicId.Value);
            if (clinic == null) return Unauthorized();

            var source = await _db.Invoices
                .Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.Id == dto.SourceInvoiceId && i.ClinicId == clinic.Id);

            if (source == null)
                return NotFound(Msg(lang, "فاتورة البيع غير موجودة", "Source invoice not found"));
            if (source.DocumentType != "388")
                return BadRequest(Msg(lang, "المرتجع يجب أن يكون على فاتورة بيع", "A return must reference a sales invoice"));

            var items = dto.Items ?? new List<ReturnItemDto>();
            if (items.Count == 0)
                return BadRequest(Msg(lang, "لا توجد بنود للإرجاع", "No items to return"));

            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                ClinicId = clinic.Id,
                PatientId = source.PatientId,
                InvoiceNumber = await NextInvoiceNumber(clinic),
                DocumentType = "381",
                IssueDate = DateTime.UtcNow,
                SourceInvoiceId = source.Id,
                TaxMethod = source.TaxMethod,
                TaxRate = source.TaxRate,
                Notes = dto.Reason,
                CreatedAt = DateTime.UtcNow,
            };

            foreach (var it in items)
            {
                var sourceItem = source.Items.FirstOrDefault(x => x.Id == it.SourceItemId);
                if (sourceItem == null)
                    return BadRequest(Msg(lang, "أحد البنود لا ينتمي لفاتورة البيع", "One of the items does not belong to the source invoice"));

                var qty = it.Quantity > 0 ? it.Quantity : sourceItem.Quantity;
                if (qty > sourceItem.Quantity)
                    return BadRequest(Msg(lang, "الكمية المرتجعة أكبر من المباعة", "Returned quantity exceeds the sold quantity"));

                // ✅ الخصم يُقسَّم بنسبة الكمية المرتجعة من الأصل
                var discount = sourceItem.Quantity > 0
                    ? Math.Round(sourceItem.Discount * qty / sourceItem.Quantity, 9)
                    : 0m;
                var net = sourceItem.UnitPrice * qty - discount;

                invoice.Items.Add(new InvoiceItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    TemplateId = sourceItem.TemplateId,
                    SourceItemId = sourceItem.Id,
                    Name = sourceItem.Name,
                    Quantity = qty,
                    UnitPrice = sourceItem.UnitPrice,
                    Discount = discount,
                    TaxRate = sourceItem.TaxRate,
                    TaxAmount = Math.Round(net * sourceItem.TaxRate / 100m, 9),
                    TaxType = sourceItem.TaxType,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            ApplyTotals(invoice);

            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync();

            return Ok(new { invoice.Id, invoice.InvoiceNumber, invoice.PayableAmount });
        }
        // ═══════════════════════════════════════
        // GET: api/invoices/{id}/export — فاتورة مفردة PDF
        // ═══════════════════════════════════════
        [HttpGet("{id}/export")]
        public async Task<ActionResult> ExportOne(Guid id, [FromQuery] string format = "pdf", [FromQuery] string lang = "ar")
        {
            var invoice = await _db.Invoices
                .Include(i => i.Patient).Include(i => i.Items).Include(i => i.SourceInvoice)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (invoice == null) return NotFound();
            if (!_clinicContext.IsSuperAdmin && invoice.ClinicId != _clinicContext.ClinicId) return Forbid();

            var clinic = await _db.Clinics.FindAsync(invoice.ClinicId);
            var isRtl = lang == "ar";
            var isReturn = invoice.DocumentType == "381";

            var columns = isRtl
                ? new List<string> { "البند", "الكمية", "السعر", "الخصم", "الضريبة" }
                : new List<string> { "Item", "Qty", "Price", "Discount", "Tax" };

            var rows = invoice.Items.Select(it => new List<string> {
        it.Name,
        it.Quantity.ToString("F0"),
        it.UnitPrice.ToString("F2"),
        it.Discount.ToString("F2"),
        it.TaxAmount.ToString("F2"),
    }).ToList();

            var summary = new List<(string, string)> {
        (isRtl ? "رقم الفاتورة" : "Invoice #", invoice.InvoiceNumber),
        (isRtl ? "التاريخ" : "Date", invoice.IssueDate.ToString("yyyy-MM-dd")),
        (isRtl ? "المريض" : "Patient", invoice.Patient?.FullName ?? "—"),
        (isRtl ? "الإجمالي" : "Total", invoice.TotalAmount.ToString("F2")),
        (isRtl ? "الخصم" : "Discount", invoice.DiscountAmount.ToString("F2")),
        (isRtl ? "الضريبة" : "Tax", invoice.TaxAmount.ToString("F2")),
        (isRtl ? "المستحق" : "Payable", invoice.PayableAmount.ToString("F2")),
    };

            if (isReturn)
                summary.Insert(1, (isRtl ? "فاتورة البيع" : "Source Invoice", invoice.SourceInvoice?.InvoiceNumber ?? "—"));
            if (!string.IsNullOrWhiteSpace(invoice.QrCode))
                summary.Add(("QR", invoice.QrCode));

            var title = isReturn
                ? (isRtl ? "إشعار دائن (مرتجع)" : "Credit Note")
                : (isRtl ? "فاتورة بيع" : "Sales Invoice");

            if (format == "excel")
            {
                var bytes = _excelExport.GenerateTableReport(new ExcelReportRequest
                {
                    SheetName = isRtl ? "فاتورة" : "Invoice",
                    Title = $"{title} — {invoice.InvoiceNumber}",
                    Columns = columns,
                    Rows = rows,
                    SummaryLines = summary,
                    IsRtl = isRtl,
                });
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"invoice-{invoice.InvoiceNumber}.xlsx");
            }
            var pdf = _pdfExport.GenerateTableReport(new PdfReportRequest
            {
                Title = $"{title} — {invoice.InvoiceNumber}",
                ClinicName = clinic?.Name ?? "",
                IsRtl = isRtl,
                Columns = columns,
                Rows = rows,
                SummaryLines = summary,
            });

            return File(pdf, "application/pdf", $"invoice-{invoice.InvoiceNumber}.pdf");
        }
        // ═══════════════════════════════════════
        // POST: api/invoices/{id}/submit
        // ترحيل الفاتورة لدائرة ضريبة الدخل والمبيعات (JoFotara)
        // ═══════════════════════════════════════
        [HttpPost("{id}/submit")]
        public async Task<ActionResult> Submit(Guid id, [FromQuery] string lang = "ar")
        {
            var invoice = await _db.Invoices
                .Include(i => i.Items)
                .Include(i => i.Patient)
                .Include(i => i.SourceInvoice)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (invoice == null) return NotFound();
            if (!_clinicContext.IsSuperAdmin && invoice.ClinicId != _clinicContext.ClinicId) return Forbid();
            if (invoice.IsSubmitted)
                return BadRequest(Msg(lang, "الفاتورة مُرحّلة مسبقاً", "Invoice already submitted"));

            // ✅ الفواتير الإلكترونية ميزة خاصة بخطط معينة فقط
            if (!_clinicContext.IsSuperAdmin && !_clinicContext.HasElectronicInvoicing)
                return StatusCode(StatusCodes.Status402PaymentRequired, new
                {
                    code = "FEATURE_NOT_IN_PLAN",
                    message = Msg(lang,
                        "ترحيل الفواتير الإلكترونية غير متوفر بخطتك الحالية — يرجى ترقية الخطة",
                        "Electronic invoice submission is not included in your current plan — please upgrade")
                });

            var clinic = await _db.Clinics.FindAsync(invoice.ClinicId);
            if (clinic == null) return NotFound();

            if (string.IsNullOrWhiteSpace(clinic.InvoiceId) || string.IsNullOrWhiteSpace(clinic.InvoiceKey))
                return BadRequest(Msg(lang,
                    "بيانات الاعتماد الضريبية غير مُعرَّفة بإعدادات العيادة",
                    "Tax credentials are not configured in the clinic settings"));

            var xml = _xmlBuilder.Build(
                clinic, invoice,
                invoice.Patient?.FullName ?? "",
                invoice.SourceInvoice?.InvoiceNumber,
                invoice.SourceInvoice?.PayableAmount ?? 0);

            invoice.InvoiceXml = xml;

            var result = await _joFotara.SendInvoiceAsync(xml, clinic.InvoiceId!, clinic.InvoiceKey!);

            invoice.TaxResponse = result.ResponseText;
            invoice.QrCode = result.QrCode;

            // ✅ نعتبرها مُرحّلة فقط لو رجع QR فعلي — نفس منطق الإجراء المخزّن
            if (result.Success && !string.IsNullOrEmpty(result.QrCode))
            {
                invoice.IsSubmitted = true;
                invoice.SubmittedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                invoice.IsSubmitted,
                submittedAt = invoice.SubmittedAt?.ToString("yyyy-MM-dd HH:mm"),
                invoice.QrCode,
                statusCode = result.StatusCode,
                invoice.TaxResponse,
                message = invoice.IsSubmitted
                    ? Msg(lang, "تم الترحيل بنجاح ✅", "Submitted successfully ✅")
                    : Msg(lang, "فشل الترحيل — راجع رد الضريبة", "Submission failed — check the tax response"),
            });
        }

        // ✅ إجماليات الفاتورة من بنودها
        private static void ApplyTotals(Invoice invoice)
        {
            invoice.TotalAmount = invoice.Items.Sum(i => i.UnitPrice * i.Quantity);
            invoice.DiscountAmount = invoice.Items.Sum(i => i.Discount);
            invoice.TaxAmount = invoice.Items.Sum(i => i.TaxAmount);
            invoice.PayableAmount = invoice.TotalAmount - invoice.DiscountAmount + invoice.TaxAmount;
        }
    }

    // ═══════════════════════════════════════
    // DTOs
    // ═══════════════════════════════════════
    public class CreateInvoiceDto
    {
        public Guid PaymentDetailId { get; set; }
        public int? TaxMethod { get; set; }
        public decimal? TaxRate { get; set; }
        public string? Notes { get; set; }
    }

    public class CreateReturnDto
    {
        public Guid SourceInvoiceId { get; set; }
        public string? Reason { get; set; }
        public List<ReturnItemDto>? Items { get; set; }
    }

    public class ReturnItemDto
    {
        public Guid SourceItemId { get; set; }
        public decimal Quantity { get; set; }
    }
}