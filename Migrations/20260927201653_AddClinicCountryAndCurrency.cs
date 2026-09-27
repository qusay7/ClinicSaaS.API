using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicCountryAndCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ✅ كل العيادات الموجودة قبل هذه الميزة كانت أردنية أصلاً (النظام كان يفترض
            // JOD/JoFotara بكل مكان) — القيمة الافتراضية "JO"/"JOD" لا نص فاضٍ
            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Clinics",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "JO");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Clinics",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "JOD");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Country",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Clinics");
        }
    }
}
