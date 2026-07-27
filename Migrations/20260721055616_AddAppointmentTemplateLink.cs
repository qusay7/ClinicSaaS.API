using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentTemplateLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TemplateId",
                table: "Appointments",
                column: "TemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_TreatmentPlanTemplates_TemplateId",
                table: "Appointments",
                column: "TemplateId",
                principalTable: "TreatmentPlanTemplates",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_TreatmentPlanTemplates_TemplateId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_TemplateId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "Appointments");
        }
    }
}
