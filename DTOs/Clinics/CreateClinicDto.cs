namespace ClinicSaaS.API.DTOs.Clinics
{
    public class CreateClinicDto
    {
        public string Name { get; set; } = default!;// اسم العيادة
        public string subDomain { get; set; } = default!;// النطاق الفرعي للعيادة (سيستخدم في URL)
        public string? Logo { get; set; }// رابط شعار العيادة (اختياري)
        public string? Address { get; set; }// عنوان العيادة (اختياري)
        public string? Phone { get; set; }// رقم هاتف العيادة (اختياري)
        public string? website { get; set; }// موقع العيادة الإلكتروني (اختياري)
        public string? Email { get; set; }// بريد إلكتروني للاتصال بالعيادة (اختياري)
        public string? OwnerName { get; set; }// اسم مالك العيادة (اختياري)
        public string? OwnerEmail { get; set; }// بريد إلكتروني لمالك العيادة (اختياري)
        public string? OwnerPhone { get; set; }// رقم هاتف مالك العيادة (اختياري)
        public string? TaxNumber { get; set; }// الرقم الضريبي للعيادة (اختياري)
        public string? CommercialRegister { get; set; }// السجل التجاري للعيادة (اختياري)
        public string? InvoiceId { get; set; }// معرف الفاتورة (اختياري، يمكن استخدامه لربط العيادة بنظام الفواتير)
        public string? InvoiceKey { get; set; }// مفتاح الفاتورة (اختياري، يمكن استخدامه لربط العيادة بنظام الفواتير)
        public string? Description { get; set; }// وصف العيادة (اختياري)
    }
}
