using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddShowOnPricingPageToPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ✅ القيمة الافتراضية true (مش false) — عشان الخطط الموجودة
            // أصلاً بقاعدة البيانات تبقى ظاهرة بصفحة الأسعار العامة بعد
            // الترحيل، بدل ما تختفي كلها فجأة
            migrationBuilder.AddColumn<bool>(
                name: "ShowOnPricingPage",
                table: "Plans",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowOnPricingPage",
                table: "Plans");
        }
    }
}
