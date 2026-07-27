using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentUniqueAppointmentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentDetails_AppointmentId",
                table: "PaymentDetails");

            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicId",
                table: "Patients");

            migrationBuilder.RenameColumn(
                name: "IsDeleted ",
                table: "VisitNotes",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "IsDeleted ",
                table: "QueueEntries",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "IsDeleted ",
                table: "Patients",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "IsDeleted ",
                table: "Doctors",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "IsDeleted ",
                table: "Appointments",
                newName: "IsDeleted");

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Staff",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "PaymentDetails",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PaymentDetails",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AlterColumn<string>(
                name: "NationalId",
                table: "Patients",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "PatientInsurances",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "InsuranceCompanies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "InsuranceClaims",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "InsuranceClaims",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AlterColumn<string>(
                name: "Subdomain",
                table: "Clinics",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Appointments",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Absences",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true,
                filter: "[IsDeleted] = 0 AND [Username] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentDetails_AppointmentId",
                table: "PaymentDetails",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicId_NationalId",
                table: "Patients",
                columns: new[] { "ClinicId", "NationalId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [NationalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicId_PatientNumber",
                table: "Patients",
                columns: new[] { "ClinicId", "PatientNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_Subdomain",
                table: "Clinics",
                column: "Subdomain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_PaymentDetails_AppointmentId",
                table: "PaymentDetails");

            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicId_NationalId",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicId_PatientNumber",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Clinics_Subdomain",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "PaymentDetails");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PaymentDetails");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "PatientInsurances");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "InsuranceCompanies");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "InsuranceClaims");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "InsuranceClaims");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Absences");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "VisitNotes",
                newName: "IsDeleted ");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "QueueEntries",
                newName: "IsDeleted ");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "Patients",
                newName: "IsDeleted ");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "Doctors",
                newName: "IsDeleted ");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "Appointments",
                newName: "IsDeleted ");

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "NationalId",
                table: "Patients",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Subdomain",
                table: "Clinics",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentDetails_AppointmentId",
                table: "PaymentDetails",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicId",
                table: "Patients",
                column: "ClinicId");
        }
    }
}
