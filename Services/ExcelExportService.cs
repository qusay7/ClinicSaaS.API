using ClosedXML.Excel;

namespace ClinicSaaS.API.Services
{
    /// <summary>
    /// خدمة توليد Excel موحّدة — نفس فكرة PdfExportService بس لملفات .xlsx
    /// </summary>
    public interface IExcelExportService
    {
        byte[] GenerateTableReport(ExcelReportRequest request);
    }

    public class ExcelReportRequest
    {
        public string SheetName { get; set; } = "Report";
        public string Title { get; set; } = "";
        public List<string> Columns { get; set; } = new();
        public List<List<string>> Rows { get; set; } = new();
        public List<(string Label, string Value)>? SummaryLines { get; set; }
        public bool IsRtl { get; set; } = true;
    }

    public class ExcelExportService : IExcelExportService
    {
        public byte[] GenerateTableReport(ExcelReportRequest req)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add(req.SheetName);
            sheet.RightToLeft = req.IsRtl;

            // العنوان
            sheet.Cell(1, 1).Value = req.Title;
            sheet.Range(1, 1, 1, Math.Max(req.Columns.Count, 1)).Merge();
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 14;
            sheet.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#5B8C8F");

            // رؤوس الأعمدة
            const int headerRow = 3;
            for (int i = 0; i < req.Columns.Count; i++)
            {
                var cell = sheet.Cell(headerRow, i + 1);
                cell.Value = req.Columns[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F0F0");
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }

            // البيانات
            for (int r = 0; r < req.Rows.Count; r++)
            {
                for (int c = 0; c < req.Rows[r].Count; c++)
                {
                    sheet.Cell(headerRow + 1 + r, c + 1).Value = req.Rows[r][c] ?? "—";
                }
            }

            sheet.Columns().AdjustToContents();

            // ملخص (لو موجود) — بعد الجدول
            if (req.SummaryLines != null && req.SummaryLines.Count > 0)
            {
                var summaryStartRow = headerRow + req.Rows.Count + 3;
                foreach (var (label, value) in req.SummaryLines)
                {
                    sheet.Cell(summaryStartRow, 1).Value = label;
                    sheet.Cell(summaryStartRow, 1).Style.Font.Bold = true;
                    sheet.Cell(summaryStartRow, 2).Value = value;
                    summaryStartRow++;
                }
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}