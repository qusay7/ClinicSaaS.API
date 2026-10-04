using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddProceduresAndEmergencyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmountPaid",
                table: "QueueEntries",
                type: "decimal(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "QueueEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DischargedAt",
                table: "QueueEntries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DischargedBy",
                table: "QueueEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                table: "QueueEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "QueueEntries",
                type: "decimal(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Procedures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DefaultPrice = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Procedures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Procedures_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VisitProcedureItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QueueEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitProcedureItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisitProcedureItems_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VisitProcedureItems_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VisitProcedureItems_Procedures_ProcedureId",
                        column: x => x.ProcedureId,
                        principalTable: "Procedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VisitProcedureItems_QueueEntries_QueueEntryId",
                        column: x => x.QueueEntryId,
                        principalTable: "QueueEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_DepartmentId",
                table: "QueueEntries",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Procedures_ClinicId",
                table: "Procedures",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitProcedureItems_AppointmentId",
                table: "VisitProcedureItems",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitProcedureItems_DoctorId",
                table: "VisitProcedureItems",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitProcedureItems_ProcedureId",
                table: "VisitProcedureItems",
                column: "ProcedureId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitProcedureItems_QueueEntryId",
                table: "VisitProcedureItems",
                column: "QueueEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_QueueEntries_Departments_DepartmentId",
                table: "QueueEntries",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QueueEntries_Departments_DepartmentId",
                table: "QueueEntries");

            migrationBuilder.DropTable(
                name: "VisitProcedureItems");

            migrationBuilder.DropTable(
                name: "Procedures");

            migrationBuilder.DropIndex(
                name: "IX_QueueEntries_DepartmentId",
                table: "QueueEntries");

            migrationBuilder.DropColumn(
                name: "AmountPaid",
                table: "QueueEntries");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "QueueEntries");

            migrationBuilder.DropColumn(
                name: "DischargedAt",
                table: "QueueEntries");

            migrationBuilder.DropColumn(
                name: "DischargedBy",
                table: "QueueEntries");

            migrationBuilder.DropColumn(
                name: "IsPaid",
                table: "QueueEntries");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "QueueEntries");
        }
    }
}
