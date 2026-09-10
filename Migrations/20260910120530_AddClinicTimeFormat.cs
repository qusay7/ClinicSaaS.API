using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicTimeFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeFormat",
                table: "Clinics",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "24");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeFormat",
                table: "Clinics");
        }
    }
}
