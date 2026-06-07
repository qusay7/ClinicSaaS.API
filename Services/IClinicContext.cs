namespace ClinicSaaS.API.Services
{
    // هذه الواجهة تحتوي على معلومات العيادة الحالية
    // سيتم حقنها في كل Controller يحتاجها

    public interface IClinicContext
    {
        Guid? ClinicId { get; }      // معرف العيادة الحالية
        string? Role { get; }        // دور المستخدم الحالي
        Guid? UserId { get; }        // معرف المستخدم الحالي
        bool IsSuperAdmin { get; }   // هل هو SuperAdmin؟
        bool IsCompanyStaff { get; }  // هل هو موظف في الشركة (SuperAdmin أو Admin)؟
        bool IsClinicUser { get; }// هل هو مستخدم عادي في العيادة (Doctor أو Receptionist)؟





    }
}
