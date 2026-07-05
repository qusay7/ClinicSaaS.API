using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InsuranceClaims_Appointments_AppointmentId",
                table: "InsuranceClaims");

            migrationBuilder.DropIndex(
                name: "IX_InsuranceClaims_AppointmentId",
                table: "InsuranceClaims");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_InsuranceClaims_AppointmentId",
                table: "InsuranceClaims",
                column: "AppointmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_InsuranceClaims_Appointments_AppointmentId",
                table: "InsuranceClaims",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
