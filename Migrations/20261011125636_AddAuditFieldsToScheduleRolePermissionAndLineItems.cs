using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditFieldsToScheduleRolePermissionAndLineItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "VisitProcedureItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "VisitProcedureItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedBy",
                table: "VisitProcedureItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Roles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "RolePermissions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Permissions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "DoctorSchedules",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "DiagnosisMedications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "DiagnosisMedications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "DiagnosisMedications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedBy",
                table: "DiagnosisMedications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "DepartmentRoles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "ClinicSchedules",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "VisitProcedureItems");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "VisitProcedureItems");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "VisitProcedureItems");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "RolePermissions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Permissions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "DoctorSchedules");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "DiagnosisMedications");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "DiagnosisMedications");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "DiagnosisMedications");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "DiagnosisMedications");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "DepartmentRoles");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "ClinicSchedules");
        }
    }
}
