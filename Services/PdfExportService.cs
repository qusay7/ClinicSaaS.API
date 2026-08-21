using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ClinicSaaS.API.Services
{
    /// <summary>
    /// خدمة توليد PDF موحّدة — تُستخدم لكل التقارير بالنظام (تسويات، ذمم، فواتير...)
    /// بدل ما يبني كل كونترولر تنسيقه الخاص. تدعم عربي/إنجليزي واتجاه RTL/LTR.
    /// </summary>
    public interface IPdfExportService
    {
        byte[] GenerateTableReport(PdfReportRequest request);
    }

    public class PdfReportRequest
    {
        public string Title { get; set; } = "";
        public string? Subtitle { get; set; }
        public string ClinicName { get; set; } = "";
        public string? LogoPath { get; set; }          // مسار فعلي على القرص (اختياري)
        public bool IsRtl { get; set; } = true;
        public List<string> Columns { get; set; } = new();
        public List<List<string>> Rows { get; set; } = new();
        public List<(string Label, string Value)>? SummaryLines { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class PdfExportService : IPdfExportService
    {
        public PdfExportService()
        {
            // ✅ ترخيص المجتمع المجاني (Community) — مناسب للشركات الصغيرة
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public byte[] GenerateTableReport(PdfReportRequest req)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(10));
                    if (req.IsRtl) page.ContentFromRightToLeft();

                    // ── Header ──────────────────────────────────────────
                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(req.ClinicName).FontSize(16).Bold().FontColor("#2C3E3F");
                                c.Item().Text(req.Title).FontSize(20).Bold().FontColor("#5B8C8F");
                                if (!string.IsNullOrEmpty(req.Subtitle))
                                    c.Item().Text(req.Subtitle).FontSize(10).FontColor("#6B8A8C");
                            });

                            if (!string.IsNullOrEmpty(req.LogoPath) && File.Exists(req.LogoPath))
                            {
                                row.ConstantItem(70).Height(70).Image(req.LogoPath).FitArea();
                            }
                        });

                        col.Item().PaddingTop(8).LineHorizontal(1).LineColor("#DCE5E5");
                        col.Item().PaddingTop(4).Text(
                            $"{(req.IsRtl ? "تاريخ التقرير" : "Generated on")}: {req.GeneratedAt:yyyy-MM-dd HH:mm}"
                        ).FontSize(8).FontColor("#8BAFB1");
                    });

                    // ── Content ─────────────────────────────────────────
                    page.Content().PaddingTop(16).Column(col =>
                    {
                        // جدول البيانات
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                foreach (var _ in req.Columns) columns.RelativeColumn();
                            });

                            table.Header(header =>
                            {
                                foreach (var colName in req.Columns)
                                {
                                    header.Cell().Background("#E8F0F0").Padding(6)
                                        .Text(colName).Bold().FontSize(9).FontColor("#2C3E3F");
                                }
                            });

                            foreach (var (row, idx) in req.Rows.Select((r, i) => (r, i)))
                            {
                                var bg = idx % 2 == 0 ? "#FFFFFF" : "#F8FAFA";
                                foreach (var cell in row)
                                {
                                    table.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#DCE5E5")
                                        .Padding(6).Text(cell ?? "—").FontSize(9);
                                }
                            }
                        });

                        // ملخص (لو موجود)
                        if (req.SummaryLines != null && req.SummaryLines.Count > 0)
                        {
                            col.Item().PaddingTop(16).Background("#E8F0F0").Padding(12).Column(sc =>
                            {
                                foreach (var (label, value) in req.SummaryLines)
                                {
                                    sc.Item().Row(r =>
                                    {
                                        r.RelativeItem().Text(label).FontSize(10).FontColor("#6B8A8C");
                                        r.ConstantItem(120).AlignRight().Text(value).FontSize(11).Bold().FontColor("#5B8C8F");
                                    });
                                }
                            });
                        }
                    });

                    // ── Footer ──────────────────────────────────────────
                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span(req.IsRtl ? "صفحة " : "Page ").FontSize(8).FontColor("#8BAFB1");
                        text.CurrentPageNumber().FontSize(8).FontColor("#8BAFB1");
                        text.Span(" / ").FontSize(8).FontColor("#8BAFB1");
                        text.TotalPages().FontSize(8).FontColor("#8BAFB1");
                    });
                });
            });

            return document.GeneratePdf();
        }
    }
}