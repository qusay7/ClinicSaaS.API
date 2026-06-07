using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }
        //DbSet هو ببساطة "بوابة" بين الكود وجدول قاعدة البيانات.
        // Define DbSets for your entities here
        // أول جدول تجريبي: المرضى
        // DbSet يمثل مجموعة من الكيانات (Entities) من نوع معين. في هذه الحالة، مجموعة المرضى.
        public DbSet<Patient> Patients { get; set; }
        public DbSet<Appointment> Appointments { get; set; }// إضافة جدول المواعيد
        public DbSet<Clinic> Clinics { get; set; } // إضافة جدول العيادات
        public DbSet<User> Users { get; set; } // إضافة جدول المستخدمين
        public DbSet<Doctor> Doctors { get; set; }// إضافة جدول الأطباء
        public DbSet<Plan> Plans { get; set; }// إضافة جدول الخطط
        public DbSet<Subscription> Subscriptions { get; set; }// إضافة جدول الاشتراكات

        public DbSet<RefreshToken> RefreshTokens { get; set; } // إضافة جدول Refresh Tokens

    }
    //جدول البيانات (Entities) تمثل الجداول في قاعدة البيانات. هذا الكلاس يمثل جدول المرضى.
    public class Patient
    {
        public Guid Id { get; set; }
        public int PatientNumber { get; set; }
        public string? NationalId { get; set; }
        public string FullName { get; set; } = default!;
        public DateTime? DateOfBirth { get; set; }
        public string? Phone { get; set; }
        public string? Phone2 { get; set; }
        public string? Gender { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // قيم افتراضية false = 0 في قاعدة البيانات
        public bool stopped { get; set; } = false;
        public bool isdeleted { get; set; } = false;

        public string? Notes { get; set; }
        public string? Notes2 { get; set; }
        public string? Notes3 { get; set; }

        public string? BloodType { get; set; }     // فصيلة الدم (A+, B-, ...)
        public string? Address { get; set; }        // العنوان
        public string? Email { get; set; }          // البريد الإلكتروني
        public string? EmergencyContact { get; set; } // اسم شخص للطوارئ
        public string? EmergencyPhone { get; set; }   // هاتف شخص الطوارئ
        public string? Allergies { get; set; }      // الحساسية (بنسلين, ...)
        public string? ChronicDiseases { get; set; } // أمراض مزمنة (سكري, ضغط, ...)
        public string? Occupation { get; set; }     // المهنة
        public string? MaritalStatus { get; set; }  // الحالة الاجتماعية

        // في كلاس Patient أضف هذا السطر
        // هذا يعني أن كل مريض يمكن أن يكون لديه عدة مواعيد (علاقة واحد-لعديد)
        public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
        public Guid ClinicId { get; set; } // ربط المريض بالعيادة
        public Clinic Clinic { get; set; } = default!; // خاصية Navigation لربط المريض بالعيادة
    }

    public class Appointment
    {       
        public Guid Id { get; set; }// Unique identifier for the appointment
        // Foreign key to link to the patient
        // ربط الموعد بالمريض — Foreign Key
        public Guid PatientId { get; set; }
         // Navigation property to link to the patient
        // خاصية Navigation — تسمح لنا بالوصول لبيانات المريض من الموعد
        public Patient Patient { get; set; } = default!;// هذا يعني أن كل موعد مرتبط بمريض واحد (علاقة واحد-لواحد)
        public DateTime AppointmentDate { get; set; }// تاريخ ووقت الموعد
                                                     // ✅ أضف هذا
        public Guid? DoctorId { get; set; }// ربط الموعد بالطبيب (اختياري)
        public Doctor? Doctor { get; set; }// خاصية Navigation لربط الموعد بالطبيب

        public string? Type { get; set; }// نوع الموعد (استشارة, متابعة, ...)
        public decimal? Price { get; set; }// سعر الموعد

        // الحالة: pending / confirmed / cancelled
        public string Status { get; set; } = "scheduled";

        public string? Notes { get; set; }// ملاحظات إضافية عن الموعد
        public string? Notes2 { get; set; }
        public string? Notes3 { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;// تاريخ إنشاء الموعد
        public bool isdeleted { get; set; } = false;// هذا الحقل يستخدم للحذف المنطقي (soft delete) — يعني أن الموعد لا يتم حذفه فعلياً من قاعدة البيانات، بل يتم تمييزه كـ "محذوف" بحيث لا يظهر في الاستعلامات العادية

        public Guid ClinicId { get; set; }          // ربط الموعد بالعيادة
        public Clinic Clinic { get; set; } = default!;// خاصية Navigation لربط الموعد بالعيادة
    }

    // جدول العيادات
    public class Clinic
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;        // اسم العيادة
        public string Subdomain { get; set; } = default!;   // clinicA
        public string? Logo { get; set; }    // Logo 
        public string? Address { get; set; }   // العنوان
        public string? Phone { get; set; }     // رقم الهاتف
        public string? Website { get; set; }  // الموقع الإلكتروني
        public string? Email { get; set; }     // قيم افتراضية true = 1 في قاعدة البيانات
        public string? OwnerName { get; set; }  // اسم صاحب العيادة
        public string? OwnerEmail { get; set; }   // بريد صاحب العيادة
        public string? OwnerPhone { get; set; }   // هاتف صاحب العيادة
        public string? TaxNumber { get; set; } //S (الرقم الضريبي)
        public string? SourceNumber { get; set; } //   (الرقم المصدر للفوترة)
        public string? InvoiceId { get; set; }    // رمز الفاتورة الإلكترونية
        public string? InvoiceKey { get; set; }   // مفتاح الفاتورة الإلكترونية
        public string? Description { get; set; }     // وصف العيادة

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // علاقة — كل عيادة لها مستخدمون
        public ICollection<User> Users { get; set; } = new List<User>();
        // علاقة — كل عيادة لها مرضى
        public ICollection<Patient> Patients { get; set; } = new List<Patient>();
        // علاقة — كل عيادة لها مواعيد (عبر المرضى)
        public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

        public ICollection<Doctor> Doctors { get; set; } = new List<Doctor>();// علاقة — كل عيادة لها أطباء
                                                                             
        public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>(); // علاقة — كل عيادة لها اشتراكات

    }

    // جدول المستخدمين
    public class User
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = default!;
        public string Email { get; set; } = default!;
        public string PasswordHash { get; set; } = default!; // كلمة المرور مشفّرة
        public string Role { get; set; } = default!;         // SuperAdmin/Admin/Doctor/Receptionist
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ربط بالعيادة — null للـ SuperAdmin
        public Guid? ClinicId { get; set; }
        public Clinic? Clinic { get; set; }

        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();// علاقة — كل مستخدم له عدة Refresh Tokens
    }

    public class Doctor
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = default!;
        public string? Specialty { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool isdeleted { get; set; } = false;

        // ربط بالعيادة
        public Guid ClinicId { get; set; }
        public Clinic Clinic { get; set; } = default!;

        // علاقة — كل طبيب له مواعيد
        public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    }

    // جدول الخطط (Subscription Plans)
    public class Plan
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;        // Basic / Standard / Premium
        public string? Description { get; set; }            // وصف الخطة
        public decimal MonthlyPrice { get; set; }           // السعر الشهري
        public decimal YearlyPrice { get; set; }            // السعر السنوي
        public int MaxUsers { get; set; }                   // -1 = غير محدود
        public int MaxDoctors { get; set; }                 // -1 = غير محدود
        public int MaxPatients { get; set; }                // -1 = غير محدود
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // علاقة — خطة واحدة لها اشتراكات كثيرة
        public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    }

    // جدول الاشتراكات (Subscriptions)
    public class Subscription
    {
        public Guid Id { get; set; }
        public DateTime StartDate { get; set; }             // تاريخ بداية الاشتراك
        public DateTime EndDate { get; set; }               // تاريخ انتهاء الاشتراك
        public string BillingCycle { get; set; } = "monthly"; // monthly / yearly
        public decimal PricePaid { get; set; }              // المبلغ المدفوع فعلياً
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ربط بالعيادة
        public Guid ClinicId { get; set; }
        public Clinic Clinic { get; set; } = default!;

        // ربط بالخطة
        public Guid PlanId { get; set; }
        public Plan Plan { get; set; } = default!;
    }

    // جدول Refresh Tokens لتخزين التوكنات المستخدمة لتجديد صلاحية الدخول (Refresh Tokens)
    public class RefreshToken
    {
        public Guid Id { get; set; }// معرف فريد للتوكن
        public string Token { get; set; } = default!;     // التوكن المشفّر
        public DateTime ExpiresAt { get; set; }           // تاريخ الانتهاء
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;// تاريخ الإنشاء
        public bool IsRevoked { get; set; } = false;      // هل تم إلغاؤه؟

        // ربط بالمستخدم
        public Guid UserId { get; set; }// معرف المستخدم الذي يملك هذا التوكن
        public User User { get; set; } = default!; // خاصية Navigation لربط التوكن بالمستخدم
    }
}
