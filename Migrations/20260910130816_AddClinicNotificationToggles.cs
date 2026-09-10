using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicNotificationToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NotifyBefore12h",
                table: "Clinics",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyBefore1h",
                table: "Clinics",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnCancel",
                table: "Clinics",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnCreate",
                table: "Clinics",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnEdit",
                table: "Clinics",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotifyBefore12h",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "NotifyBefore1h",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "NotifyOnCancel",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "NotifyOnCreate",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "NotifyOnEdit",
                table: "Clinics");
        }
    }
}
