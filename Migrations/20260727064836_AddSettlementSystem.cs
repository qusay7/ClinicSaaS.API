using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSettlementSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SettlementId",
                table: "InsuranceClaims",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CommissionSettlementId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Settlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InsuranceCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Settlements_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Settlements_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Settlements_InsuranceCompanies_InsuranceCompanyId",
                        column: x => x.InsuranceCompanyId,
                        principalTable: "InsuranceCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InsuranceClaims_SettlementId",
                table: "InsuranceClaims",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CommissionSettlementId",
                table: "Appointments",
                column: "CommissionSettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_ClinicId",
                table: "Settlements",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_DoctorId",
                table: "Settlements",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_InsuranceCompanyId",
                table: "Settlements",
                column: "InsuranceCompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Settlements_CommissionSettlementId",
                table: "Appointments",
                column: "CommissionSettlementId",
                principalTable: "Settlements",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InsuranceClaims_Settlements_SettlementId",
                table: "InsuranceClaims",
                column: "SettlementId",
                principalTable: "Settlements",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Settlements_CommissionSettlementId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_InsuranceClaims_Settlements_SettlementId",
                table: "InsuranceClaims");

            migrationBuilder.DropTable(
                name: "Settlements");

            migrationBuilder.DropIndex(
                name: "IX_InsuranceClaims_SettlementId",
                table: "InsuranceClaims");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_CommissionSettlementId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "SettlementId",
                table: "InsuranceClaims");

            migrationBuilder.DropColumn(
                name: "CommissionSettlementId",
                table: "Appointments");
        }
    }
}
