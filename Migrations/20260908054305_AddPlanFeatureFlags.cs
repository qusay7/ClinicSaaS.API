using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasElectronicInvoicing",
                table: "Plans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasMultipleDepartments",
                table: "Plans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // ✅ يطابق نص الميزات الموجود فعلياً بكل خطة — "فواتير إلكترونية" و"أقسام
            // متعددة" مذكورتين بـ Standard وPremium بس، Basic ما تشملهم
            migrationBuilder.Sql(
                "UPDATE Plans SET HasElectronicInvoicing = 1, HasMultipleDepartments = 1 WHERE Name IN ('Standard', 'Premium')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasElectronicInvoicing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "HasMultipleDepartments",
                table: "Plans");
        }
    }
}
