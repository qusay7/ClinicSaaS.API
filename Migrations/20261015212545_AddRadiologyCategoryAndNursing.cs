using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddRadiologyCategoryAndNursing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // كل الطلبات الموجودة اليوم هي طلبات مختبر فعلية (الأشعة لم تكن موجودة
            // كخيار قبل هذي الميجريشن) — defaultValue "lab" صحيح 100%، لا "" عشوائي
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "LabOrders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "lab");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "LabOrders");
        }
    }
}
