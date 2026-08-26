using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentVisitTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppointmentVisitTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
                    InsuranceRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    InsuranceAmount = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentVisitTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentVisitTypes_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AppointmentVisitTypes_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AppointmentVisitTypes_TreatmentPlanTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "TreatmentPlanTemplates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentVisitTypes_AppointmentId",
                table: "AppointmentVisitTypes",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentVisitTypes_ClinicId",
                table: "AppointmentVisitTypes",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentVisitTypes_TemplateId",
                table: "AppointmentVisitTypes",
                column: "TemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentVisitTypes");
        }
    }
}
