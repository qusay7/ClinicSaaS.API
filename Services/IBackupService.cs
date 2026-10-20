namespace ClinicSaaS.API.Services
{
    public interface IBackupService
    {
        // ينشئ نسخة احتياطية (BACKUP DATABASE) ويرفعها لـ Azure Blob Storage،
        // ثم يحذف النسخ الأقدم من RetentionDays. يرجع اسم الملف المرفوع.
        Task<string> RunBackupAsync();
    }
}
