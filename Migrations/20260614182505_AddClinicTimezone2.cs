using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicTimezone2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "Clinics",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "Clinics");
        }
    }
}
