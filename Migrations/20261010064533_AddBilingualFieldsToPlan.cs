using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddBilingualFieldsToPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescriptionEn",
                table: "Plans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeaturesTextEn",
                table: "Plans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameEn",
                table: "Plans",
                type: "nvarchar(max)",
                nullable: true);

            // ✅ تعبئة النسخة الإنجليزية للخطط الافتراضية الثلاث الموجودة فعلياً (Basic/Standard/Premium)
            // بدل ما تضل فاضية للأبد لحد ما أحد يدخل يعدّلها يدوياً من لوحة السوبر أدمن
            migrationBuilder.Sql(@"
UPDATE Plans SET DescriptionEn = N'For small clinics',
    FeaturesTextEn = N'Appointment Scheduling' + CHAR(10) + N'Visit Notes' + CHAR(10) + N'Basic Reports'
    WHERE Name = N'Basic' AND DescriptionEn IS NULL;

UPDATE Plans SET DescriptionEn = N'For medium clinics',
    FeaturesTextEn = N'Everything in Basic' + CHAR(10) + N'Electronic Invoicing' + CHAR(10) + N'Multiple Departments' + CHAR(10) + N'Priority Support'
    WHERE Name = N'Standard' AND DescriptionEn IS NULL;

UPDATE Plans SET DescriptionEn = N'For large clinics',
    FeaturesTextEn = N'Everything in Standard' + CHAR(10) + N'Unlimited Users & Doctors' + CHAR(10) + N'Dedicated Account Manager' + CHAR(10) + N'Free Team Training'
    WHERE Name = N'Premium' AND DescriptionEn IS NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescriptionEn",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "FeaturesTextEn",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "NameEn",
                table: "Plans");
        }
    }
}
