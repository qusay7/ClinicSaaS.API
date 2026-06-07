namespace ClinicSaaS.API.DTOs.Users
{
    public class CreateUserDto
    {
        public string FullName { get; set; } = default!;// اسم المستخدم
        public string Email { get; set; } = default!;// البريد الإلكتروني للمستخدم
        public string Password { get; set; } = default!;// كلمة المرور للمستخدم
        public string Role { get; set; } = default!;// دور المستخدم (مثلاً: SuperAdmin, Admin, User)
        public Guid? ClinicId { get; set; }                 // العيادة التي ينتمي إليها


    }
}
