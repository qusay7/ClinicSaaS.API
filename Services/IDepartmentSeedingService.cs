// Services/DepartmentSeedingService.cs
using ClinicSaaS.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Services
{
    public interface IDepartmentSeedingService
    {
        Task<int> SeedDefaultDepartments(Guid clinicId);
    }

    public class DepartmentSeedingService : IDepartmentSeedingService
    {
        private readonly ApplicationDbContext _db;

        public DepartmentSeedingService(ApplicationDbContext db)
        {
            _db = db;
        }

        private static readonly (string Name, string NameEn)[] DefaultDepartments = new[]
        {
            ("الاستقبال", "Reception"),
            ("طب عام", "General medicine"),
            ("طوارء", "Emergency"),
            ("الأسنان",   "Dentistry"),
            ("الأطفال",   "Pediatrics"),
            ("العيون",    "Ophthalmology"),
            ("المحاسبة",  "Accounting"),
            ("الإدارة",   "Administration"),
            ("المختبر",   "Laboratory"),
            ("الأشعة",    "Radiology"),
            ("تمريض",     "Nursing"),
            ("صيدله",     "Pharmacy"),
        };

        public async Task<int> SeedDefaultDepartments(Guid clinicId)
        {
            int added = 0;

            foreach (var dept in DefaultDepartments)
            {
                var exists = await _db.Departments
                    .AnyAsync(d => d.Name == dept.Name && d.ClinicId == clinicId);
                if (exists) continue;

                _db.Departments.Add(new Department
                {
                    Id = Guid.NewGuid(),
                    Name = dept.Name,
                    NameEn = dept.NameEn,
                    ClinicId = clinicId,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                });
                added++;
            }

            await _db.SaveChangesAsync();
            return added;
        }
    }
}