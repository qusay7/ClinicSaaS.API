using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorCommissionSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FirstVisitPrice",
                table: "TreatmentPlanTemplates",
                type: "decimal(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FollowUpPrice",
                table: "TreatmentPlanTemplates",
                type: "decimal(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DoctorCommissionAmount",
                table: "Appointments",
                type: "decimal(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DoctorTemplateSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FirstVisitPrice = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    FollowUpPrice = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    CommissionType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FirstVisitCommissionRate = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    FollowUpCommissionRate = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorTemplateSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorTemplateSettings_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorTemplateSettings_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorTemplateSettings_TreatmentPlanTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "TreatmentPlanTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorTemplateSettings_ClinicId",
                table: "DoctorTemplateSettings",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorTemplateSettings_Doctor_GeneralOnly",
                table: "DoctorTemplateSettings",
                column: "DoctorId",
                unique: true,
                filter: "[TemplateId] IS NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorTemplateSettings_Doctor_Template",
                table: "DoctorTemplateSettings",
                columns: new[] { "DoctorId", "TemplateId" },
                unique: true,
                filter: "[TemplateId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorTemplateSettings_TemplateId",
                table: "DoctorTemplateSettings",
                column: "TemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoctorTemplateSettings");

            migrationBuilder.DropColumn(
                name: "FirstVisitPrice",
                table: "TreatmentPlanTemplates");

            migrationBuilder.DropColumn(
                name: "FollowUpPrice",
                table: "TreatmentPlanTemplates");

            migrationBuilder.DropColumn(
                name: "DoctorCommissionAmount",
                table: "Appointments");
        }
    }
}
