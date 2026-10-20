using Azure.Storage.Blobs;
using Microsoft.Data.SqlClient;

namespace ClinicSaaS.API.Services
{
    // ✅ نسخ احتياطي حقيقي (BACKUP DATABASE على نفس السيرفر، لا حاجة لصلاحية
    // SQL Agent غير المتوفرة بإصدار Express) + رفعه لـ Azure Blob Storage +
    // تنظيف النسخ الأقدم من RetentionDays. معطَّل افتراضياً عبر
    // BackupSettings:Enabled حتى يُفعَّل يدوياً (انظر BackupBackgroundService).
    public class BackupService : IBackupService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<BackupService> _logger;

        public BackupService(IConfiguration config, ILogger<BackupService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<string> RunBackupAsync()
        {
            var connStr = _config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
                throw new InvalidOperationException("ConnectionStrings:DefaultConnection غير مُعدّ");

            var azureConnStr = _config["BackupSettings:AzureConnectionString"];
            if (string.IsNullOrWhiteSpace(azureConnStr))
                throw new InvalidOperationException("BackupSettings:AzureConnectionString غير مُعدّ — أضفه قبل التفعيل");

            var containerName = _config["BackupSettings:ContainerName"];
            if (string.IsNullOrWhiteSpace(containerName)) containerName = "clinicsaas-backups";

            var dbName = new SqlConnectionStringBuilder(connStr).InitialCatalog;
            if (string.IsNullOrWhiteSpace(dbName))
                throw new InvalidOperationException("تعذّر استخراج اسم قاعدة البيانات من سلسلة الاتصال");

            var fileName = $"{dbName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.bak";
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);

            _logger.LogInformation("🔄 Starting backup of database {Db} to {Path}", dbName, tempPath);

            await using (var connection = new SqlConnection(connStr))
            {
                await connection.OpenAsync();
                await using var cmd = connection.CreateCommand();
                // ✅ 10 دقائق مهلة — قاعدة البيانات ستكبر مع الوقت، النسخ الافتراضي
                // (30 ثانية) غير كافٍ حتى لحجم متوسط
                cmd.CommandTimeout = 600;
                // ✅ اسم القاعدة لا يُمرَّر كمعامل بـ BACKUP DATABASE (T-SQL لا يسمح
                // بمعاملات للمعرّفات)، فنتحقق أولاً أنه لا يحتوي أي حرف خارج النطاق
                // الآمن قبل دمجه مباشرة بنص الأمر
                if (!System.Text.RegularExpressions.Regex.IsMatch(dbName, @"^[A-Za-z0-9_]+$"))
                    throw new InvalidOperationException("اسم قاعدة البيانات يحتوي رموزاً غير متوقعة - أُلغي النسخ الاحتياطي احتياطاً");
                cmd.CommandText = $"BACKUP DATABASE [{dbName}] TO DISK = @path WITH COMPRESSION, INIT, COPY_ONLY";
                cmd.Parameters.Add(new SqlParameter("@path", tempPath));
                await cmd.ExecuteNonQueryAsync();
            }

            try
            {
                var containerClient = new BlobContainerClient(azureConnStr, containerName);
                await containerClient.CreateIfNotExistsAsync();
                var blobClient = containerClient.GetBlobClient(fileName);

                await using (var fs = File.OpenRead(tempPath))
                {
                    await blobClient.UploadAsync(fs, overwrite: true);
                }

                _logger.LogInformation("✅ Backup uploaded to Azure Blob Storage: {FileName}", fileName);

                await CleanupOldBackupsAsync(containerClient);

                return fileName;
            }
            finally
            {
                // ✅ احذف الملف المحلي دائماً (نجح الرفع أو فشل) - لا داعي يتراكم
                // على قرص السيرفر
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private async Task CleanupOldBackupsAsync(BlobContainerClient containerClient)
        {
            var retentionDays = _config.GetValue<int?>("BackupSettings:RetentionDays") ?? 30;
            var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

            await foreach (var blob in containerClient.GetBlobsAsync())
            {
                if (blob.Properties.CreatedOn.HasValue && blob.Properties.CreatedOn.Value < cutoff)
                {
                    await containerClient.DeleteBlobIfExistsAsync(blob.Name);
                    _logger.LogInformation("🗑️ Deleted old backup beyond retention ({Days} days): {Name}", retentionDays, blob.Name);
                }
            }
        }
    }
}
