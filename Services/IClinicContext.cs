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
        List<string> Permissions { get; }           // ✅ أضف
        bool HasPermission(string permission);       // ✅ أضف
        Guid? RoleId { get; }

        // ✅ بوابات ميزات الخطة الحالية — من نفس اشتراك العيادة النشط، لا من التوكن
        // (عشان أي تغيير بالخطة ينعكس فوراً بدون داعي لتسجيل خروج/دخول)
        bool HasElectronicInvoicing { get; }
        bool HasMultipleDepartments { get; }



    }


}
