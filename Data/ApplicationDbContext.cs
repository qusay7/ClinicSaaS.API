using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClinicSaaS.API.Data
{
    public enum RoleScope
    {
        Clinic = 0,    // أدوار العيادة
        Admin = 1      // أدوار الإدارة
    }
    public class ApplicationDbContext : DbContext
    {
        private readonly IHttpContextAccessor? _httpContextAccessor;
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IHttpContextAccessor? httpContextAccessor = null)
       : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        // ✅ الدالة السحرية — تشتغل تلقائياً قبل كل حفظ
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var userIdClaim = _httpContextAccessor?.HttpContext?.User?
                .FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            Guid? currentUserId = Guid.TryParse(userIdClaim, out var uid) ? uid : null;

            foreach (var entry in ChangeTracker.Entries<IAuditable>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.CreatedBy = currentUserId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Entity.UpdatedBy = currentUserId;
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                }
            }

            return await base.SaveChangesAsync(cancellationToken);
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
        public DbSet<ClinicSchedule> ClinicSchedules { get; set; }// إضافة جدول جداول دوام العيادة
        public DbSet<DoctorSchedule> DoctorSchedules { get; set; }// إضافة جدول جداول دوام الأطباء
        public DbSet<Role> Roles { get; set; }// إضافة جدول الأدوار
        public DbSet<Permission> Permissions { get; set; }// إضافة جدول الصلاحيات
        public DbSet<RolePermission> RolePermissions { get; set; }// إضافة جدول ربط الأدوار بالصلاحيات
        public DbSet<Department> Departments { get; set; }
        public DbSet<DepartmentRole> DepartmentRoles { get; set; }
        public DbSet<QueueEntry> QueueEntries { get; set; }
        public DbSet<VisitNote> VisitNotes { get; set; }
        public DbSet<Absence> Absences { get; set; }
        public DbSet<NotificationLog> NotificationLogs { get; set; }

        // ══ Insurance ══
        public DbSet<InsuranceCompany> InsuranceCompanies { get; set; }
        public DbSet<PatientInsurance> PatientInsurances { get; set; }
        public DbSet<InsuranceClaim> InsuranceClaims { get; set; }
        public DbSet<PaymentDetail> PaymentDetails { get; set; }

        public DbSet<Staff> Staff { get; set; }
        // ══ خطط العلاج ══
        public DbSet<TreatmentPlanTemplate> TreatmentPlanTemplates { get; set; }
        public DbSet<TreatmentPlan> TreatmentPlans { get; set; }
        public DbSet<TreatmentSession> TreatmentSessions { get; set; }
        public DbSet<DoctorTemplateSetting> DoctorTemplateSettings { get; set; }   // ✅ جديد
        public DbSet<Settlement> Settlements { get; set; }   // ✅ جديد — محرك التسوية الموحّد
        public DbSet<Attachment> Attachments { get; set; }   // ✅ جديد — مرفقات المريض
        public DbSet<AppointmentVisitType> AppointmentVisitTypes { get; set; }   // ✅ جديد — بنود الفاتورة
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<InvoiceItem> InvoiceItems { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ✅ Appointments
            modelBuilder.Entity<Appointment>()
                .HasOne(a => a.Patient)
                .WithMany(p => p.Appointments)
                .HasForeignKey(a => a.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Appointment>()
                .HasOne(a => a.Doctor)
                .WithMany(d => d.Appointments)
                .HasForeignKey(a => a.DoctorId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Appointment>()
                .HasOne(a => a.Clinic)
                .WithMany(c => c.Appointments)
                .HasForeignKey(a => a.ClinicId)
                .OnDelete(DeleteBehavior.NoAction);

            // ✅ قالب الزيارة المرتبط بالموعد (اختياري)
            modelBuilder.Entity<Appointment>()
                .HasOne(a => a.Template)
                .WithMany()
                .HasForeignKey(a => a.TemplateId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Doctor>()
                .HasOne(d => d.Clinic)
                .WithMany(c => c.Doctors)
                .HasForeignKey(d => d.ClinicId)
                .OnDelete(DeleteBehavior.NoAction);

            // ✅ Doctors - Department (صريح ومحدد)
            modelBuilder.Entity<Doctor>()
                .HasOne(d => d.Department)
                .WithMany(dep => dep.Doctors)
                .HasForeignKey(d => d.DepartmentId)
                .OnDelete(DeleteBehavior.NoAction);

            // ✅ QueueEntries
            modelBuilder.Entity<QueueEntry>()
                .HasOne(q => q.Clinic)
                .WithMany()
                .HasForeignKey(q => q.ClinicId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<QueueEntry>()
                .HasOne(q => q.Patient)
                .WithMany()
                .HasForeignKey(q => q.PatientId)
                .OnDelete(DeleteBehavior.NoAction);


            modelBuilder.Entity<QueueEntry>()
                .HasOne(q => q.Doctor)
                .WithMany()
                .HasForeignKey(q => q.DoctorId)
                .OnDelete(DeleteBehavior.NoAction);

            // ✅ يمنع تكرار رقم الدور بنفس اليوم ونفس العيادة
            modelBuilder.Entity<QueueEntry>()
                .HasIndex(q => new { q.ClinicId, q.Date, q.QueueNumber })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            modelBuilder.Entity<VisitNote>()
                .HasOne(v => v.Clinic)
                .WithMany()
                .HasForeignKey(v => v.ClinicId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<VisitNote>()
                .HasOne(v => v.Patient)
                .WithMany()
                .HasForeignKey(v => v.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<VisitNote>()
                .HasOne(v => v.Doctor)
                .WithMany()
                .HasForeignKey(v => v.DoctorId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<VisitNote>()
                .HasOne(v => v.Appointment)
                .WithMany()
                .HasForeignKey(v => v.AppointmentId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<VisitNote>()
                .HasOne(v => v.QueueEntry)
                .WithMany()
                .HasForeignKey(v => v.QueueEntryId)
                .OnDelete(DeleteBehavior.NoAction);



            modelBuilder.Entity<Absence>()
                .HasOne(a => a.Clinic)
                .WithMany()
                .HasForeignKey(a => a.ClinicId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Absence>()
                .HasOne(a => a.Doctor)
                .WithMany()
                .HasForeignKey(a => a.DoctorId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<InsuranceCompany>(e => {
                e.HasKey(x => x.Id);
                e.Property(x => x.CoverageRate).HasPrecision(5, 2);
                e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PatientInsurance>(e => {
                e.HasKey(x => x.Id);
                e.Property(x => x.CoverageRate).HasPrecision(5, 2);
                e.Property(x => x.MaxCoverageAmount).HasPrecision(10, 3);
                e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.InsuranceCompany).WithMany(c => c.PatientInsurances).HasForeignKey(x => x.InsuranceCompanyId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<InsuranceClaim>(e => {
                e.HasKey(x => x.Id);
                e.Property(x => x.TotalAmount).HasPrecision(10, 3);
                e.Property(x => x.InsuranceAmount).HasPrecision(10, 3);
                e.Property(x => x.PatientAmount).HasPrecision(10, 3);
                e.Property(x => x.CoverageRate).HasPrecision(5, 2);
                e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.PatientInsurance).WithMany(p => p.Claims).HasForeignKey(x => x.PatientInsuranceId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PaymentDetail>(e => {
                e.HasKey(x => x.Id);
                e.Property(x => x.TotalAmount).HasPrecision(10, 3);
                e.Property(x => x.InsuranceAmount).HasPrecision(10, 3);
                e.Property(x => x.PatientAmount).HasPrecision(10, 3);
                e.Property(x => x.AmountPaid).HasPrecision(10, 3);
                e.Property(x => x.InsuranceBalance).HasPrecision(10, 3);
                e.Ignore(x => x.PatientBalance); // computed property
                e.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.InsuranceClaim).WithMany().HasForeignKey(x => x.InsuranceClaimId).OnDelete(DeleteBehavior.Restrict);
                // ✅ يمنع وجود أكثر من دفعة لنفس الموعد (يحل مشكلة الدفعة المزدوجة)
                e.HasIndex(x => x.AppointmentId).IsUnique();
            });

            modelBuilder.Entity<Staff>(e => {
                e.HasKey(x => x.Id);
                e.Property(x => x.Salary).HasPrecision(10, 3);
                e.HasOne(x => x.Clinic)
                    .WithMany()
                    .HasForeignKey(x => x.ClinicId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Department)
                    .WithMany()
                    .HasForeignKey(x => x.DepartmentId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Role)
                    .WithMany()
                    .HasForeignKey(x => x.RoleId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Doctor)
                    .WithMany()
                    .HasForeignKey(x => x.DoctorId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            // ══ خطط العلاج ══
            modelBuilder.Entity<TreatmentPlanTemplate>(e => {
                e.HasKey(x => x.Id);
                e.Property(x => x.DefaultPricePerSession).HasPrecision(10, 3);
                e.Property(x => x.DefaultTotalPrice).HasPrecision(10, 3);
                e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<TreatmentPlan>(e => {
                e.HasKey(x => x.Id);
                e.Property(x => x.PricePerSession).HasPrecision(10, 3);
                e.Property(x => x.TotalPrice).HasPrecision(10, 3);
                e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<TreatmentSession>(e => {
                e.HasKey(x => x.Id);
                e.Property(x => x.Cost).HasPrecision(10, 3);
                e.HasOne(x => x.TreatmentPlan).WithMany(p => p.Sessions).HasForeignKey(x => x.TreatmentPlanId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            });

            // ✅ إعدادات الطبيب المالية (سعر خاص + حصة) لكل قالب
            modelBuilder.Entity<DoctorTemplateSetting>(e => {
                e.HasKey(x => x.Id);
                e.Property(x => x.FirstVisitPrice).HasPrecision(10, 3);
                e.Property(x => x.FollowUpPrice).HasPrecision(10, 3);
                e.Property(x => x.FirstVisitCommissionRate).HasPrecision(10, 3);
                e.Property(x => x.FollowUpCommissionRate).HasPrecision(10, 3);
                e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);

                // ✅ طبيب معيّن ممكن يكون له سطر واحد بس لكل قالب محدد
                e.HasIndex(x => new { x.DoctorId, x.TemplateId })
                    .IsUnique()
                    .HasFilter("[TemplateId] IS NOT NULL AND [IsDeleted] = 0")
                    .HasDatabaseName("IX_DoctorTemplateSettings_Doctor_Template");

                // ✅ وطبيب معيّن ممكن يكون له "إعداد عام" واحد بس (TemplateId = NULL)
                e.HasIndex(x => x.DoctorId)
                    .IsUnique()
                    .HasFilter("[TemplateId] IS NULL AND [IsDeleted] = 0")
                    .HasDatabaseName("IX_DoctorTemplateSettings_Doctor_GeneralOnly");
            });
            modelBuilder.Entity<DoctorTemplateSetting>().HasQueryFilter(x => !x.IsDeleted);

            // ✅ محرك التسوية الموحّد
            modelBuilder.Entity<Settlement>(e => {
                e.HasKey(x => x.Id);
                e.Property(x => x.TotalAmount).HasPrecision(10, 3);
                e.Property(x => x.AmountPaid).HasPrecision(10, 3);
                e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.InsuranceCompany).WithMany().HasForeignKey(x => x.InsuranceCompanyId).OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<Settlement>().HasQueryFilter(x => !x.IsDeleted);

            // ✅ مرفقات المريض
            modelBuilder.Entity<Attachment>(e => {
                e.HasKey(x => x.Id);
                e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.NoAction);
            });
            modelBuilder.Entity<Attachment>().HasQueryFilter(x => !x.IsDeleted);

            modelBuilder.Entity<Appointment>()
                .HasOne(a => a.CommissionSettlement)
                .WithMany()
                .HasForeignKey(a => a.CommissionSettlementId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<InsuranceClaim>()
                .HasOne(c => c.Settlement)
                .WithMany()
                .HasForeignKey(c => c.SettlementId)
                .OnDelete(DeleteBehavior.NoAction);

            // Soft Delete
            modelBuilder.Entity<TreatmentPlanTemplate>().HasQueryFilter(x => !x.IsDeleted);
            modelBuilder.Entity<TreatmentPlan>().HasQueryFilter(x => !x.IsDeleted);
            modelBuilder.Entity<TreatmentSession>().HasQueryFilter(x => !x.IsDeleted);

            // ══ Global Query Filters (Soft Delete) ══
            modelBuilder.Entity<Patient>().HasQueryFilter(x => !x.IsDeleted);
            modelBuilder.Entity<Appointment>().HasQueryFilter(x => !x.IsDeleted);
            modelBuilder.Entity<QueueEntry>().HasQueryFilter(x => !x.IsDeleted);
            modelBuilder.Entity<VisitNote>().HasQueryFilter(x => !x.IsDeleted);
            modelBuilder.Entity<Doctor>().HasQueryFilter(x => !x.IsDeleted);
            modelBuilder.Entity<User>().HasQueryFilter(x => !x.IsDeleted);
            modelBuilder.Entity<Absence>().HasQueryFilter(x => !x.IsDeleted);
            modelBuilder.Entity<InsuranceCompany>().HasQueryFilter(x => !x.IsDeleted);
            modelBuilder.Entity<PatientInsurance>().HasQueryFilter(x => !x.IsDeleted);
            modelBuilder.Entity<InsuranceClaim>().HasQueryFilter(x => !x.IsDeleted);
            modelBuilder.Entity<PaymentDetail>().HasQueryFilter(x => !x.IsDeleted);
            modelBuilder.Entity<Staff>().HasQueryFilter(x => !x.IsDeleted);

            // ══ Unique Indexes ══

            // كل عيادة لها Subdomain فريد (يستخدم بالـ URL: clinicA.yoursite.com)
            modelBuilder.Entity<Clinic>()
                .HasIndex(c => c.Subdomain)
                .IsUnique();
            modelBuilder.Entity<Clinic>().Property(x => x.DefaultTaxRate).HasPrecision(5, 2);
            // الإيميل يجب أن يكون فريد بين المستخدمين النشطين فقط
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            // اسم المستخدم فريد بين النشطين فقط
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique()
                .HasFilter("[IsDeleted] = 0 AND [Username] IS NOT NULL");

            // رقم المريض فريد ضمن نفس العيادة، بين المرضى النشطين فقط
            modelBuilder.Entity<Patient>()
                .HasIndex(p => new { p.ClinicId, p.PatientNumber })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            // الرقم الوطني فريد ضمن نفس العيادة، بين النشطين فقط
            modelBuilder.Entity<Patient>()
                .HasIndex(p => new { p.ClinicId, p.NationalId })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0 AND [NationalId] IS NOT NULL");

            // ══ تصحيح دقة الحقول المالية (decimal precision) ══
            modelBuilder.Entity<Appointment>().Property(x => x.Price).HasPrecision(10, 3);
            modelBuilder.Entity<Appointment>().Property(x => x.DoctorCommissionAmount).HasPrecision(10, 3);
            modelBuilder.Entity<DoctorSchedule>().Property(x => x.FirstVisitPrice).HasPrecision(10, 3);
            modelBuilder.Entity<DoctorSchedule>().Property(x => x.FollowUpPrice).HasPrecision(10, 3);
            modelBuilder.Entity<Plan>().Property(x => x.MonthlyPrice).HasPrecision(10, 3);
            modelBuilder.Entity<Plan>().Property(x => x.YearlyPrice).HasPrecision(10, 3);
            modelBuilder.Entity<Subscription>().Property(x => x.PricePaid).HasPrecision(10, 3);
            modelBuilder.Entity<VisitNote>().Property(x => x.Cost).HasPrecision(10, 3);
            modelBuilder.Entity<TreatmentPlanTemplate>().Property(x => x.FirstVisitPrice).HasPrecision(10, 3);
            modelBuilder.Entity<TreatmentPlanTemplate>().Property(x => x.FollowUpPrice).HasPrecision(10, 3);
            // بـ OnModelCreating
            modelBuilder.Entity<Subscription>()
                .HasIndex(s => s.ClinicId)
                .IsUnique()
                .HasFilter("[IsActive] = 1")
                .HasDatabaseName("IX_Subscriptions_ClinicId_ActiveOnly");   // ✅ اسم مختلف صراحة

            // ✅ بنود الفاتورة (أنواع الزيارة الفعلية لكل موعد)
            modelBuilder.Entity<AppointmentVisitType>(e => {
                e.HasKey(x => x.Id);
                e.Property(x => x.Price).HasPrecision(10, 3);
                e.Property(x => x.InsuranceRate).HasPrecision(5, 2);
                e.Property(x => x.InsuranceAmount).HasPrecision(10, 3);
                e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.NoAction);
                e.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.NoAction);
                e.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.NoAction);
                e.HasIndex(x => x.AppointmentId);
            });
            modelBuilder.Entity<AppointmentVisitType>().HasQueryFilter(x => !x.IsDeleted);

            // ✅ الفواتير الضريبية والمرتجعات
            modelBuilder.Entity<Invoice>(e => {
                e.HasKey(x => x.Id);
                e.Property(x => x.TotalAmount).HasPrecision(18, 9);
                e.Property(x => x.DiscountAmount).HasPrecision(18, 9);
                e.Property(x => x.TaxAmount).HasPrecision(18, 9);
                e.Property(x => x.PayableAmount).HasPrecision(18, 9);
                e.Property(x => x.TaxRate).HasPrecision(5, 2);
                e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.NoAction);
                e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.NoAction);
                e.HasOne(x => x.PaymentDetail).WithMany().HasForeignKey(x => x.PaymentDetailId).OnDelete(DeleteBehavior.NoAction);
                e.HasOne(x => x.SourceInvoice).WithMany().HasForeignKey(x => x.SourceInvoiceId).OnDelete(DeleteBehavior.NoAction);
                e.HasIndex(x => new { x.ClinicId, x.InvoiceNumber }).IsUnique().HasFilter("[IsDeleted] = 0");
                e.HasIndex(x => x.PaymentDetailId);
            });
            modelBuilder.Entity<Invoice>().HasQueryFilter(x => !x.IsDeleted);

            modelBuilder.Entity<InvoiceItem>(e => {
                e.HasKey(x => x.Id);
                e.Property(x => x.Quantity).HasPrecision(18, 9);
                e.Property(x => x.UnitPrice).HasPrecision(18, 9);
                e.Property(x => x.Discount).HasPrecision(18, 9);
                e.Property(x => x.TaxRate).HasPrecision(5, 2);
                e.Property(x => x.TaxAmount).HasPrecision(18, 9);
                e.HasOne(x => x.Invoice).WithMany(i => i.Items).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.NoAction);
                e.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.NoAction);
                e.HasOne(x => x.SourceItem).WithMany().HasForeignKey(x => x.SourceItemId).OnDelete(DeleteBehavior.NoAction);
                e.HasIndex(x => x.InvoiceId);
            });
            modelBuilder.Entity<InvoiceItem>().HasQueryFilter(x => !x.IsDeleted);
        }


    }

    //جدول البيانات (Entities) تمثل الجداول في قاعدة البيانات. هذا الكلاس يمثل جدول المرضى.
    public class Patient : IAuditable
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

        public bool stopped { get; set; } = false;
        public bool IsDeleted { get; set; } = false;

        public string? Notes { get; set; }
        public string? Notes2 { get; set; }
        public string? Notes3 { get; set; }

        public string? BloodType { get; set; }
        public string? Address { get; set; }
        public string? Email { get; set; }
        public string? EmergencyContact { get; set; }
        public string? EmergencyPhone { get; set; }
        public string? Allergies { get; set; }
        public string? ChronicDiseases { get; set; }
        public string? Occupation { get; set; }
        public string? MaritalStatus { get; set; }

        public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
        public Guid ClinicId { get; set; }
        public Clinic Clinic { get; set; } = default!;

        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // جدول المواعيد
    public class Appointment : IAuditable
    {
        public Guid Id { get; set; }
        public Guid PatientId { get; set; }
        public Patient Patient { get; set; } = default!;
        public DateTime AppointmentDate { get; set; }
        public Guid? DoctorId { get; set; }
        public Doctor? Doctor { get; set; }

        public string? Type { get; set; }
        public decimal? Price { get; set; }

        public string Status { get; set; } = "scheduled";

        public string? Notes { get; set; }
        public string? Notes2 { get; set; }
        public string? Notes3 { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;

        public Guid ClinicId { get; set; }
        public Clinic Clinic { get; set; } = default!;

        public DateTime? CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }

        // ✅ جديد — أي قالب زيارة ينطبق على هذا الموعد (كشف/مراجعة/استشارة/متابعة أو أي قالب مخصص)
        // يُستخدم لتحديد السعر وحصة الطبيب تلقائياً وقت الحجز والـ Checkout
        public Guid? TemplateId { get; set; }
        public TreatmentPlanTemplate? Template { get; set; }

        // ✅ مبلغ حصة الطبيب الفعلي، يُحسب ويُخزّن مرة واحدة وقت الـ Checkout
        // (Snapshot ثابت — ما يتأثر لو تغيّرت نسبة الطبيب مستقبلاً)
        public decimal? DoctorCommissionAmount { get; set; }

        // ✅ جديد — ربط حصة الطبيب بدفعة تسوية جماعية (لو اتسوّت مع الطبيب)
        public Guid? CommissionSettlementId { get; set; }
        public Settlement? CommissionSettlement { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = default!;
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        /// <summary>
        /// أوقات التذكير بالساعات قبل الموعد.
        /// مثال: "24,2,1" = تذكير قبل 24 ساعة، وقبل ساعتين، وقبل ساعة.
        /// </summary>
        public string? CustomReminders { get; set; }

        /// <summary>
        /// هل تم إرسال إشعار إنشاء الموعد؟
        /// </summary>
        public bool AppointmentCreatedNotificationSent { get; set; } = false;

        /// <summary>
        /// هل تم إرسال إشعار التعديل؟
        /// </summary>
        public bool AppointmentUpdatedNotificationSent { get; set; } = false;

        /// <summary>
        /// هل تم إرسال إشعار الإلغاء؟
        /// </summary>
        public bool AppointmentCancelledNotificationSent { get; set; } = false;
    }

    // جدول العيادات
    public class Clinic : IAuditable
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string Subdomain { get; set; } = default!;
        public string? Logo { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Website { get; set; }
        public string? Email { get; set; }
        public string? OwnerName { get; set; }
        public string? OwnerEmail { get; set; }
        public string? OwnerPhone { get; set; }
        public string? TaxNumber { get; set; }
        public string? SourceNumber { get; set; }
        public string? InvoiceId { get; set; }
        public string? InvoiceKey { get; set; }
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string TimeZone { get; set; } = "Asia/Amman";
        public ICollection<User> Users { get; set; } = new List<User>();
        public ICollection<Patient> Patients { get; set; } = new List<Patient>();
        public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
        public ICollection<Doctor> Doctors { get; set; } = new List<Doctor>();
        public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
        public ICollection<ClinicSchedule> Schedules { get; set; } = new List<ClinicSchedule>();
        public ICollection<Department> Departments { get; set; } = new List<Department>();
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // ✅ إعدادات Ultramsg للإشعارات
        public string? UltramsgInstanceId { get; set; }
        public string? UltramsgApiToken { get; set; }
        public bool IsNotificationsEnabled { get; set; } = true;

        // ✅ إعدادات الفوترة الضريبية (JoFotara)
        // ✅ إعدادات الفوترة الضريبية (JoFotara)
        public string TaxRegistrationType { get; set; } = "income";   // income / sales
        public int DefaultTaxMethod { get; set; } = 2;                // النمط الافتراضي عند إنشاء فاتورة
        public decimal DefaultTaxRate { get; set; } = 0;
        public bool PricesIncludeTax { get; set; } = true;
        public int LastInvoiceNumber { get; set; } = 0;

    }

    // جدول المستخدمين
    public class User : IAuditable
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = default!;
        public string? Username { get; set; }
        public string Email { get; set; } = default!;
        public string PasswordHash { get; set; } = default!;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Role { get; set; } = default!;
        public Guid? ClinicId { get; set; }
        public Clinic? Clinic { get; set; }
        public Guid? RoleId { get; set; }
        public Role? UserRole { get; set; }
        public Guid? DepartmentId { get; set; }
        public Department? Department { get; set; }
        public bool IsDeleted { get; set; } = false;
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    //الطبيب
    public class Doctor : IAuditable
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = default!;
        public string? Specialty { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;

        public Guid ClinicId { get; set; }
        public Clinic Clinic { get; set; } = default!;

        public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
        public ICollection<DoctorSchedule> Schedules { get; set; } = new List<DoctorSchedule>();

        public Guid? UserId { get; set; }
        public User? User { get; set; }

        public Guid? DepartmentId { get; set; }
        public Department? Department { get; set; }

        public string WorkType { get; set; } = "appointments";
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // جدول الخطط (Subscription Plans)
    public class Plan : IAuditable
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public decimal MonthlyPrice { get; set; }
        public decimal YearlyPrice { get; set; }
        public int MaxUsers { get; set; }
        public int MaxDoctors { get; set; }
        public int MaxPatients { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? FeaturesText { get; set; }   // ✅ جديد — كل ميزة بسطر منفصل
        public bool IsFeatured { get; set; } = false;  // ✅ جديد — تعليم "الأكثر اختيارًا"

        public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // جدول الاشتراكات (Subscriptions)
    public class Subscription : IAuditable
    {
        public Guid Id { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string BillingCycle { get; set; } = "monthly";
        public decimal PricePaid { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Guid ClinicId { get; set; }
        public Clinic Clinic { get; set; } = default!;

        public Guid PlanId { get; set; }
        public Plan Plan { get; set; } = default!;
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // جدول Refresh Tokens
    public class RefreshToken : IAuditable
    {
        public Guid Id { get; set; }
        public string Token { get; set; } = default!;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsRevoked { get; set; } = false;
        public Guid? UserId { get; set; }
        public User? User { get; set; }
      
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // جدول دوام العيادة
    public class ClinicSchedule : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public Clinic Clinic { get; set; } = default!;

        public DayOfWeek DayOfWeek { get; set; }
        public TimeOnly OpenTime { get; set; }
        public TimeOnly CloseTime { get; set; }
        public bool IsActive { get; set; } = true;
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // جدول دوام الطبيب
    public class DoctorSchedule : IAuditable
    {
        public Guid Id { get; set; }
        public Guid DoctorId { get; set; }
        public Doctor? Doctor { get; set; }
         public DayOfWeek DayOfWeek { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public int SlotDuration { get; set; } = 10;
        public bool IsActive { get; set; } = true;
        public decimal? FirstVisitPrice { get; set; }
        public decimal? FollowUpPrice { get; set; }
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // Department
    public class Department : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public string Name { get; set; } = "";
        public DepartmentType Type { get; set; }
        public string? SettingsJson { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public string? NameEn { get; set; }
        public Clinic Clinic { get; set; } = null!;
        public ICollection<Doctor> Doctors { get; set; } = new List<Doctor>();
        public ICollection<DepartmentRole> DepartmentRoles { get; set; } = new List<DepartmentRole>();
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public enum DepartmentType
    {
        General = 0,
        Emergency = 1,
        Pharmacy = 2,
        Radiology = 3,
        Laboratory = 4,
        Accounting = 5,
        Other = 99
    }

    // Role مخصص للعيادة
    public class Role : IAuditable
    {
        public Guid Id { get; set; }
        public Guid? ClinicId { get; set; }
        public Guid? DepartmentId { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsSystem { get; set; }
        public string? NameEn { get; set; }
        public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();

        // ✅ تحويل من string إلى enum
        public RoleScope Scope { get; set; } = RoleScope.Clinic;
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // ✅ Computed properties
        [NotMapped]
        public string ScopeDisplay => Scope == RoleScope.Admin ? "🔐 Admin" : "🏥 Clinic";

        [NotMapped]
        public int PermissionsCount => RolePermissions?.Count(rp => rp.IsActive) ?? 0;
    }
    // Permission
    public class Permission : IAuditable
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Module { get; set; } = "";
        public string? Group { get; set; }
        public string DisplayName { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // RolePermission
    public class RolePermission : IAuditable
    {
        public Guid Id { get; set; }
        public Guid RoleId { get; set; }
        public Guid PermissionId { get; set; }
        public Guid? ClinicId { get; set; }
        public bool IsActive { get; set; } = true;
        public Role Role { get; set; } = null!;
        public Permission Permission { get; set; } = null!;
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // DepartmentRole
    public class DepartmentRole : IAuditable
    {
        public Guid Id { get; set; }
        public Guid DepartmentId { get; set; }
        public Guid RoleId { get; set; }

        public Department Department { get; set; } = null!;
        public Role Role { get; set; } = null!;
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // جدول الانتظار (Queue)
    public class QueueEntry : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public Clinic Clinic { get; set; } = default!;

        public Guid PatientId { get; set; }
        public Patient Patient { get; set; } = default!;

        public Guid? DoctorId { get; set; }
        public Doctor? Doctor { get; set; }

        public int QueueNumber { get; set; }
        public DateTime Date { get; set; }
        public string Status { get; set; } = "waiting";
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // جدول ملاحظات الزيارة (Visit Notes)
    // ══════════════════════════════════════
    // مرفقات المريض — صور أشعة، تحاليل، أي ملف طبي مرتبط بالمريض (اختياريًا بزيارة معينة)
    // ══════════════════════════════════════
    public class Attachment : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public Guid PatientId { get; set; }
        public Guid? AppointmentId { get; set; }   // اختياري — مرتبط بزيارة معينة

        public string FileName { get; set; } = "";     // الاسم الأصلي كما رفعه المستخدم
        public string FilePath { get; set; } = "";     // المسار النسبي داخل مجلد uploads
        public string FileType { get; set; } = "";     // Content-Type (image/jpeg, application/pdf...)
        public long FileSize { get; set; }               // بالبايت
        public string? Category { get; set; }             // "xray" / "lab" / "other" — نوع المرفق
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;

        public Clinic? Clinic { get; set; }
        public Patient? Patient { get; set; }
        public Appointment? Appointment { get; set; }

        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class VisitNote : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public Guid PatientId { get; set; }
        public Guid? AppointmentId { get; set; }
        public Guid? QueueEntryId { get; set; }
        public Guid? DoctorId { get; set; }

        public string? Diagnosis { get; set; }
        public string? Prescription { get; set; }
        public string? Tests { get; set; }
        public string? Notes { get; set; }
        public DateTime? NextVisitDate { get; set; }
        public decimal? Cost { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;

        public Clinic? Clinic { get; set; }
        public Patient? Patient { get; set; }
        public Doctor? Doctor { get; set; }
        public Appointment? Appointment { get; set; }
        public QueueEntry? QueueEntry { get; set; }
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // جدول الغياب (Absences)
    public class Absence : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public Guid? DoctorId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public string Type { get; set; } = "holiday";
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Clinic Clinic { get; set; } = null!;
        public Doctor? Doctor { get; set; }

        public bool IsDeleted { get; set; } = false;
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // NotificationLog
    public class NotificationLog : IAuditable
    {
        public Guid Id { get; set; }
        public Guid? AppointmentId { get; set; }
        public Appointment? Appointment { get; set; }
        public Guid ClinicId { get; set; }
        public Guid PatientId { get; set; }
        public string Type { get; set; } = "";
        public string Channel { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Message { get; set; } = "";
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime SentAt { get; set; }
         public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // ══════════════════════════════════════
    // شركة التأمين
    // ══════════════════════════════════════
    public class InsuranceCompany : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public string Name { get; set; } = "";
        public string? NameEn { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? ContactName { get; set; }
        public decimal CoverageRate { get; set; } = 80;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public bool IsDeleted { get; set; } = false;
        public Clinic? Clinic { get; set; }
        public ICollection<PatientInsurance> PatientInsurances { get; set; } = new List<PatientInsurance>();
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // ══════════════════════════════════════
    // بوليصة تأمين المريض
    // ══════════════════════════════════════
    public class PatientInsurance : IAuditable
    {
        public Guid Id { get; set; }
        public Guid PatientId { get; set; }
        public Guid ClinicId { get; set; }
        public Guid InsuranceCompanyId { get; set; }
        public string PolicyNumber { get; set; } = "";
        public string? MembershipNumber { get; set; }
        public decimal CoverageRate { get; set; } = 80;
        public decimal? MaxCoverageAmount { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsPrimary { get; set; } = true;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsDeleted { get; set; } = false;
        public Patient? Patient { get; set; }
        public InsuranceCompany? InsuranceCompany { get; set; }
        public ICollection<InsuranceClaim> Claims { get; set; } = new List<InsuranceClaim>();
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // ══════════════════════════════════════
    // مطالبة التأمين
    // ══════════════════════════════════════
    // ══════════════════════════════════════
    // محرك التسوية الموحّد — يخدم مخالصة الطبيب ومخالصة شركة التأمين بنفس الجدول
    // ══════════════════════════════════════
    public class Settlement : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public string Type { get; set; } = "";   // "doctor" / "insurance"

        public Guid? DoctorId { get; set; }
        public Doctor? Doctor { get; set; }

        public Guid? InsuranceCompanyId { get; set; }
        public InsuranceCompany? InsuranceCompany { get; set; }

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal TotalAmount { get; set; }
        // ✅ جديد — يسمح بتسوية جزئية (مو دايماً المبلغ كامل)
        public decimal AmountPaid { get; set; }
        public string Status { get; set; } = "pending";   // pending / partial / paid
        public DateTime? PaidAt { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;

        public Clinic? Clinic { get; set; }
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class InsuranceClaim : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public Guid PatientId { get; set; }
        public Guid PatientInsuranceId { get; set; }
        public Guid? AppointmentId { get; set; }
        public string ClaimNumber { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public decimal CoverageRate { get; set; }
        public decimal InsuranceAmount { get; set; }
        public decimal PatientAmount { get; set; }
        public string Status { get; set; } = "pending";
        public string? ApprovalNumber { get; set; }
        public string? RejectionReason { get; set; }
        public string? Notes { get; set; }
        public DateTime ServiceDate { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsDeleted { get; set; } = false;
        public Patient? Patient { get; set; }
        public PatientInsurance? PatientInsurance { get; set; }

        // ✅ جديد — ربط المطالبة بدفعة تسوية جماعية مع شركة التأمين (لو اتسوّت)
        public Guid? SettlementId { get; set; }
        public Settlement? Settlement { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = default!;
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class PaymentDetail : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public Guid AppointmentId { get; set; }
        public Guid PatientId { get; set; }

        public decimal TotalAmount { get; set; }
        public decimal InsuranceAmount { get; set; }
        public decimal PatientAmount { get; set; }

        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; } = "cash";
        public DateTime? PaidAt { get; set; }
        public bool IsPaid { get; set; } = false;

        public decimal PatientBalance => AmountPaid - PatientAmount;
        public decimal InsuranceBalance { get; set; }

        public Guid? InsuranceClaimId { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Clinic? Clinic { get; set; }
        public Appointment? Appointment { get; set; }
        public Patient? Patient { get; set; }
        public InsuranceClaim? InsuranceClaim { get; set; }
        public bool IsDeleted { get; set; } = false;
        [Timestamp]
        public byte[] RowVersion { get; set; } = default!;
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }

    }

    public class Staff : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }

        public string FullName { get; set; } = "";
        public string? FullNameEn { get; set; }
        public string? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? NationalId { get; set; }
        public string? Nationality { get; set; }
        public string? MaritalStatus { get; set; }
        public string? BloodType { get; set; }

        public string? Phone { get; set; }
        public string? Phone2 { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? EmergencyContact { get; set; }
        public string? EmergencyPhone { get; set; }

        public string JobTitle { get; set; } = "";
        public string ContractType { get; set; } = "fulltime";
        public string? StaffRole { get; set; }
        public DateTime? JoinDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal? Salary { get; set; }
        public string? WorkingHours { get; set; }
        public string? Qualifications { get; set; }
        public string? Specialization { get; set; }

        public bool IsActive { get; set; } = true;
        public string? Notes { get; set; }

        public Guid? UserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Guid? DepartmentId { get; set; }
        public Department? Department { get; set; }

        public Guid? RoleId { get; set; }
        public Role? Role { get; set; }

        public Guid? DoctorId { get; set; }
        public Doctor? Doctor { get; set; }

        public Clinic? Clinic { get; set; }

        public bool IsDeleted { get; set; } = false;
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
    // ══════════════════════════════════════
    // قوالب الخطط العلاجية (يحددها كل عيادة حسب سياستها)
    // ══════════════════════════════════════
    public class TreatmentPlanTemplate : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public string Name { get; set; } = "";          // "سحب عصب"
        public string? NameEn { get; set; }
        public Guid? DepartmentId { get; set; }          // مثلاً: الأسنان
        public int DefaultSessionsCount { get; set; } = 1;
        public decimal? DefaultPricePerSession { get; set; }
        public decimal? DefaultTotalPrice { get; set; }

        // ✅ جديد — لقوالب الزيارة الواحدة (كشف/مراجعة/استشارة/متابعة):
        // سعر أول مرة وسعر المراجعة، منفصلين عن بعض
        public decimal? FirstVisitPrice { get; set; }
        public decimal? FollowUpPrice { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Clinic? Clinic { get; set; }
        public Department? Department { get; set; }

        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // ══════════════════════════════════════
    // الخطة العلاجية الفعلية لمريض معين
    // ══════════════════════════════════════
    public class TreatmentPlan : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public Guid PatientId { get; set; }
        public Guid? DoctorId { get; set; }
        public Guid? TemplateId { get; set; }            // null = خطة مخصصة يدوية

        public string Name { get; set; } = "";
        public int TotalSessions { get; set; } = 1;

        public string PricingType { get; set; } = "per_session";  // per_session / total
        public decimal? PricePerSession { get; set; }
        public decimal? TotalPrice { get; set; }

        public string PaymentType { get; set; } = "per_session";  // per_session / upfront

        public string Status { get; set; } = "active";  // active / completed / cancelled / paused
        public DateTime StartDate { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Clinic? Clinic { get; set; }
        public Patient? Patient { get; set; }
        public Doctor? Doctor { get; set; }
        public TreatmentPlanTemplate? Template { get; set; }
        public ICollection<TreatmentSession> Sessions { get; set; } = new List<TreatmentSession>();

        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // ══════════════════════════════════════
    // كل جلسة فعلية بالخطة العلاجية
    // ══════════════════════════════════════
    public class TreatmentSession : IAuditable
    {
        public Guid Id { get; set; }
        public Guid TreatmentPlanId { get; set; }
        public int SessionNumber { get; set; }            // 1، 2، 3...

        public Guid? AppointmentId { get; set; }           // مرتبطة بموعد فعلي (اختياري)
        public DateTime? ScheduledDate { get; set; }        // أو مجرد تاريخ متوقع بدون موعد رسمي

        public string Status { get; set; } = "scheduled";  // scheduled / completed / missed / cancelled
        public DateTime? CompletedAt { get; set; }
        public decimal? Cost { get; set; }                  // ممكن يختلف عن الافتراضي
        public bool IsPaid { get; set; } = false;
        public string? Notes { get; set; }

        public bool IsDeleted { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public TreatmentPlan? TreatmentPlan { get; set; }
        public Appointment? Appointment { get; set; }

        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // ══════════════════════════════════════
    // إعدادات الطبيب المالية لكل قالب — سطر واحد بـ TemplateId=null يمثل
    // "الإعداد العام" لهذا الطبيب (يطبّق على كل القوالب)، وأي سطر بـ TemplateId
    // محدد يمثل استثناء خاص لقالب بعينه (يتجاوز الإعداد العام + سعر القالب نفسه)
    // ══════════════════════════════════════
    public class DoctorTemplateSetting : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public Guid DoctorId { get; set; }
        public Guid? TemplateId { get; set; }   // null = إعداد عام لكل قوالب هذا الطبيب

        // ✅ سعر خاص بهذا الطبيب — لو فاضي، نستخدم سعر القالب العام (FirstVisitPrice/FollowUpPrice)
        public decimal? FirstVisitPrice { get; set; }
        public decimal? FollowUpPrice { get; set; }

        // ✅ حصة الطبيب
        public string CommissionType { get; set; } = "percentage";  // percentage / fixed
        public decimal? FirstVisitCommissionRate { get; set; }
        public decimal? FollowUpCommissionRate { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Clinic? Clinic { get; set; }
        public Doctor? Doctor { get; set; }
        public TreatmentPlanTemplate? Template { get; set; }

        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // ══════════════════════════════════════
    // بنود الفاتورة / أنواع الزيارة الفعلية — سطر لكل بند على نفس الموعد
    // ══════════════════════════════════════
    public class AppointmentVisitType : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public Guid AppointmentId { get; set; }
        public Guid TemplateId { get; set; }

        public decimal Price { get; set; }
        public decimal InsuranceRate { get; set; }
        public decimal InsuranceAmount { get; set; }

        public bool IsDeleted { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Clinic? Clinic { get; set; }
        public Appointment? Appointment { get; set; }
        public TreatmentPlanTemplate? Template { get; set; }

        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }


    // ══════════════════════════════════════
    // الفاتورة الضريبية — 388 فاتورة بيع، 381 إشعار دائن (مرتجع)
    // ══════════════════════════════════════
    public class Invoice : IAuditable
    {
        public Guid Id { get; set; }
        public Guid ClinicId { get; set; }
        public Guid PatientId { get; set; }

        public string InvoiceNumber { get; set; } = "";
        public string DocumentType { get; set; } = "388";   // 388 بيع | 381 مرتجع
        public DateTime IssueDate { get; set; } = DateTime.UtcNow;

        // ✅ مصدر الفاتورة: دفعة (لفاتورة البيع) أو فاتورة بيع (للمرتجع)
        public Guid? PaymentDetailId { get; set; }
        public Guid? SourceInvoiceId { get; set; }          // SourceBill

        public decimal TotalAmount { get; set; }            // قبل الخصم، بدون ضريبة
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal PayableAmount { get; set; }
        public string? Notes { get; set; }

        // ✅ الترحيل الضريبي (JoFotara)
        public bool IsSubmitted { get; set; } = false;
        public DateTime? SubmittedAt { get; set; }
        public string? InvoiceXml { get; set; }
        public string? TaxResponse { get; set; }
        public string? QrCode { get; set; }

        public bool IsDeleted { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Clinic? Clinic { get; set; }
        public Patient? Patient { get; set; }
        public PaymentDetail? PaymentDetail { get; set; }
        public Invoice? SourceInvoice { get; set; }
        public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
        // ✅ نمط الضريبة لهذه الفاتورة — 1 خاضع | 2 غير خاضع | 3 معفي | 4 تصدير | 5 خاضع بنسبة صفر
public int TaxMethod { get; set; } = 2;
public decimal TaxRate { get; set; }   // النسبة المطبّقة (0 لغير الخاضع)
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // ✅ بند الفاتورة — للمرتجع يشير لبند فاتورة البيع (SourceRowGuide)
    public class InvoiceItem : IAuditable
    {
        public Guid Id { get; set; }
        public Guid InvoiceId { get; set; }
        public Guid? TemplateId { get; set; }
        public Guid? SourceItemId { get; set; }             // بند فاتورة البيع، للمرتجع

        public string Name { get; set; } = "";
        public decimal Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; }
        public decimal Discount { get; set; }
        public decimal TaxRate { get; set; }
        public decimal TaxAmount { get; set; }
        public string TaxType { get; set; } = "O";          // S / O / Z

        public bool IsDeleted { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Invoice? Invoice { get; set; }
        public TreatmentPlanTemplate? Template { get; set; }
        public InvoiceItem? SourceItem { get; set; }

        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}