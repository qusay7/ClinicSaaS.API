using System.Globalization;
using System.Text;
using ClinicSaaS.API.Data;

namespace ClinicSaaS.API.Services
{
    public interface IInvoiceXmlBuilder
    {
        string Build(Clinic clinic, Invoice invoice, string patientName, string? sourceInvoiceNumber = null, decimal sourceInvoiceTotal = 0);
    }

    public class InvoiceXmlBuilder : IInvoiceXmlBuilder
    {
        private const string Cur = "JO";

        private static string F(decimal v) => v.ToString("F9", CultureInfo.InvariantCulture);

        // ✅ نفس التنضيف بالإجراء المخزّن — يمنع كسر الـ XML بأي رمز داخل الأسماء
        private static string Esc(string? s) => string.IsNullOrEmpty(s) ? "" :
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
             .Replace("\"", "&quot;").Replace("'", "&apos;");

        // ✅ نمط الضريبة — نفس ترميز TBL134
        // 1 خاضع (S) | 2 غير خاضع (O) | 3 معفي (Z) | 4 تصدير (O) | 5 خاضع بنسبة صفر (O)
        private static string TaxTypeOf(int taxMethod) => taxMethod switch
        {
            1 => "S",
            3 => "Z",
            _ => "O",
        };

        public string Build(Clinic clinic, Invoice invoice, string patientName,
            string? sourceInvoiceNumber = null, decimal sourceInvoiceTotal = 0)
        {
            var isReturn = invoice.DocumentType == "381";
            var items = invoice.Items.ToList();

            var taxType = TaxTypeOf(invoice.TaxMethod);
            var rate = invoice.TaxMethod == 1 ? invoice.TaxRate : 0m;

            var isSales = clinic.TaxRegistrationType == "sales";
            // نقدي (012/011) أو ذمم (022/021) — والتصدير له ترميزه الخاص
            var isCash = invoice.PayableAmount <= 0 || invoice.TaxMethod != 4;
            var payMethodCode = invoice.TaxMethod == 4
                ? (isCash ? "112" : "122")
                : isSales ? (isCash ? "012" : "022") : (isCash ? "011" : "021");

            var taxExclusive = items.Sum(i => i.UnitPrice * i.Quantity);
            var discountTotal = items.Sum(i => i.Discount);
            var taxAmount = items.Sum(i => i.TaxAmount);
            var taxInclusive = taxExclusive - discountTotal + taxAmount;

            var lines = new StringBuilder();
            foreach (var it in items)
            {
                var lineNet = it.UnitPrice * it.Quantity - it.Discount;
                var lineWithTax = lineNet + it.TaxAmount;

                lines.Append("<cac:InvoiceLine>")
                     .Append($"<cbc:ID>{it.Id}</cbc:ID>")
                     .Append($"<cbc:InvoicedQuantity unitCode=\"PCE\">{F(it.Quantity)}</cbc:InvoicedQuantity>")
                     .Append($"<cbc:LineExtensionAmount currencyID=\"{Cur}\">{F(lineNet)}</cbc:LineExtensionAmount>")
                     .Append("<cac:TaxTotal>")
                       .Append($"<cbc:TaxAmount currencyID=\"{Cur}\">{F(it.TaxAmount)}</cbc:TaxAmount>")
                       .Append($"<cbc:RoundingAmount currencyID=\"{Cur}\">{F(lineWithTax)}</cbc:RoundingAmount>")
                       .Append("<cac:TaxSubtotal>")
                         .Append(isReturn ? $"<cbc:TaxableAmount currencyID=\"{Cur}\">{F(lineNet)}</cbc:TaxableAmount>" : "")
                         .Append($"<cbc:TaxAmount currencyID=\"{Cur}\">{F(it.TaxAmount)}</cbc:TaxAmount>")
                         .Append("<cac:TaxCategory>")
                           .Append($"<cbc:ID schemeAgencyID=\"6\" schemeID=\"UN/ECE 5305\">{it.TaxType}</cbc:ID>")
                           .Append($"<cbc:Percent>{F(it.TaxRate)}</cbc:Percent>")
                           .Append("<cac:TaxScheme><cbc:ID schemeAgencyID=\"6\" schemeID=\"UN/ECE 5153\">VAT</cbc:ID></cac:TaxScheme>")
                         .Append("</cac:TaxCategory>")
                       .Append("</cac:TaxSubtotal>")
                     .Append("</cac:TaxTotal>")
                     .Append($"<cac:Item><cbc:Name>{Esc(it.Name)}</cbc:Name></cac:Item>")
                     .Append("<cac:Price>")
                       .Append($"<cbc:PriceAmount currencyID=\"{Cur}\">{F(it.UnitPrice)}</cbc:PriceAmount>")
                       // ✅ المرتجع يتطلب BaseQuantity صراحة
                       .Append(isReturn ? "<cbc:BaseQuantity unitCode=\"C62\">1</cbc:BaseQuantity>" : "")
                       .Append("<cac:AllowanceCharge>")
                         .Append("<cbc:ChargeIndicator>false</cbc:ChargeIndicator>")
                         .Append("<cbc:AllowanceChargeReason>DISCOUNT</cbc:AllowanceChargeReason>")
                         .Append($"<cbc:Amount currencyID=\"{Cur}\">{F(it.Discount)}</cbc:Amount>")
                       .Append("</cac:AllowanceCharge>")
                     .Append("</cac:Price>")
                     .Append("</cac:InvoiceLine>");
            }

            var xml = new StringBuilder();
            xml.Append("<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\" ")
               .Append("xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\" ")
               .Append("xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\" ")
               .Append("xmlns:ext=\"urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2\">")
               .Append("<cbc:ProfileID>reporting:1.0</cbc:ProfileID>")
               .Append($"<cbc:ID>{Esc(invoice.InvoiceNumber)}</cbc:ID>")
               .Append($"<cbc:UUID>{invoice.Id}</cbc:UUID>")
               .Append($"<cbc:IssueDate>{invoice.IssueDate:yyyy-MM-dd}</cbc:IssueDate>")
               .Append($"<cbc:InvoiceTypeCode name=\"{payMethodCode}\">{invoice.DocumentType}</cbc:InvoiceTypeCode>");

            // ✅ فاتورة البيع فيها Note بالترويسة، أما المرتجع فسببه يظهر بـ PaymentMeans
            if (!isReturn)
                xml.Append($"<cbc:Note>{Esc(invoice.Notes)}</cbc:Note>");

            xml.Append("<cbc:DocumentCurrencyCode>JOD</cbc:DocumentCurrencyCode>")
               .Append("<cbc:TaxCurrencyCode>JOD</cbc:TaxCurrencyCode>");

            // ✅ المرتجع يشير للفاتورة الأصلية
            if (isReturn)
            {
                xml.Append("<cac:BillingReference><cac:InvoiceDocumentReference>")
                   .Append($"<cbc:ID>{Esc(sourceInvoiceNumber)}</cbc:ID>")
                   .Append($"<cbc:UUID>{invoice.SourceInvoiceId}</cbc:UUID>")
                   .Append($"<cbc:DocumentDescription>{F(sourceInvoiceTotal)}</cbc:DocumentDescription>")
                   .Append("</cac:InvoiceDocumentReference></cac:BillingReference>");
            }

            xml.Append("<cac:AdditionalDocumentReference>")
               .Append("<cbc:ID>ICV</cbc:ID>")
               .Append($"<cbc:UUID>{invoice.Id}</cbc:UUID>")
               .Append("</cac:AdditionalDocumentReference>")
               .Append("<cac:AccountingSupplierParty><cac:Party>")
                 .Append("<cac:PostalAddress><cac:Country><cbc:IdentificationCode>JO</cbc:IdentificationCode></cac:Country></cac:PostalAddress>")
                 .Append($"<cac:PartyTaxScheme><cbc:CompanyID>{Esc(clinic.TaxNumber)}</cbc:CompanyID><cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme></cac:PartyTaxScheme>")
                 .Append($"<cac:PartyLegalEntity><cbc:RegistrationName>{Esc(clinic.Name)}</cbc:RegistrationName></cac:PartyLegalEntity>")
               .Append("</cac:Party></cac:AccountingSupplierParty>")
               .Append("<cac:AccountingCustomerParty><cac:Party>")
                 .Append("<cac:PartyIdentification><cbc:ID schemeID=\"TN\"></cbc:ID></cac:PartyIdentification>")
                 .Append("<cac:PostalAddress><cbc:PostalZone></cbc:PostalZone><cbc:CountrySubentityCode></cbc:CountrySubentityCode>")
                 .Append("<cac:Country><cbc:IdentificationCode>JO</cbc:IdentificationCode></cac:Country></cac:PostalAddress>")
                 .Append($"<cac:PartyTaxScheme><cbc:CompanyID>{Esc(clinic.TaxNumber)}</cbc:CompanyID><cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme></cac:PartyTaxScheme>")
                 .Append($"<cac:PartyLegalEntity><cbc:RegistrationName>{Esc(patientName)}</cbc:RegistrationName></cac:PartyLegalEntity>")
               .Append("</cac:Party><cac:AccountingContact><cbc:Telephone></cbc:Telephone></cac:AccountingContact></cac:AccountingCustomerParty>")
               .Append($"<cac:SellerSupplierParty><cac:Party><cac:PartyIdentification><cbc:ID>{Esc(clinic.SourceNumber)}</cbc:ID></cac:PartyIdentification></cac:Party></cac:SellerSupplierParty>");

            // ✅ المرتجع يحمل سببه بـ PaymentMeans بكود 10
            if (isReturn)
            {
                xml.Append("<cac:PaymentMeans>")
                   .Append("<cbc:PaymentMeansCode listID=\"UN/ECE 4461\">10</cbc:PaymentMeansCode>")
                   .Append($"<cbc:InstructionNote>{Esc(invoice.Notes)}</cbc:InstructionNote>")
                   .Append("</cac:PaymentMeans>");
            }

            xml.Append("<cac:AllowanceCharge>")
                 .Append("<cbc:ChargeIndicator>false</cbc:ChargeIndicator>")
                 .Append("<cbc:AllowanceChargeReason>discount</cbc:AllowanceChargeReason>")
                 .Append($"<cbc:Amount currencyID=\"{Cur}\">{F(discountTotal)}</cbc:Amount>")
               .Append("</cac:AllowanceCharge>")
               .Append("<cac:TaxTotal>")
                 .Append($"<cbc:TaxAmount currencyID=\"{Cur}\">{F(taxAmount)}</cbc:TaxAmount>");

            // ✅ المرتجع يفصّل TaxSubtotal على مستوى الفاتورة كمان
            if (isReturn)
            {
                xml.Append("<cac:TaxSubtotal>")
                     .Append($"<cbc:TaxableAmount currencyID=\"{Cur}\">{F(taxExclusive - discountTotal)}</cbc:TaxableAmount>")
                     .Append($"<cbc:TaxAmount currencyID=\"{Cur}\">{F(taxAmount)}</cbc:TaxAmount>")
                     .Append("<cac:TaxCategory>")
                       .Append($"<cbc:ID schemeID=\"UN/ECE 5305\" schemeAgencyID=\"6\">{taxType}</cbc:ID>")
                       .Append($"<cbc:Percent>{F(rate)}</cbc:Percent>")
                       .Append("<cac:TaxScheme><cbc:ID schemeID=\"UN/ECE 5153\" schemeAgencyID=\"6\">VAT</cbc:ID></cac:TaxScheme>")
                     .Append("</cac:TaxCategory>")
                   .Append("</cac:TaxSubtotal>");
            }

            xml.Append("</cac:TaxTotal>")
               .Append("<cac:LegalMonetaryTotal>")
                 .Append($"<cbc:TaxExclusiveAmount currencyID=\"{Cur}\">{F(taxExclusive)}</cbc:TaxExclusiveAmount>")
                 .Append($"<cbc:TaxInclusiveAmount currencyID=\"{Cur}\">{F(taxInclusive)}</cbc:TaxInclusiveAmount>")
                 .Append($"<cbc:AllowanceTotalAmount currencyID=\"{Cur}\">{F(discountTotal)}</cbc:AllowanceTotalAmount>")
                 .Append(isReturn ? $"<cbc:PrepaidAmount currencyID=\"{Cur}\">0</cbc:PrepaidAmount>" : "")
                 .Append($"<cbc:PayableAmount currencyID=\"{Cur}\">{F(taxInclusive)}</cbc:PayableAmount>")
               .Append("</cac:LegalMonetaryTotal>")
               .Append(lines)
               .Append("</Invoice>");

            return xml.ToString();
        }
    }
}