using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameEn",
                table: "Roles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameEn",
                table: "Departments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VisitNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QueueEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Diagnosis = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Prescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tests = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NextVisitDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisitNotes_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VisitNotes_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VisitNotes_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VisitNotes_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VisitNotes_QueueEntries_QueueEntryId",
                        column: x => x.QueueEntryId,
                        principalTable: "QueueEntries",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_DepartmentId",
                table: "Users",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitNotes_AppointmentId",
                table: "VisitNotes",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitNotes_ClinicId",
                table: "VisitNotes",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitNotes_DoctorId",
                table: "VisitNotes",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitNotes_PatientId",
                table: "VisitNotes",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitNotes_QueueEntryId",
                table: "VisitNotes",
                column: "QueueEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Departments_DepartmentId",
                table: "Users",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Departments_DepartmentId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "VisitNotes");

            migrationBuilder.DropIndex(
                name: "IX_Users_DepartmentId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NameEn",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "NameEn",
                table: "Departments");
        }
    }
}
