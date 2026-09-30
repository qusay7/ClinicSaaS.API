using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitNoteVitals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BloodPressure",
                table: "VisitNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BloodSugar",
                table: "VisitNotes",
                type: "decimal(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeartRate",
                table: "VisitNotes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RespiratoryRate",
                table: "VisitNotes",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BloodPressure",
                table: "VisitNotes");

            migrationBuilder.DropColumn(
                name: "BloodSugar",
                table: "VisitNotes");

            migrationBuilder.DropColumn(
                name: "HeartRate",
                table: "VisitNotes");

            migrationBuilder.DropColumn(
                name: "RespiratoryRate",
                table: "VisitNotes");
        }
    }
}
